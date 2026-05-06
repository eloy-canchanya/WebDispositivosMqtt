using Microsoft.AspNetCore.Mvc;
using WebDispositivosMqtt.Services.NewDevices;

namespace WebDispositivosMqtt.Controllers
{
    public class NewDevicesController : Controller
    {
        private readonly INewDevicesService _unregisteredDeviceService;

        public NewDevicesController(INewDevicesService unregisteredDeviceService)
        {
            _unregisteredDeviceService = unregisteredDeviceService;
        }

        public IActionResult Index()
        {
            var devices = _unregisteredDeviceService
                .GetAll()
                .OrderByDescending(d => d.LastSeen)
                .ToList();

            return View(devices);
        }
    }
}
