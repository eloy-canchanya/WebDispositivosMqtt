using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using WebDispositivosMqtt.Hubs;

namespace WebDispositivosMqtt.Services.NewDevices
{
    public class NewDevice
    {
        public string TempId { get; set; } = default!; // MAC o derivado
        public DateTime LastSeen { get; set; }
        public string Status { get; set; } = "Unregistered";
    }


    // se crea la interfase para inyectarlo en todos lados
    public interface INewDevicesService
    {
        Task<NewDevice> AddOrUpdateAsync(string tempId);
        Task CleanupExpiredAsync(TimeSpan ts); // <- antes void
        IReadOnlyCollection<NewDevice> GetAll();
        void Remove(string tempId);
        bool TryGet(string tempId, out NewDevice device);
    }


    public class NewDevicesService : INewDevicesService
    {
        private readonly IHubContext<NewDevicesHub> _hubContext;
        private readonly ConcurrentDictionary<string, NewDevice> _devices = new();

        public NewDevicesService(IHubContext<NewDevicesHub> hubContext)
        {
            _hubContext = hubContext;
        }

        public async Task<NewDevice> AddOrUpdateAsync(string tempId)
        {
            var now = DateTime.UtcNow;
            string accion = default!;

            var device = _devices.AddOrUpdate(
                tempId,
                key =>
                {
                    accion = "created";
                    return new NewDevice
                    {
                        TempId = key,
                        LastSeen = now
                    };
                },
                (key, existing) =>
                {
                    accion = "updated";
                    existing.LastSeen = now;
                    return existing;
                });

            await _hubContext.Clients.All.SendAsync("NuevoDispositivo", new
            {
                accion,
                device
            });

            return device;
        }

        public IReadOnlyCollection<NewDevice> GetAll()
            => _devices.Values.ToList();

        public bool TryGet(string tempId, out NewDevice device)
            => _devices.TryGetValue(tempId, out device!);

        public void Remove(string tempId)
            => _devices.TryRemove(tempId, out _);

        public async Task CleanupExpiredAsync(TimeSpan ts)
        {
            var now = DateTime.UtcNow;

            foreach (var kvp in _devices)
            {
                if (now - kvp.Value.LastSeen > ts &&
                    _devices.TryRemove(kvp.Key, out var removed))
                {
                    await _hubContext.Clients.All.SendAsync("DispositivoExpirado", new
                    {
                        tempId = removed!.TempId
                    });
                }
            }
        }
    
    }


    public class NewDevicesCleanupWorker : BackgroundService
    {
        private readonly INewDevicesService _service;
        //private readonly TimeSpan _ttl = TimeSpan.FromMinutes(2);
        private readonly TimeSpan _ttl = TimeSpan.FromSeconds(30);

        public NewDevicesCleanupWorker(INewDevicesService service)
        {
            _service = service;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await _service.CleanupExpiredAsync(_ttl); // <- ahora async
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }

}


