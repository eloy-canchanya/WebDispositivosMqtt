using System.Security.Claims;
using FirebaseAdmin.Messaging;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebDispositivosMqtt.Data;
using WebDispositivosMqtt.Data.Models;

namespace WebDispositivosMqtt.Controllers.Api;

[ApiController]
[Route("api/push-tokens")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class FcmController(DatabaseContext db) : ControllerBase
{
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterTokenRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var existing = await db.FcmTokens
            .FirstOrDefaultAsync(t => t.Token == request.Token);

        if (existing is not null)
        {
            existing.UserId = userId;
            existing.LastUsedAtUtc = DateTime.UtcNow;
        }
        else
        {
            db.FcmTokens.Add(new FcmToken
            {
                UserId = userId,
                Token = request.Token,
            });
        }

        await db.SaveChangesAsync();
        return Ok();
    }
 


    [HttpDelete("unregister")]
    public async Task<IActionResult> Unregister([FromBody] UnregisterTokenRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var token = await db.FcmTokens
            .FirstOrDefaultAsync(t => t.Token == request.Token && t.UserId == userId);

        if (token is null)
            return NotFound();

        db.FcmTokens.Remove(token);
        await db.SaveChangesAsync();
        return Ok();
    }

    [AllowAnonymous]
    [HttpPost("enviar-prueba")]
    public async Task<IActionResult> EnviarPrueba()
    {
        var tokens = await db.FcmTokens.Select(t => t.Token).ToListAsync();
        if (tokens.Count == 0)
            return Ok(new { enviados = 0, mensaje = "No hay tokens registrados" });

        var messages = tokens.Select(token => new Message
        {
            Token = token,
            Notification = new Notification
            {
                Title = "Prueba de notificación",
                Body = "Notificación de prueba desde el servidor"
            }
        }).ToList();

        var response = await FirebaseMessaging.DefaultInstance.SendEachAsync(messages);

        return Ok(new
        {
            enviados = response.SuccessCount,
            fallidos = response.FailureCount,
            total = tokens.Count
        });
    }
}

public record RegisterTokenRequest(string Token);
public record UnregisterTokenRequest(string Token);
