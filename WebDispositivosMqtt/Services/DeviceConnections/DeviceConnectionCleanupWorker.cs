namespace WebDispositivosMqtt.Services.Devices
{
    public class DeviceConnectionCleanupWorker : BackgroundService
    {
        private readonly IDeviceConnectionService _service;
        private readonly TimeSpan antiguedad = TimeSpan.FromSeconds(20);
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
