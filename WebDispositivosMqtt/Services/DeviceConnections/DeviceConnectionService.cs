using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using WebDispositivosMqtt.Hubs;

namespace WebDispositivosMqtt.Services.Devices
{
    public enum LastSeenType
    {
        Status,
        Heartbeat,
        Telemetry
    }

    public class DeviceConnectionState
    {
        public string MacAddress { get; set; } = default!;

        public bool IsOnline { get; set; } = false;

        public DateTime LastSeenUtc { get; set; }
        public LastSeenType LastSeenType { get; set; }
    }


    public interface IDeviceConnectionService
    {
        Task EvaluateTopicAsync(string domain, string entityId, string resource, string acknowledge, string payload);
        Task CleanupExpiredAsync(TimeSpan ts);
        IReadOnlyCollection<DeviceConnectionState> GetAll();
    }


    public class DeviceConnectionService : IDeviceConnectionService
    {
        private readonly IHubContext<DeviceConnectionsHub> _deviceConnectionHub;
        private readonly ConcurrentDictionary<string, DeviceConnectionState> _deviceConnections = new();


        public DeviceConnectionService(IHubContext<DeviceConnectionsHub> deviceConnectionHub)
        {
            _deviceConnectionHub = deviceConnectionHub;
        }


        public async Task EvaluateTopicAsync(string domain, string entityId, string resource, string acknowledge, string payload)
        {
            if (resource == "status" && (payload == "online" || payload == "offline"))
            {
                bool isOnline = payload == "online";
                var now = DateTime.UtcNow;

                var hadPreviousState = TryGet(entityId, out var existing);
                var previousIsOnline = hadPreviousState && existing.IsOnline;

                await AddOrUpdateAsync(entityId, isOnline, now, LastSeenType.Status);

                var shouldNotifyStatusChanged =
                    (!hadPreviousState && isOnline) ||
                    (hadPreviousState && previousIsOnline != isOnline);

                if (shouldNotifyStatusChanged)
                {
                    await _deviceConnectionHub.Clients.All.SendAsync("EstadoDispositivoCambiado", new
                    {
                        macAddress = entityId,
                        isOnline,
                        status = isOnline ? "online" : "offline",
                        changedAtUtc = now
                    });


                }
            }

        }


        public async Task<DeviceConnectionState> AddOrUpdateAsync(string macAddress, bool isOnline, DateTime lastSeenUtc, LastSeenType lastSeenType)
        {
            string accion = default!;

            var connection = _deviceConnections.AddOrUpdate(
                macAddress,
                key =>
                {
                    accion = "created";
                    return new DeviceConnectionState
                    {
                        MacAddress = key,
                        IsOnline = isOnline,
                        LastSeenUtc = lastSeenUtc,
                        LastSeenType = lastSeenType
                    };
                },
                (key, existing) =>
                {
                    accion = "updated";
                    existing.IsOnline = isOnline;
                    existing.LastSeenUtc = lastSeenUtc;
                    existing.LastSeenType = lastSeenType;
                    return existing;
                }
            );

            await _deviceConnectionHub.Clients.All.SendAsync("NuevoDispositivo", new
            {
                accion,
                connection
            });

            return connection;
        }

        public IReadOnlyCollection<DeviceConnectionState> GetAll()
            => _deviceConnections.Values.ToList();

        public bool TryGet(string tempId, out DeviceConnectionState device)
            => _deviceConnections.TryGetValue(tempId, out device!);

        public void Remove(string tempId)
            => _deviceConnections.TryRemove(tempId, out _);

        public async Task CleanupExpiredAsync(TimeSpan antiguedad)
        {
            var now = DateTime.UtcNow;

            foreach (var kvp in _deviceConnections)
            {
                if (now - kvp.Value.LastSeenUtc > antiguedad && _deviceConnections.TryRemove(kvp.Key, out var removed))
                {
                    await _deviceConnectionHub.Clients.All.SendAsync("DispositivoExpirado", new
                    {
                        macAddress = removed!.MacAddress
                    });
                }
            }
        }

    }


    public class DeviceConnectionCleanupWorker : BackgroundService
    {
        private readonly IDeviceConnectionService _service;
        private readonly TimeSpan antiguedad = TimeSpan.FromSeconds(60);
        private readonly TimeSpan periodo = TimeSpan.FromSeconds(30);

        public DeviceConnectionCleanupWorker(IDeviceConnectionService service)
        {
            _service = service;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await _service.CleanupExpiredAsync(antiguedad);
                await Task.Delay(periodo, stoppingToken);
            }
        }
    }


}
