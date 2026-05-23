using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebDispositivosMqtt.Data;
using WebDispositivosMqtt.DataIdentity.Models;
using WebDispositivosMqtt.Services.Devices;

namespace WebDispositivosMqtt.Controllers
{
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
    public class DevicesController(DatabaseContext db, UserManager<ApplicationUser> userManager) : Controller
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
    }
}
