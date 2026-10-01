using WebDispositivosMqtt.Services.Commands;

namespace WebDispositivosMqtt.Services.Readings;

// Espera de un "medir" en curso, una por equipo. La usa POST
// api/devices/{id}/refresh: la solicitud queda abierta y la completa el listener
// MQTT al procesar cada lectura o al recibir el ack, así responde apenas llega
// lo último, sin consultar la BD mientras tanto.
//
// Vive en memoria: supone una sola instancia del backend, igual que
// CommandAckService (ver contratos/api-rest.md).
public interface IMeasurementWaiter
{
    // La espera en curso de esa MAC, o una nueva. IsNew = quien llama tiene que
    // registrar y publicar "medir"; si no, otra solicitud ya lo hizo y ésta se
    // suma a la misma respuesta.
    (MeasurementWait Wait, bool IsNew) Begin(string mac, IReadOnlyCollection<string> expected, TimeSpan timeout);

    void OnAck(string mac, CommandRecord record);
}

public static class RefreshResult
{
    public const string Fresh = "fresh";
    public const string Cached = "cached";
    public const string Offline = "offline";
    public const string Unsupported = "unsupported";
    public const string Timeout = "timeout";
}

public sealed class MeasurementWait
{
    private readonly object _lock = new();
    private readonly TaskCompletionSource<string> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly HashSet<string> _arrived = [];
    private HashSet<string> _expected;

    internal MeasurementWait(IEnumerable<string> expected) => _expected = [.. expected];

    // commandId del "medir", para reconocer su ack. Lo fija quien recibe IsNew.
    public string? CommandId { get; set; }

    // fresh · unsupported · timeout
    public Task<string> Result => _result.Task;

    public bool Complete(string result) => _result.TrySetResult(result);

    internal void Arrived(string reading)
    {
        lock (_lock)
        {
            _arrived.Add(reading);
            CompleteIfAllArrived();
        }
    }

    // El ack de "medir" dice qué va a medir el equipo ("medir: ok level"), y
    // manda sobre lo guardado en Devices.Readings
    internal void Announced(IEnumerable<string> readings)
    {
        lock (_lock)
        {
            _expected = [.. readings];
            CompleteIfAllArrived();
        }
    }

    private void CompleteIfAllArrived()
    {
        if (_expected.IsSubsetOf(_arrived))
            _result.TrySetResult(RefreshResult.Fresh);
    }
}

public class MeasurementWaiter : IMeasurementWaiter
{
    private const string MedirOkPrefix = "medir: ok";

    private readonly Dictionary<string, MeasurementWait> _waits = [];

    public MeasurementWaiter(IReadingEvents readingEvents)
    {
        readingEvents.ReadingProcessed += OnReading;
    }

    public (MeasurementWait Wait, bool IsNew) Begin(string mac, IReadOnlyCollection<string> expected, TimeSpan timeout)
    {
        MeasurementWait wait;
        lock (_waits)
        {
            if (_waits.TryGetValue(mac, out var existing))
                return (existing, false);

            wait = new MeasurementWait(expected);
            _waits[mac] = wait;
        }

        // El tope corre aparte de la solicitud HTTP: si la app corta, la espera
        // se cierra igual y no queda colgada en el diccionario
        var timer = new CancellationTokenSource(timeout);
        timer.Token.Register(() => wait.Complete(RefreshResult.Timeout));
        wait.Result.ContinueWith(_ =>
        {
            lock (_waits)
            {
                if (_waits.TryGetValue(mac, out var current) && current == wait)
                    _waits.Remove(mac);
            }
            timer.Dispose();
        }, TaskScheduler.Default);

        return (wait, true);
    }

    public void OnAck(string mac, CommandRecord record)
    {
        var wait = Find(mac);
        if (wait is null || wait.CommandId != record.CommandId)
            return;

        // Firmware anterior a "medir": responde "error: comando desconocido"
        if (record.AckStatus == "error")
        {
            wait.Complete(RefreshResult.Unsupported);
            return;
        }

        // "medir: ok level,battery". Sólo "medir: ok" (firmware anterior a
        // "bat on|off") no anuncia nada: queda lo de Devices.Readings
        var response = record.Response?.Trim() ?? "";
        if (response.StartsWith(MedirOkPrefix) && response.Length > MedirOkPrefix.Length)
        {
            var announced = response[MedirOkPrefix.Length..]
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(DeviceReadings.IsValidName)
                .ToList();
            if (announced.Count > 0)
                wait.Announced(announced);
        }
    }

    private void OnReading(string mac, string subtype) => Find(mac)?.Arrived(subtype);

    private MeasurementWait? Find(string mac)
    {
        lock (_waits)
            return _waits.GetValueOrDefault(mac);
    }
}
