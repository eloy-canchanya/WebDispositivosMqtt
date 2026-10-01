using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WebDispositivosMqtt.Data;
using WebDispositivosMqtt.Data.Models;
using WebDispositivosMqtt.Services.Commands;
using WebDispositivosMqtt.Services.Devices;
using WebDispositivosMqtt.Services.Mqtt;
using WebDispositivosMqtt.Services.Readings;

namespace WebDispositivosMqtt.Controllers.Api;

// Estado actual de los equipos del usuario, para la app. El backend decide el %
// y los estados; la app sólo los pinta. Ver contratos/api-rest.md.
[ApiController]
[Route("api/devices")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class DeviceStatusController(
    DatabaseContext db,
    IDeviceConnectionService connections,
    ICommandAckService commandAcks,
    IMqttPublisherService publisher,
    IMeasurementWaiter measurementWaiter,
    IOptions<RefreshOptions> refreshOptions,
    ILogger<DeviceStatusController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Ok(await LoadStatusAsync(userId!, deviceId: null, HttpContext.RequestAborted));
    }

    // "Deslizar para actualizar" de la app: pide una medición nueva al equipo y
    // espera a que llegue, salvo que lo guardado ya sea reciente. El campo
    // refresh dice qué pasó (ver contratos/api-rest.md).
    //
    // La solicitud queda abierta sin consultar la BD: MeasurementWaiter la
    // completa cuando el listener MQTT procesa la última lectura esperada.
    [HttpPost("{id:guid}/refresh")]
    public async Task<IActionResult> Refresh(Guid id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var ct = HttpContext.RequestAborted;
        var o = refreshOptions.Value;

        var device = await db.UserDevices
            .Where(ud => ud.UserId == userId && ud.DeviceId == id)
            .Select(ud => new { ud.Device.MacAddress, ud.Device.IsEnabled })
            .FirstOrDefaultAsync(ct);

        if (device is null)
            return NotFound();

        var status = (await LoadStatusAsync(userId, id, ct)).Single();

        // Sólo cuentan las lecturas que el equipo tiene: sin batería, alcanza el
        // nivel. Por hora de llegada, no por tsUtc: un RTC atrasado no puede
        // hacer que cada deslizar mande un "medir"
        var maxAgeCutoff = DateTime.UtcNow.AddSeconds(-o.MaxAgeSeconds);
        if (status.Readings.All(r => ReadingReceivedAtUtc(status, r) >= maxAgeCutoff))
            return Ok(status with { Refresh = RefreshResult.Cached });

        // Sin estado en memoria no es "offline": el worker borra a los inactivos.
        // En ese caso se intenta igual y el tope de espera cubre el silencio.
        var connection = connections.GetAll().FirstOrDefault(c => c.MacAddress == device.MacAddress);
        if (!device.IsEnabled || connection is { IsOnline: false })
            return Ok(status with { Refresh = RefreshResult.Offline });

        // Si ya hay un "medir" en curso para el equipo (doble deslizar, o dos
        // usuarios mirándolo), esta solicitud se suma a esa espera
        var (wait, isNew) = measurementWaiter.Begin(
            device.MacAddress, status.Readings, TimeSpan.FromSeconds(o.WaitSeconds));

        if (isNew)
        {
            // Registrado antes de publicar: el ack puede llegar enseguida
            wait.CommandId = commandAcks.RegisterCommand(device.MacAddress, "medir");
            try
            {
                await publisher.PublishCommandAsync(device.MacAddress, wait.CommandId, "medir");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Refresh: no se pudo publicar 'medir' a {Mac}", device.MacAddress);
                wait.Complete(RefreshResult.Timeout);
            }
        }

        var refresh = await wait.Result.WaitAsync(ct);

        status = (await LoadStatusAsync(userId, id, ct)).Single();
        return Ok(status with { Refresh = refresh });
    }

    private static DateTime? ReadingReceivedAtUtc(DeviceStatusDto status, string reading) => reading switch
    {
        DeviceReadings.Level => status.Level?.ReceivedAtUtc,
        DeviceReadings.Battery => status.Battery?.ReceivedAtUtc,
        _ => null // lectura que este endpoint no conoce: nunca cuenta como reciente
    };

    private async Task<List<DeviceStatusDto>> LoadStatusAsync(string userId, Guid? deviceId, CancellationToken ct)
    {
        // Equipo sin tipo de batería elegido: usa el de IsDefault (gel 12 V)
        var defaultBattery = await db.BatteryTypes.AsNoTracking().FirstOrDefaultAsync(b => b.IsDefault, ct);

        var rows = await db.UserDevices
            .Where(ud => ud.UserId == userId && (deviceId == null || ud.DeviceId == deviceId))
            .Select(ud => ud.Device)
            .OrderBy(d => d.Name)
            .Select(d => new
            {
                d.DeviceId,
                d.MacAddress,
                d.Name,
                d.Readings,
                // La última por tsUtc, que tiene índice (IX_*_DeviceId_TsUtc)
                Level = d.ChlorinatorLevels
                    .OrderByDescending(l => l.TsUtc)
                    .Select(l => new { l.DistanceMm, l.TsUtc, l.CreatedAtUtc })
                    .FirstOrDefault(),
                FullDistanceMm  = (int?)d.ChlorinatorConfig.FullDistanceMm,
                EmptyDistanceMm = (int?)d.ChlorinatorConfig.EmptyDistanceMm,
                Battery = d.ChlorinatorBatteries
                    .OrderByDescending(b => b.TsUtc)
                    .Select(b => new { b.VoltageV, b.TsUtc, b.CreatedAtUtc })
                    .FirstOrDefault(),
                BatteryType = d.ChlorinatorConfig.BatteryType,
                UnreadAlarms = d.Alarms.Count(a => !a.AlarmReads.Any(r => r.UserId == userId)),
            })
            .ToListAsync(ct);

        return rows.Select(r =>
        {
            // Una lectura que el equipo dejó de medir ("bat off") no se muestra,
            // aunque queden filas viejas en la BD
            var readings = DeviceReadings.Parse(r.Readings);
            return new DeviceStatusDto(
                r.DeviceId,
                r.MacAddress,
                r.Name,
                readings,
                r.Level is null || !readings.Contains(DeviceReadings.Level)
                    ? null
                    : ToLevel(r.Level.DistanceMm, r.Level.TsUtc, r.Level.CreatedAtUtc, r.FullDistanceMm, r.EmptyDistanceMm),
                r.Battery is null || !readings.Contains(DeviceReadings.Battery)
                    ? null
                    : new BatteryStatusDto(
                        r.Battery.VoltageV,
                        BatteryState(r.Battery.VoltageV, r.BatteryType ?? defaultBattery),
                        r.Battery.TsUtc,
                        r.Battery.CreatedAtUtc),
                r.UnreadAlarms);
        }).ToList();
    }

    // Fórmula de contratos/db-esquema.md. El A02 devuelve 0 mm tanto en la zona
    // ciega como sin eco: no es "lleno", es una lectura inválida.
    private static LevelStatusDto ToLevel(int distanceMm, DateTime tsUtc, DateTime receivedAtUtc, int? fullMm, int? emptyMm)
    {
        if (distanceMm <= 0)
            return new LevelStatusDto(null, "noReading", tsUtc, receivedAtUtc);

        if (fullMm is null || emptyMm is null)
            return new LevelStatusDto(null, "uncalibrated", tsUtc, receivedAtUtc);

        var percent = (emptyMm.Value - distanceMm) * 100.0 / (emptyMm.Value - fullMm.Value);
        return new LevelStatusDto((int)Math.Round(Math.Clamp(percent, 0, 100)), "ok", tsUtc, receivedAtUtc);
    }

    // Umbrales del catálogo BatteryTypes. ChargingMinV es null en litio: llena en
    // reposo tiene la misma tensión que cargando, así que nunca da "charging".
    // "unknown" sólo si falta la fila IsDefault: es un error de datos.
    private static string BatteryState(decimal voltage, BatteryType? type)
    {
        if (type is null) return "unknown";
        if (type.ChargingMinV is { } charging && voltage >= charging) return "charging";
        if (voltage >= type.GoodMinV) return "good";
        if (voltage >= type.LowMinV) return "low";
        return "critical";
    }
}

