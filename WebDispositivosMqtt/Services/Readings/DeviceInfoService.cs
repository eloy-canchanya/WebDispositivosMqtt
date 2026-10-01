using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WebDispositivosMqtt.Data;

namespace WebDispositivosMqtt.Services.Readings;

// devices/{mac}/info: lo que el equipo dice de sí mismo. Hoy sólo
// {"readings": ["level", "battery"]}, que se guarda en Devices.Readings.
// Es retained, así que llega también cada vez que el backend se reconecta.
public interface IDeviceInfoService
{
    Task ProcessAsync(string mac, string payload);
}

public class DeviceInfoService(DatabaseContext db, ILogger<DeviceInfoService> logger) : IDeviceInfoService
{
    public async Task ProcessAsync(string mac, string payload)
    {
        // Un retained vacío es alguien borrándolo a mano: nada que guardar
        if (string.IsNullOrWhiteSpace(payload))
            return;

        List<string?> readings;
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (!doc.RootElement.TryGetProperty("readings", out var array) || array.ValueKind != JsonValueKind.Array)
            {
                logger.LogWarning("Info de {Mac} sin \"readings\": {Payload}", mac, payload);
                return;
            }
            readings = array.EnumerateArray()
                .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : null)
                .ToList();
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Info de {Mac}: JSON inválido: {Payload}", mac, payload);
            return;
        }

        if (!readings.All(DeviceReadings.IsValidName))
        {
            logger.LogWarning("Info de {Mac}: nombre de lectura inválido: {Payload}", mac, payload);
            return;
        }

        var stored = DeviceReadings.Format(readings.Select(r => r!));
        if (stored.Length > DeviceReadings.MaxStoredLength)
        {
            logger.LogWarning("Info de {Mac}: demasiadas lecturas: {Payload}", mac, payload);
            return;
        }

        var updated = await db.Devices
            .Where(d => d.MacAddress == mac && d.Readings != stored)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.Readings, stored));

        if (updated > 0)
            logger.LogInformation("Lecturas de {Mac}: {Readings}", mac, stored);
    }
}
