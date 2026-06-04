using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebDispositivosMqtt.Data;
using WebDispositivosMqtt.DataIdentity.Models;
using WebDispositivosMqtt.Services.Devices;
using WebDispositivosMqtt.Services.Dynsec;
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
        DateTime? ProvisioningExpiresAt,
        bool HasPassword,
        bool IsDelivered);

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
                    d.ProvisioningExpiresAt,
                    d.MqttCredential != null,
                    d.IsDelivered))
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

            device.ProvisioningExpiresAt = DateTime.UtcNow.AddMinutes(10);
            await db.SaveChangesAsync();

            TempData["Ok"] = $"Ventana abierta para {device.Name}. El dispositivo tiene 10 minutos para provisionarse.";
            return RedirectToAction(nameof(Admin));
        }
    }
}