// Sección "Refresh" de appsettings. La app tiene que esperar más que WaitSeconds.
public class RefreshOptions
{
    public int MaxAgeSeconds { get; set; } = 10; // lo guardado más nuevo que esto no molesta al equipo (evita el doble deslizar, no un deslizar deliberado)
    public int WaitSeconds { get; set; } = 9;    // tope de espera de la medición nueva
}

public record DeviceStatusDto(
    Guid DeviceId,
    string Mac,
    string Name,
    // Lo que mide el equipo (Devices.Readings): level, battery
    IReadOnlyList<string> Readings,
    LevelStatusDto? Level,
    BatteryStatusDto? Battery,
    int UnreadAlarms,
    // Sólo en POST {id}/refresh: fresh · cached · offline · unsupported · timeout
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Refresh = null
);

// TsUtc: cuándo midió el equipo, según su RTC. ReceivedAtUtc: cuándo la guardó
// el backend (CreatedAtUtc). Lo "reciente" se decide con ReceivedAtUtc.
public record LevelStatusDto(int? Percent, string State, DateTime TsUtc, DateTime ReceivedAtUtc);

public record BatteryStatusDto(
    [property: JsonPropertyName("voltage_V")] decimal VoltageV,
    string State,
    DateTime TsUtc,
    DateTime ReceivedAtUtc
);
