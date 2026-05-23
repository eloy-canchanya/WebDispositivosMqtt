using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using WebDispositivosMqtt.DataIdentity.Models;
using WebDispositivosMqtt.Models;

namespace WebDispositivosMqtt.Controllers
{
    public class AccountController : Controller
    {
        public const string UserTimeZoneSessionKey = "UserTimeZoneId";
        private readonly SignInManager<ApplicationUser> _signInManager;

        public AccountController(SignInManager<ApplicationUser> signInManager)
        {
            _signInManager = signInManager;
        }

        [AllowAnonymous]
        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            if (User.Identity?.IsAuthenticated == true)
                return RedirectToAction("Index", "Devices");

            return View(new LoginViewModel { ReturnUrl = returnUrl });
        }

        [AllowAnonymous]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var result = await _signInManager.PasswordSignInAsync(
                model.Email,
                model.Password,
                model.RememberMe,
                lockoutOnFailure: false);

            if (result.Succeeded)
            {
                HttpContext.Session.SetString(UserTimeZoneSessionKey, GetUserTimeZoneFromDatabase(model.Email));

                if (!string.IsNullOrWhiteSpace(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
                    return Redirect(model.ReturnUrl);

                return RedirectToAction("Index", "Devices");
            }

            if (result.IsLockedOut)
            {
                ModelState.AddModelError(string.Empty, "La cuenta está deshabilitada por un administrador.");
                return View(model);
            }

            ModelState.AddModelError(string.Empty, "Email o contraseña incorrectos.");
            return View(model);
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            HttpContext.Session.Remove(UserTimeZoneSessionKey);
            await _signInManager.SignOutAsync();
            return RedirectToAction("Index", "Home");
        }

        [HttpGet]
        public IActionResult AccessDenied()
        {
            return View();
        }

        private static string GetUserTimeZoneFromDatabase(string email)
        {
            // Simula una preferencia cargada desde base de datos al iniciar sesión.
            return "America/Lima";
        }
    }
}
