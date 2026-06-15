namespace WebDispositivosMqtt.Services.Telemetria;

public interface ITelemetriaService
{
    Task ProcesarAsync(string mac, string topic, string payload);
}
