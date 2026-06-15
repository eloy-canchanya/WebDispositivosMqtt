using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Text.RegularExpressions;
using WebDispositivosMqtt.Data;
using WebDispositivosMqtt.Data.Models;

namespace WebDispositivosMqtt.Services.Telemetria;

public class TelemetriaService : ITelemetriaService
{
    private static readonly Regex SpNameRegex = new(@"^[a-zA-Z_][a-zA-Z0-9_]*$", RegexOptions.Compiled);

    private readonly DatabaseContext _context;
    private readonly ILogger<TelemetriaService> _logger;

    public TelemetriaService(DatabaseContext context, ILogger<TelemetriaService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task ProcesarAsync(string mac, string topic, string payload)
    {
        var device = await _context.Devices
            .Include(d => d.DeviceType)
            .FirstOrDefaultAsync(d => d.MacAddress == mac);

        var log = new TelemetryLog
        {
            DeviceId = device?.DeviceId,
            Topic = topic,
            Payload = payload,
            Processed = false,
            ReceivedAtUtc = DateTime.UtcNow
        };
        _context.TelemetryLogs.Add(log);
        await _context.SaveChangesAsync();

        if (device is null)
        {
            log.ErrorMessage = $"Dispositivo no encontrado para MAC {mac}";
            _logger.LogWarning(log.ErrorMessage);
            await _context.SaveChangesAsync();
            return;
        }

        var spName = device.DeviceType?.TelemetrySp;

        if (string.IsNullOrEmpty(spName))
        {
            log.ErrorMessage = $"DeviceType o SpTelemetria no configurado para MAC {mac}";
            _logger.LogWarning(log.ErrorMessage);
            await _context.SaveChangesAsync();
            return;
        }

        if (!SpNameRegex.IsMatch(spName))
        {
            log.ErrorMessage = $"Nombre de SP inválido: {spName}";
            _logger.LogError(log.ErrorMessage);
            await _context.SaveChangesAsync();
            return;
        }

        try
        {
            await _context.Database.ExecuteSqlRawAsync(
                $"EXEC {spName} @DeviceId, @Topic, @Payload",
                new SqlParameter("@DeviceId", device.DeviceId),
                new SqlParameter("@Topic", topic),
                new SqlParameter("@Payload", SqlDbType.NVarChar, -1) { Value = payload }
            );

            log.Processed = true;
            _logger.LogInformation("Telemetría procesada. MAC: {Mac}, SP: {Sp}, Topic: {Topic}", mac, spName, topic);
        }
        catch (Exception ex)
        {
            log.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Error al procesar telemetría. MAC: {Mac}, SP: {Sp}", mac, spName);
        }
        finally
        {
            await _context.SaveChangesAsync();
        }
    }
}
