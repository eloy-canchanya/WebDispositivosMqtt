using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebDispositivosMqtt.Data;
using WebDispositivosMqtt.Data.Models;
using WebDispositivosMqtt.DataIdentity.Models;
using WebDispositivosMqtt.Services.NewDevices;

namespace WebDispositivosMqtt.Controllers
{
    public class NewDevicesController : Controller
    {
        private readonly INewDevicesService _unregisteredDeviceService;
        private readonly DatabaseContext _db;
        private readonly UserManager<ApplicationUser> _userManager;

        public NewDevicesController(
            INewDevicesService unregisteredDeviceService,
            DatabaseContext db,
            UserManager<ApplicationUser> userManager)
        {
            _unregisteredDeviceService = unregisteredDeviceService;
            _db = db;
            _userManager = userManager;
        }

        [HttpGet]
        public IActionResult Index()
        {
            var devices = _unregisteredDeviceService
                .GetAll()
                .OrderByDescending(d => d.LastSeen)
                .ToList();

            return View(devices);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegisterDevice(string tempId, string macAddress, string displayName)
        {
            // 1) Validaciones básicas
            if (string.IsNullOrWhiteSpace(macAddress))
            {
                TempData["Error"] = "MAC es obligatoria.";
                return RedirectToAction(nameof(Index));
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                TempData["Error"] = "El nombre del dispositivo es obligatorio.";
                return RedirectToAction(nameof(Index));
            }

            var normalizedMac = NormalizeMac(macAddress);

            if (normalizedMac.Length != 12 || normalizedMac.Any(c => !Uri.IsHexDigit(c)))
            {
                TempData["Error"] = "La MAC debe tener 12 caracteres hexadecimales.";
                return RedirectToAction(nameof(Index));
            }

            // 2) Evitar duplicados
            var exists = await _db.Devices.AnyAsync(d => d.MacAddress == normalizedMac);
            if (exists)
            {
                TempData["Error"] = $"Ya existe un dispositivo con MAC {normalizedMac}.";
                return RedirectToAction(nameof(Index));
            }

            // 3) Insertar en tabla Devices

            DateTime nowUtc = DateTime.UtcNow;
            var userId = _userManager.GetUserId(User); // Id real de AspNetUsers


            var entity = new Device
            {
                MacAddress = normalizedMac,
                Name = displayName.Trim(),
                RegisteredAtUtc = nowUtc,
                RegisteredByUserId = userId,
                IsActive = true,
                CreatedAtUtc = nowUtc,
                // UpdatedAtUtc = nowUtc

            };


            _db.Devices.Add(entity);
            await _db.SaveChangesAsync();

            // 4) Quitar de la lista temporal en memoria
            if (!string.IsNullOrWhiteSpace(tempId))
            {
                _unregisteredDeviceService.Remove(tempId);
                if (tempId != normalizedMac)
                {
                    _unregisteredDeviceService.Remove(normalizedMac);
                }
            }

            TempData["Ok"] = $"Dispositivo {entity.Name} registrado correctamente.";
            return RedirectToAction(nameof(Index));
        }

        private static string NormalizeMac(string mac)
        {
            return new string(mac
                .Where(c => Uri.IsHexDigit(c))
                .Select(char.ToUpperInvariant)
                .ToArray());
        }
    }
}
