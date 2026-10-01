using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebDispositivosMqtt.Data;
using WebDispositivosMqtt.Data.Models;
using WebDispositivosMqtt.DataIdentity.Models;
using Microsoft.Extensions.Options;
using WebDispositivosMqtt.Services.Commands;
using WebDispositivosMqtt.Services.Devices;
using WebDispositivosMqtt.Services.Dynsec;
using WebDispositivosMqtt.Services.Mqtt;
using WebDispositivosMqtt.Services.Provisioning;

namespace WebDispositivosMqtt.Controllers
{
    public record DeviceAdminViewModel(
        Guid DeviceId,
        string MacAddress,
        string Name,
        bool IsEnabled,
        DateTime RegisteredAtUtc,
        string RegisteredByUserName,
        DateTime? ProvisioningExpiresAtUtc,
        bool HasPassword,
        bool IsDelivered);

    public record BatteryTypeOption(int Id, string Name, bool IsDefault);

    public record DeviceConnectionViewModel
    {
        public string Name { get; set; } = default!;
        public bool IsOnline { get; set; } = false;
        public DateTime LastSeenUtc { get; set; }
        public LastSeenType? LastSeenType { get; set; }

        public string DeviceId { get; set; } = default!;
        public string MacAddress { get; set; } = default!;
        public bool IsEnabled { get; set; }
        public string RegisteredByUserName { get; set; } = default!;
        public DateTime RegisteredAtUtc { get; set; }
    }

