using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebDispositivosMqtt.Data;
using WebDispositivosMqtt.DataIdentity.Models;
using WebDispositivosMqtt.Services.Devices;
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
        DateTime? ProvisioningExpiresAt);

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
        IDeviceProvisioningService provisioning) : Controller
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

            var viewModels = dbRows.Select(d => new DeviceConnectionViewModel
            {
                DeviceId = d.DeviceId.ToString(),
                MacAddress = d.MacAddress,
                Name = d.Name,
                IsEnabled = d.IsEnabled,
                RegisteredByUserName = d.RegisteredByUserName ?? string.Empty,
                RegisteredAtUtc = d.RegisteredAtUtc
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
                    d.ProvisioningExpiresAt))
                .ToListAsync();

            return View(devices);
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetProvisioning(Guid deviceId)
        {
            var device = await db.Devices.FindAsync(deviceId);

            if (device is null || !device.IsEnabled)
            {
                TempData["Error"] = "Dispositivo no encontrado.";
                return RedirectToAction(nameof(Admin));
            }

            device.MqttCredential = provisioning.GenerateCredential(out _);
            device.ProvisioningExpiresAt = DateTime.UtcNow.AddMinutes(10);

            await db.SaveChangesAsync();

            TempData["Ok"] = $"Credenciales regeneradas para {device.Name}. El dispositivo tiene 10 minutos para provisionarse.";
            return RedirectToAction(nameof(Admin));
        }
    }
}
