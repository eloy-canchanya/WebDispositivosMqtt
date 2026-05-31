using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using WebDispositivosMqtt.Hubs;

namespace WebDispositivosMqtt.Services.DeviceRequests
{
    public class DeviceRequestOptions
    {
        public int ActiveTtlSeconds { get; set; } = 300;
        public int HistoryTtlSeconds { get; set; } = 600;
        public int CleanupIntervalSeconds { get; set; } = 30;
    }

    public enum DeviceRequestStatus
    {
        Pending,
        Approved,
        Cancelled,
        Provisioned
    }

    public class DeviceRequest
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string MacAddress { get; set; } = default!;
        public string Keyword { get; set; } = default!;
        public DateTime CreatedAtUtc { get; set; }
        public DeviceRequestStatus Status { get; set; } = DeviceRequestStatus.Pending;
    }

    public interface IDeviceRequestService
    {
        Task AddAsync(Guid sessionId, string macAddress, string keyword, bool isRegistered);
        IReadOnlyCollection<DeviceRequest> GetAll();
        bool TryGet(Guid id, out DeviceRequest request);
        bool TryGetApproved(Guid sessionId, out DeviceRequest request);
        bool TryApprove(Guid id);
        Task<bool> CancelAsync(Guid id);
        Task<bool> MarkProvisionedAsync(Guid id);
        Task CleanupExpiredAsync(TimeSpan activeTtl, TimeSpan historyTtl);
    }

    public class DeviceRequestService : IDeviceRequestService
    {
        private readonly IHubContext<NewDeviceConnectionsHub> _hubContext;
        private readonly ConcurrentDictionary<Guid, DeviceRequest> _requests = new();

        public DeviceRequestService(IHubContext<NewDeviceConnectionsHub> hubContext)
        {
            _hubContext = hubContext;
        }

        public async Task AddAsync(Guid sessionId, string macAddress, string keyword, bool isRegistered)
        {
            // Mismo GUID: mismo dispositivo reintentando → refrescar TTL silenciosamente
            if (_requests.TryGetValue(sessionId, out var existing))
            {
                if (existing.Status == DeviceRequestStatus.Pending)
                    existing.CreatedAtUtc = DateTime.UtcNow;
                return;
            }

            // GUID nuevo: nueva sesión de provisioning
            var request = new DeviceRequest
            {
                Id = sessionId,
                MacAddress = macAddress,
                Keyword = keyword,
                CreatedAtUtc = DateTime.UtcNow,
                Status = DeviceRequestStatus.Pending
            };

            _requests.TryAdd(request.Id, request);

            await _hubContext.Clients.All.SendAsync("SolicitudCredencial", new
            {
                id = request.Id,
                macAddress = request.MacAddress,
                keyword = request.Keyword,
                createdAtUtc = request.CreatedAtUtc,
                status = request.Status.ToString(),
                isRegistered
            });
        }

        public IReadOnlyCollection<DeviceRequest> GetAll()
            => _requests.Values.ToList();

        public bool TryGet(Guid id, out DeviceRequest request)
            => _requests.TryGetValue(id, out request!);

        public bool TryGetApproved(Guid sessionId, out DeviceRequest request)
            => _requests.TryGetValue(sessionId, out request!) && request.Status == DeviceRequestStatus.Approved;

        public bool TryApprove(Guid id)
        {
            if (!_requests.TryGetValue(id, out var request) || request.Status != DeviceRequestStatus.Pending)
                return false;

            request.Status = DeviceRequestStatus.Approved;
            return true;
        }

        public async Task<bool> CancelAsync(Guid id)
        {
            if (!_requests.TryGetValue(id, out var request) ||
                request.Status == DeviceRequestStatus.Cancelled ||
                request.Status == DeviceRequestStatus.Provisioned)
                return false;

            request.Status = DeviceRequestStatus.Cancelled;

            await _hubContext.Clients.All.SendAsync("SolicitudCancelada", new
            {
                id = request.Id,
                macAddress = request.MacAddress
            });
            return true;
        }

        public async Task<bool> MarkProvisionedAsync(Guid id)
        {
            if (!_requests.TryGetValue(id, out var request) || request.Status != DeviceRequestStatus.Approved)
                return false;

            request.Status = DeviceRequestStatus.Provisioned;

            await _hubContext.Clients.All.SendAsync("SolicitudProvisionada", new
            {
                id = request.Id,
                macAddress = request.MacAddress
            });
            return true;
        }

        public async Task CleanupExpiredAsync(TimeSpan activeTtl, TimeSpan historyTtl)
        {
            var now = DateTime.UtcNow;

            foreach (var kvp in _requests)
            {
                var ttl = (kvp.Value.Status == DeviceRequestStatus.Pending ||
                           kvp.Value.Status == DeviceRequestStatus.Approved)
                    ? activeTtl
                    : historyTtl;

                if (now - kvp.Value.CreatedAtUtc > ttl &&
                    _requests.TryRemove(kvp.Key, out var removed))
                {
                    await _hubContext.Clients.All.SendAsync("SolicitudExpirada", new { id = removed!.Id });
                }
            }
        }
    }

    public class DeviceRequestCleanupWorker : BackgroundService
    {
        private readonly IDeviceRequestService _service;
        private readonly DeviceRequestOptions _options;

        public DeviceRequestCleanupWorker(IDeviceRequestService service, IOptions<DeviceRequestOptions> options)
        {
            _service = service;
            _options = options.Value;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var activeTtl = TimeSpan.FromSeconds(_options.ActiveTtlSeconds);
            var historyTtl = TimeSpan.FromSeconds(_options.HistoryTtlSeconds);
            var interval = TimeSpan.FromSeconds(_options.CleanupIntervalSeconds);

            while (!stoppingToken.IsCancellationRequested)
            {
                await _service.CleanupExpiredAsync(activeTtl, historyTtl);
                await Task.Delay(interval, stoppingToken);
            }
        }
    }
}
