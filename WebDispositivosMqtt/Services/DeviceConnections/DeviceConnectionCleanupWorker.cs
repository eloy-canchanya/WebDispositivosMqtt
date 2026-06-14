namespace WebDispositivosMqtt.Services.Devices
{
    public class DeviceConnectionCleanupWorker : BackgroundService
    {
        private readonly IDeviceConnectionService _service;
        private readonly TimeSpan antiguedad;
        private readonly TimeSpan periodo;

        public DeviceConnectionCleanupWorker(IDeviceConnectionService service, IConfiguration configuration)
        {
            _service = service;
            antiguedad = TimeSpan.FromSeconds(configuration.GetValue<int>("DeviceConnectionCleanup:AntiguedadSeconds", 20));
            periodo    = TimeSpan.FromSeconds(configuration.GetValue<int>("DeviceConnectionCleanup:PeriodoSeconds", 30));
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