    [Authorize]
    public class DevicesController(
        DatabaseContext db,
        UserManager<ApplicationUser> userManager,
        IDeviceProvisioningService provisioning,
        IDeviceConnectionService deviceConnectionService,
        IDynsecService dynsec,
        ICommandAckService commandAckService,
        IMqttPublisherService publisher,
        IOptions<TerminalOptions> terminalOptions,
        ILogger<DevicesController> logger) : Controller
    {
        private sealed record DeviceRow(
            Guid DeviceId,
            string MacAddress,
            string Name,
            bool IsEnabled,
            DateTime RegisteredAtUtc,
            string? RegisteredByUserName);

        public async Task<IActionResult> Index()
        {
            var userId = userManager.GetUserId(User);
            var isAdmin = User.IsInRole("Admin");

            List<DeviceRow> dbRows;

            if (isAdmin)
            {
                dbRows = await db.Devices
                    .OrderByDescending(d => d.RegisteredAtUtc)
                    .Select(d => new DeviceRow(
                        d.DeviceId,
                        d.MacAddress,
                        d.Name,
                        d.IsEnabled,
                        d.RegisteredAtUtc,
                        d.RegisteredByUser!.UserName))
                    .ToListAsync();
            }
            else
            {
                dbRows = await db.Devices
                    .Where(d => d.UserDevices.Any(ud => ud.UserId == userId))
                    .OrderByDescending(d => d.RegisteredAtUtc)
                    .Select(d => new DeviceRow(
                        d.DeviceId,
                        d.MacAddress,
                        d.Name,
                        d.IsEnabled,
                        d.RegisteredAtUtc,
                        null))
                    .ToListAsync();
            }

            var connectionStates = deviceConnectionService.GetAll()
                .ToDictionary(s => s.MacAddress);

            var viewModels = dbRows.Select(d =>
            {
                connectionStates.TryGetValue(d.MacAddress, out var state);
                return new DeviceConnectionViewModel
                {
                    DeviceId = d.DeviceId.ToString(),
                    MacAddress = d.MacAddress,
                    Name = d.Name,
                    IsEnabled = d.IsEnabled,
                    RegisteredByUserName = d.RegisteredByUserName ?? string.Empty,
                    RegisteredAtUtc = d.RegisteredAtUtc,
                    IsOnline = state?.IsOnline ?? false,
                    LastSeenUtc = state?.LastSeenUtc ?? default,
                    LastSeenType = state?.LastSeenType
                };
            }).ToList();

            ViewData["CommandTimeoutSeconds"] = terminalOptions.Value.CommandTimeoutSeconds;
            return View(viewModels);
        }

        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Admin()
        {
            var devices = await db.Devices
                .OrderByDescending(d => d.RegisteredAtUtc)
                .Select(d => new DeviceAdminViewModel(
                    d.DeviceId,
                    d.MacAddress,
                    d.Name,
                    d.IsEnabled,
                    d.RegisteredAtUtc,
                    d.RegisteredByUser!.UserName ?? "",
                    d.ProvisioningExpiresAtUtc,
                    d.MqttCredential != null,
                    d.IsDelivered))
                .ToListAsync();

            // Opciones del desplegable de batería del modal de configuración
            ViewData["BatteryTypes"] = await db.BatteryTypes
                .OrderBy(b => b.Id)
                .Select(b => new BatteryTypeOption(b.Id, b.Name, b.IsDefault))
                .ToListAsync();

            return View(devices);
        }

        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DynsecStatus([FromQuery] string mac)
        {
            try
            {
                var info = await dynsec.GetClientStatusAsync(mac);
                return Json(new { status = info.Status.ToString(), roles = info.Roles, error = info.ErrorMessage });
            }
            catch (Exception ex)
            {
                return Json(new { status = "Error", roles = Array.Empty<string>(), error = ex.Message });
            }
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetPassword(Guid deviceId)
        {
            var device = await db.Devices.FindAsync(deviceId);

            if (device is null || !device.IsEnabled)
            {
                TempData["Error"] = "Dispositivo no encontrado o deshabilitado.";
                return RedirectToAction(nameof(Admin));
            }

            var encrypted = provisioning.GenerateCredential(out var plainPassword);

            try
            {
                await dynsec.SetDevicePasswordAsync(device.MacAddress, plainPassword);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error al guardar password en dynsec para {Mac}", device.MacAddress);
                TempData["Error"] = $"Error al guardar credenciales en Mosquitto para {device.Name}.";
                return RedirectToAction(nameof(Admin));
            }

            device.MqttCredential = encrypted;
            device.IsDelivered = false;
            await db.SaveChangesAsync();

            TempData["Ok"] = $"Password actualizado para {device.Name}.";
            return RedirectToAction(nameof(Admin));
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> OpenWindow(Guid deviceId)
        {
            var device = await db.Devices.FindAsync(deviceId);

            if (device is null || !device.IsEnabled)
            {
                TempData["Error"] = "Dispositivo no encontrado o deshabilitado.";
                return RedirectToAction(nameof(Admin));
            }

            if (device.MqttCredential is null)
            {
                TempData["Error"] = $"El dispositivo {device.Name} no tiene password. Créelo primero.";
                return RedirectToAction(nameof(Admin));
            }

            device.ProvisioningExpiresAtUtc = DateTime.UtcNow.AddMinutes(10);
            await db.SaveChangesAsync();

            TempData["Ok"] = $"Ventana abierta para {device.Name}. El dispositivo tiene 10 minutos para provisionarse.";
            return RedirectToAction(nameof(Admin));
        }

        // Valores actuales para el modal de configuración. Cada campo se llama
        // igual que el input del modal (en camelCase), que se llena solo.
        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Config([FromQuery] Guid deviceId)
        {
            var config = await db.Devices
                .Where(d => d.DeviceId == deviceId)
                .Select(d => new
                {
                    FullDistanceMm  = (int?)d.ChlorinatorConfig.FullDistanceMm,
                    EmptyDistanceMm = (int?)d.ChlorinatorConfig.EmptyDistanceMm,
                    BatteryTypeId   = (int?)d.ChlorinatorConfig.BatteryTypeId
                })
                .FirstOrDefaultAsync();

            if (config is null)
                return Json(new { ok = false, error = "Dispositivo no encontrado." });

            return Json(new { ok = true, config });
        }

        // Guarda el modal completo en ChlorinatorConfig. Un campo vacío es NULL:
        // tanque sin calibrar, o batería del tipo por defecto.
        [HttpPost]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveConfig(
            [FromForm] Guid deviceId,
            [FromForm] int? fullDistanceMm,
            [FromForm] int? emptyDistanceMm,
            [FromForm] int? batteryTypeId)
        {
            if (!ModelState.IsValid)
                return Json(new { ok = false, error = "Las distancias deben ser números enteros en mm." });

            var device = await db.Devices.FindAsync(deviceId);
            if (device is null)
                return Json(new { ok = false, error = "Dispositivo no encontrado." });

            // Mismas reglas que CK_ChlorinatorConfig_Distances: las dos o ninguna
            if ((fullDistanceMm is null) != (emptyDistanceMm is null))
                return Json(new { ok = false, error = "Completá las dos distancias del tanque, o dejá las dos vacías." });
            if (fullDistanceMm <= 0)
                return Json(new { ok = false, error = "La distancia de lleno debe ser mayor que 0." });
            if (emptyDistanceMm <= fullDistanceMm)
                return Json(new { ok = false, error = "La distancia de vacío debe ser mayor que la de lleno: el sensor está arriba." });

            if (batteryTypeId is not null && !await db.BatteryTypes.AnyAsync(b => b.Id == batteryTypeId))
                return Json(new { ok = false, error = "Tipo de batería desconocido." });

            var config = await db.ChlorinatorConfigs.FindAsync(deviceId);
            if (config is null)
            {
                config = new ChlorinatorConfig { DeviceId = deviceId };
                db.ChlorinatorConfigs.Add(config);
            }

            config.FullDistanceMm  = fullDistanceMm;
            config.EmptyDistanceMm = emptyDistanceMm;
            config.BatteryTypeId   = batteryTypeId;
            config.UpdatedAtUtc    = DateTime.UtcNow;
            await db.SaveChangesAsync();

            logger.LogInformation("Configuración de {Mac}: lleno {Full} mm, vacío {Empty} mm, batería {BatteryTypeId}",
                device.MacAddress, config.FullDistanceMm, config.EmptyDistanceMm, config.BatteryTypeId);

            return Json(new { ok = true, message = $"Configuración guardada para {device.Name}." });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendCommand([FromForm] Guid deviceId, [FromForm] string cmd)
        {
            var userId  = userManager.GetUserId(User);
            var isAdmin = User.IsInRole("Admin");

            var device = isAdmin
                ? await db.Devices.FindAsync(deviceId)
                : await db.Devices.FirstOrDefaultAsync(d =>
                    d.DeviceId == deviceId &&
                    d.UserDevices.Any(ud => ud.UserId == userId));

            if (device is null || !device.IsEnabled)
                return Json(new { ok = false, error = "Dispositivo no encontrado o deshabilitado." });

            var commandId = commandAckService.RegisterCommand(device.MacAddress, cmd);

            try
            {
                await publisher.PublishCommandAsync(device.MacAddress, commandId, cmd);
                return Json(new { ok = true, commandId });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error publicando comando '{Cmd}' al dispositivo {Mac}", cmd, device.MacAddress);
                return Json(new { ok = false, error = "Error al publicar el comando." });
            }
        }
    }
}
