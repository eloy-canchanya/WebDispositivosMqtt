using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebDispositivosMqtt.Data;
using WebDispositivosMqtt.Data.Models;
using WebDispositivosMqtt.DataIdentity.Models;
using WebDispositivosMqtt.Services.DeviceRequests;
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

    private const int ProvisioningWindowMinutes = 10;

    public DeviceRequestsController(
        IDeviceRequestService deviceRequests,
        DatabaseContext db,
        UserManager<ApplicationUser> userManager,
        IDeviceProvisioningService provisioning)
    {
        _deviceRequests = deviceRequests;
        _db = db;
        _userManager = userManager;
        _provisioning = provisioning;
    }

    // Lista todas las solicitudes (activas e historial) enriquecidas con datos de BD
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var requests = _deviceRequests.GetAll();
        var macs = requests.Select(r => r.MacAddress).ToHashSet();

        var dbDevices = await _db.Devices
            .Where(d => macs.Contains(d.MacAddress))
            .Select(d => new { d.MacAddress, d.DeviceId, d.Name, d.ProvisioningExpiresAt })
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
                provisioningExpiresAt = db?.ProvisioningExpiresAt
            };
        });

        return Ok(result);
    }

    // Registra un dispositivo nuevo en BD y aprueba la solicitud específica
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
            return Conflict(new { error = $"Ya existe un dispositivo registrado con MAC {mac}. Use /approve para abrirle la ventana." });

        var userId = _userManager.GetUserId(User);
        var nowUtc = DateTime.UtcNow;

        var device = new Device
        {
            MacAddress = mac,
            Name = body.Name.Trim(),
            RegisteredAtUtc = nowUtc,
            RegisteredByUserId = userId!,
            IsEnabled = true,
            MqttCredential = _provisioning.GenerateCredential(out _),
            ProvisioningExpiresAt = nowUtc.AddMinutes(ProvisioningWindowMinutes)
        };

        _db.Devices.Add(device);
        await _db.SaveChangesAsync();

        _deviceRequests.TryApprove(id);

        return Ok(new
        {
            deviceId = device.DeviceId,
            deviceName = device.Name,
            macAddress = device.MacAddress,
            provisioningExpiresAt = device.ProvisioningExpiresAt,
            message = $"Dispositivo registrado. El dispositivo tiene {ProvisioningWindowMinutes} minutos para obtener sus credenciales."
        });
    }

    // Abre la ventana de provisioning para un dispositivo ya registrado
    [HttpPost("{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id)
    {
        if (!_deviceRequests.TryGet(id, out var request))
            return NotFound(new { error = "No existe una solicitud con ese Id." });

        if (request.Status != DeviceRequestStatus.Pending)
            return BadRequest(new { error = "La solicitud ya no está pendiente." });

        var device = await _db.Devices.FirstOrDefaultAsync(d => d.MacAddress == request.MacAddress);
        if (device is null)
            return NotFound(new { error = "Dispositivo no encontrado en BD. Use /register para registrarlo primero." });

        if (!device.IsEnabled)
            return StatusCode(403, new { error = "El dispositivo está deshabilitado." });

        device.MqttCredential = _provisioning.GenerateCredential(out _);
        device.ProvisioningExpiresAt = DateTime.UtcNow.AddMinutes(ProvisioningWindowMinutes);

        await _db.SaveChangesAsync();

        _deviceRequests.TryApprove(id);

        return Ok(new
        {
            deviceId = device.DeviceId,
            deviceName = device.Name,
            provisioningExpiresAt = device.ProvisioningExpiresAt,
            message = $"Ventana abierta. El dispositivo tiene {ProvisioningWindowMinutes} minutos para obtener sus credenciales."
        });
    }

    // Cancela una solicitud activa (queda en historial hasta que el worker la limpie)
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (!await _deviceRequests.CancelAsync(id))
            return NotFound(new { error = "No existe una solicitud activa con ese Id." });

        return Ok(new { message = "Solicitud cancelada." });
    }
}

public record RegisterDeviceBody(string Name);
