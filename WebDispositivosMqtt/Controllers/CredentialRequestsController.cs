using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace WebDispositivosMqtt.Controllers
{
    [Authorize(Roles = "Admin")]
    public class CredentialRequestsController : Controller
    {
        public IActionResult Index() => View();
    }
}
