using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebDispositivosMqtt.Data;
using WebDispositivosMqtt.Services.DeviceRequests;
using WebDispositivosMqtt.Services.Provisioning;
using WebDispositivosMqtt.Utils;

namespace WebDispositivosMqtt.Controllers.Api;

[ApiController]
[Route("api/devices")]
public class ProvisioningController : ControllerBase
{
    private readonly DatabaseContext _db;
    private readonly IDeviceProvisioningService _provisioning;
    private readonly IDeviceRequestService _deviceRequests;

    public ProvisioningController(
        DatabaseContext db,
        IDeviceProvisioningService provisioning,
        IDeviceRequestService deviceRequests)
    {
        _db = db;
        _provisioning = provisioning;
        _deviceRequests = deviceRequests;
    }

    [HttpPost("credential-request")]
    public async Task<IActionResult> CredentialRequest([FromBody] CredentialRequestBody body)
    {
        if (!DeviceMac.IsValid(body.Mac))
            return BadRequest(new { error = "MAC inválida. Se esperan 12 caracteres hexadecimales en mayúsculas." });

        if (!IsValidKeyword(body.Keyword))
            return BadRequest(new { error = "Palabra clave inválida. Debe contener entre 3 y 6 dígitos o letras." });

        var device = await _db.Devices
            .FirstOrDefaultAsync(d => d.MacAddress == body.Mac);

        // Dispositivo registrado, habilitado, con credenciales, ventana abierta y GUID aprobado → entregar
        if (device is not null
            && device.IsEnabled
            && device.MqttCredential is not null
            && device.ProvisioningExpiresAt is not null
            && device.ProvisioningExpiresAt > DateTime.UtcNow
            && _deviceRequests.TryGetApproved(body.SessionId, out var approvedRequest))
        {
            var plainPassword = _provisioning.GetPlainPassword(device.MqttCredential);

            device.ProvisioningExpiresAt = null;
            await _db.SaveChangesAsync();

            await _deviceRequests.MarkProvisionedAsync(approvedRequest.Id);

            return Ok(new
            {
                mqttUser = device.MacAddress,
                mqttPassword = plainPassword,
                // deviceId = device.DeviceId,
                // deviceName = device.Name
            });
        }

        // Dispositivo registrado pero deshabilitado
        if (device is not null && !device.IsEnabled)
            return StatusCode(403, new { status = "disabled", message = "El dispositivo está deshabilitado. Contacte al administrador." });

        // Cualquier otro caso: almacenar/actualizar solicitud y esperar aprobación
        await _deviceRequests.AddAsync(body.SessionId, body.Mac, body.Keyword, isRegistered: device is not null);

        return Accepted(new { status = "pending", message = "Solicitud recibida. Esperando aprobación del administrador." });
    }

    private static bool IsValidKeyword(string? keyword)
        => keyword is not null
            && keyword.Length >= 3
            && keyword.Length <= 6
            && keyword.All(char.IsLetterOrDigit);
}

public record CredentialRequestBody(Guid SessionId, string Mac, string Keyword);
