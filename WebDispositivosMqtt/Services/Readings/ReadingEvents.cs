namespace WebDispositivosMqtt.Services.Readings;

// Aviso de "se guardó una lectura de tal equipo", que dispara el listener MQTT
// cuando el SP de telemetría terminó sin error. Hoy lo escucha el refresh
// (MeasurementWaiter); una vista en vivo (SignalR o SSE) se colgaría acá.
public interface IReadingEvents
{
    // (mac, subtipo de tel/{subtipo})
    event Action<string, string>? ReadingProcessed;

    void Publish(string mac, string subtype);
}

public class ReadingEvents(ILogger<ReadingEvents> logger) : IReadingEvents
{
    public event Action<string, string>? ReadingProcessed;

    public void Publish(string mac, string subtype)
    {
        // Un suscriptor que falla no puede cortar la ingesta del listener
        foreach (var handler in ReadingProcessed?.GetInvocationList() ?? [])
        {
            try
            {
                ((Action<string, string>)handler)(mac, subtype);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "ReadingProcessed: falló un suscriptor. MAC: {Mac}, subtipo: {Subtype}", mac, subtype);
            }
        }
    }
}
