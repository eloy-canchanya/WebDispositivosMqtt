using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebDispositivosMqtt.Data;
using WebDispositivosMqtt.Data.Models;
using WebDispositivosMqtt.DataIdentity.Models;
using WebDispositivosMqtt.Services.DeviceRequests;
using WebDispositivosMqtt.Services.Dynsec;
using WebDispositivosMqtt.Services.Provisioning;

namespace WebDispositivosMqtt.Controllers.Api;

[ApiController]
[Route("api/device-requests")]
[Authorize(Roles = "Admin")]
public class DeviceRequestsController : ControllerBase
{
    private readonly IDeviceRequestService _deviceRequests;
    private readonly DatabaseContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IDeviceProvisioningService _provisioning;
    private readonly IDynsecService _dynsec;
    private readonly ILogger<DeviceRequestsController> _logger;

    private const int ProvisioningWindowMinutes = 10;

    public DeviceRequestsController(
        IDeviceRequestService deviceRequests,
        DatabaseContext db,
        UserManager<ApplicationUser> userManager,
        IDeviceProvisioningService provisioning,
        IDynsecService dynsec,
        ILogger<DeviceRequestsController> logger)
    {
        _deviceRequests = deviceRequests;
        _db = db;
        _userManager = userManager;
        _provisioning = provisioning;
        _dynsec = dynsec;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var requests = _deviceRequests.GetAll();
        var macs = requests.Select(r => r.MacAddress).ToHashSet();

        var dbDevices = await _db.Devices
            .Where(d => macs.Contains(d.MacAddress))
            .Select(d => new { d.MacAddress, d.DeviceId, d.Name, d.ProvisioningExpiresAt, d.MqttCredential, d.IsDelivered })
            .ToListAsync();

        var dbDict = dbDevices.ToDictionary(d => d.MacAddress);

        var result = requests.Select(r =>
        {
            dbDict.TryGetValue(r.MacAddress, out var db);
            return new
            {
                id = r.Id,
                macAddress = r.MacAddress,
                keyword = r.Keyword,
                createdAtUtc = r.CreatedAtUtc,
                status = r.Status.ToString(),
                isRegistered = db is not null,
                deviceId = db?.DeviceId,
                deviceName = db?.Name,
                provisioningExpiresAt = db?.ProvisioningExpiresAt,
                hasPassword = db?.MqttCredential != null,
                isDelivered = db?.IsDelivered ?? false
            };
        });

        return Ok(result);
    }

    // Registra un dispositivo nuevo en BD (sin credenciales ni ventana)
    [HttpPost("{id:guid}/register")]
    public async Task<IActionResult> Register(Guid id, [FromBody] RegisterDeviceBody body)
    {
        if (!_deviceRequests.TryGet(id, out var request))
            return NotFound(new { error = "No existe una solicitud con ese Id." });

        if (request.Status != DeviceRequestStatus.Pending)
            return BadRequest(new { error = "La solicitud ya no está pendiente." });

        if (string.IsNullOrWhiteSpace(body.Name))
            return BadRequest(new { error = "El nombre del dispositivo es obligatorio." });

        var mac = request.MacAddress;

        var exists = await _db.Devices.AnyAsync(d => d.MacAddress == mac);
        if (exists)
            return Conflict(new { error = $"Ya existe un dispositivo registrado con MAC {mac}." });

        var userId = _userManager.GetUserId(User);
        var nowUtc = DateTime.UtcNow;

        var device = new Device
        {
            MacAddress = mac,
            Name = body.Name.Trim(),
            RegisteredAtUtc = nowUtc,
            RegisteredByUserId = userId!,
            IsEnabled = true,
            IsDelivered = false
        };

        _db.Devices.Add(device);
        await _db.SaveChangesAsync();

        return Ok(new
        {
            deviceId = device.DeviceId,
            deviceName = device.Name,
            macAddress = device.MacAddress,
            message = "Dispositivo registrado. Ahora cree el password y abra la ventana."
        });
    }

    // Crea o recrea el password en dynsec y lo guarda en BD
    [HttpPost("{id:guid}/set-password")]
    public async Task<IActionResult> SetPassword(Guid id)
    {
        if (!_deviceRequests.TryGet(id, out var request))
            return NotFound(new { error = "No existe una solicitud con ese Id." });

        if (request.Status != DeviceRequestStatus.Pending)
            return BadRequest(new { error = "La solicitud ya no está pendiente." });

        var device = await _db.Devices.FirstOrDefaultAsync(d => d.MacAddress == request.MacAddress);
        if (device is null)
            return NotFound(new { error = "Dispositivo no encontrado en BD." });

        if (!device.IsEnabled)
            return StatusCode(403, new { error = "El dispositivo está deshabilitado." });

        var encrypted = _provisioning.GenerateCredential(out var plainPassword);

        try
        {
            await _dynsec.SetDevicePasswordAsync(device.MacAddress, plainPassword);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al guardar password en dynsec para {Mac}", device.MacAddress);
            return StatusCode(500, new { error = "Error al guardar las credenciales en Mosquitto." });
        }

        device.MqttCredential = encrypted;
        device.IsDelivered = false;
        await _db.SaveChangesAsync();

        return Ok(new { hasPassword = true, isDelivered = false, message = "Password creado/actualizado correctamente." });
    }

    // Abre la ventana de provisioning y aprueba la solicitud
    [HttpPost("{id:guid}/open-window")]
    public async Task<IActionResult> OpenWindow(Guid id)
    {
        if (!_deviceRequests.TryGet(id, out var request))
            return NotFound(new { error = "No existe una solicitud con ese Id." });

        if (request.Status != DeviceRequestStatus.Pending)
            return BadRequest(new { error = "La solicitud ya no está pendiente." });

        var device = await _db.Devices.FirstOrDefaultAsync(d => d.MacAddress == request.MacAddress);
        if (device is null)
            return NotFound(new { error = "Dispositivo no encontrado en BD." });

        if (!device.IsEnabled)
            return StatusCode(403, new { error = "El dispositivo está deshabilitado." });

        if (device.MqttCredential is null)
            return BadRequest(new { error = "El dispositivo no tiene password. Créelo primero." });

        device.ProvisioningExpiresAt = DateTime.UtcNow.AddMinutes(ProvisioningWindowMinutes);
        await _db.SaveChangesAsync();
        _deviceRequests.TryApprove(id);

        return Ok(new
        {
            provisioningExpiresAt = device.ProvisioningExpiresAt,
            message = $"Ventana abierta. El dispositivo tiene {ProvisioningWindowMinutes} minutos para obtener sus credenciales."
        });
    }

    // Cancela una solicitud activa
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (!await _deviceRequests.CancelAsync(id))
            return NotFound(new { error = "No existe una solicitud activa con ese Id." });

        return Ok(new { message = "Solicitud cancelada." });
    }
}

public record RegisterDeviceBody(string Name);
