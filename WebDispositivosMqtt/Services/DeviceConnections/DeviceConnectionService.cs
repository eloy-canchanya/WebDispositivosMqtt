using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using WebDispositivosMqtt.Hubs;

namespace WebDispositivosMqtt.Services.Devices
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
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
        public LastSeenType? LastSeenType { get; set; }
    }

    public interface IDeviceConnectionService
    {
        Task EvaluateTopicAsync(string domain, string entityId, string resource, string acknowledge, string payload);
        Task CleanupExpiredAsync(TimeSpan ts);
        IReadOnlyCollection<DeviceConnectionState> GetAll();
    }

    public class DeviceConnectionService(IHubContext<DeviceConnectionsHub> hub) : IDeviceConnectionService
    {
        private readonly ConcurrentDictionary<string, DeviceConnectionState> _deviceConnections = new();

        public async Task EvaluateTopicAsync(string domain, string entityId, string resource, string acknowledge, string payload)
        {
            if (resource == "status" && (payload == "online" || payload == "offline"))
            {
                bool isOnline = payload == "online";
                var now = DateTime.UtcNow;

                var hadPreviousState = TryGet(entityId, out var existing);
                var previousIsOnline = hadPreviousState && existing.IsOnline;

                AddOrUpdate(entityId, isOnline, now, LastSeenType.Status);

                var shouldNotifyStatusChanged =
                    (!hadPreviousState && isOnline) ||
                    (hadPreviousState && previousIsOnline != isOnline);

                if (shouldNotifyStatusChanged)
                {
                    await DeviceConnectionsHub.NotifyStatusChangedAsync(hub, entityId, isOnline, now, LastSeenType.Status);
                }
            }
        }

        public async Task CleanupExpiredAsync(TimeSpan antiguedad)
        {
            var now = DateTime.UtcNow;

            foreach (var kvp in _deviceConnections)
            {
                if (now - kvp.Value.LastSeenUtc > antiguedad && _deviceConnections.TryRemove(kvp.Key, out var removed))
                {
                    await DeviceConnectionsHub.NotifyDeviceExpiredAsync(hub, removed!.MacAddress);
                }
            }
        }

        public IReadOnlyCollection<DeviceConnectionState> GetAll()
            => _deviceConnections.Values.ToList();

        private bool TryGet(string macAddress, out DeviceConnectionState device)
            => _deviceConnections.TryGetValue(macAddress, out device!);

        private DeviceConnectionState AddOrUpdate(string macAddress, bool isOnline, DateTime lastSeenUtc, LastSeenType lastSeenType)
        {
            return _deviceConnections.AddOrUpdate(
                macAddress,
                key => new DeviceConnectionState
                {
                    MacAddress = key,
                    IsOnline = isOnline,
                    LastSeenUtc = lastSeenUtc,
                    LastSeenType = lastSeenType
                },
                (_, existing) =>
                {
                    existing.IsOnline = isOnline;
                    existing.LastSeenUtc = lastSeenUtc;
                    existing.LastSeenType = lastSeenType;
                    return existing;
                }
            );
        }
    }
}
