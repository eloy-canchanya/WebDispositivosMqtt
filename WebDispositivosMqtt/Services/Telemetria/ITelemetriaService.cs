namespace WebDispositivosMqtt.Services.Telemetria;

public interface ITelemetriaService
{
    // true si el SP de telemetría corrió sin error (TelemetryLog.Processed)
    Task<bool> ProcesarAsync(string mac, string topic, string payload);
}
