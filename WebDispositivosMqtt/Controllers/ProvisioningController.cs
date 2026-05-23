using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WebDispositivosMqtt.Data;
using WebDispositivosMqtt.Services.Mqtt;
using WebDispositivosMqtt.Services.Provisioning;
using WebDispositivosMqtt.Utils;

namespace WebDispositivosMqtt.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProvisioningController : ControllerBase
{
    private readonly DatabaseContext _db;
    private readonly IDeviceProvisioningService _provisioning;
    private readonly MqttOptions _mqttOptions;

    public ProvisioningController(
        DatabaseContext db,
        IDeviceProvisioningService provisioning,
        IOptions<MqttOptions> mqttOptions)
    {
        _db = db;
        _provisioning = provisioning;
        _mqttOptions = mqttOptions.Value;
    }

    [HttpPost]
    public async Task<IActionResult> Post([FromBody] ProvisioningRequest request)
    {
        if (!DeviceMac.IsValid(request.Mac))
            return BadRequest(new { error = "MAC inválida." });

        var device = await _db.Devices
            .FirstOrDefaultAsync(d => d.MacAddress == request.Mac && d.IsEnabled);

        if (device is null || device.MqttCredential is null)
            return NotFound();

        // Verificar que la ventana esté abierta y no haya expirado
        if (device.ProvisioningExpiresAt is null || device.ProvisioningExpiresAt < DateTime.UtcNow)
            return Forbid();

        var plainPassword = _provisioning.GetPlainPassword(device.MqttCredential);

        // Cerrar la ventana: un solo uso
        device.ProvisioningExpiresAt = null;
        await _db.SaveChangesAsync();

        return Ok(new
        {
            mqttHost = _mqttOptions.Host,
            mqttPort = _mqttOptions.Port,
            mqttUser = device.MacAddress,
            mqttPassword = plainPassword
        });
    }
}

public record ProvisioningRequest(string Mac);
