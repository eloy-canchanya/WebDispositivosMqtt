using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebDispositivosMqtt.Data;
using WebDispositivosMqtt.Data.Models;
using WebDispositivosMqtt.DataIdentity.Models;
using WebDispositivosMqtt.Services.NewDevices;
using WebDispositivosMqtt.Services.Provisioning;
using WebDispositivosMqtt.Utils;

namespace WebDispositivosMqtt.Controllers
{
    public record NewDeviceViewModel(
        string MacAddress,
        DateTime LastSeen,
        string Status,
        bool IsRegistered,
        Guid? DeviceId,
        DateTime? ProvisioningExpiresAt);

    public class NewDevicesController : Controller
    {
        private readonly INewDevicesService _unregisteredDeviceService;
        private readonly DatabaseContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IDeviceProvisioningService _provisioning;
        private readonly int _provisioningExpirationMinutes;

        public NewDevicesController(
            INewDevicesService unregisteredDeviceService,
            DatabaseContext db,
            UserManager<ApplicationUser> userManager,
            IDeviceProvisioningService provisioning,
            IConfiguration configuration)
        {
            _unregisteredDeviceService = unregisteredDeviceService;
            _db = db;
            _userManager = userManager;
            _provisioning = provisioning;
            _provisioningExpirationMinutes = configuration.GetValue<int>("Provisioning:ExpirationMinutes", 10);
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var newDevices = _unregisteredDeviceService
                .GetAll()
                .OrderByDescending(d => d.LastSeen)
                .ToList();

            var macs = newDevices.Select(d => d.MacAddress).ToHashSet();

            var registered = await _db.Devices
                .Where(d => macs.Contains(d.MacAddress))
                .Select(d => new { d.MacAddress, d.DeviceId, d.ProvisioningExpiresAt })
                .ToListAsync();

            var registeredDict = registered.ToDictionary(d => d.MacAddress);

            var viewModels = newDevices.Select(d =>
            {
                registeredDict.TryGetValue(d.MacAddress, out var reg);
                return new NewDeviceViewModel(
                    d.MacAddress,
                    d.LastSeen,
                    d.Status,
                    reg is not null,
                    reg?.DeviceId,
                    reg?.ProvisioningExpiresAt);
            }).ToList();

            return View(viewModels);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegisterDevice(string tempId, string macAddress, string displayName)
        {
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

            if (!DeviceMac.IsValid(macAddress))
            {
                TempData["Error"] = "La MAC debe tener exactamente 12 caracteres hexadecimales en mayúsculas, sin separadores.";
                return RedirectToAction(nameof(Index));
            }

            var exists = await _db.Devices.AnyAsync(d => d.MacAddress == macAddress);
            if (exists)
            {
                TempData["Error"] = $"Ya existe un dispositivo con MAC {macAddress}.";
                return RedirectToAction(nameof(Index));
            }

            DateTime nowUtc = DateTime.UtcNow;
            var userId = _userManager.GetUserId(User);

            var mqttCredential = _provisioning.GenerateCredential(out _);

            var entity = new Device
            {
                MacAddress = macAddress,
                Name = displayName.Trim(),
                RegisteredAtUtc = nowUtc,
                RegisteredByUserId = userId,
                IsEnabled = true,
                MqttCredential = mqttCredential,
                ProvisioningExpiresAt = nowUtc.AddMinutes(_provisioningExpirationMinutes),
            };

            _db.Devices.Add(entity);
            await _db.SaveChangesAsync();

            if (!string.IsNullOrWhiteSpace(tempId))
                _unregisteredDeviceService.Remove(tempId);

            TempData["Ok"] = $"Dispositivo {entity.Name} registrado. El dispositivo tiene {_provisioningExpirationMinutes} minuto(s) para conectarse y obtener sus credenciales.";
            return RedirectToAction(nameof(Index));
        }
    }
}
