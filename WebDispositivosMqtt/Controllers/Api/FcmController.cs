using Microsoft.AspNetCore.Mvc;
using FirebaseAdmin.Messaging;

namespace WebDispositivosMqtt.Controllers.Api;

[ApiController]
[Route("api/fcm")]
public class FcmController : ControllerBase
{
    // Almacenamiento en memoria para la prueba (reemplazar por BD después)
    private static readonly List<string> _tokens = [];

    // La app Android llama esto al iniciar con su FCM token
    [HttpPost("registrar-token")]
    public IActionResult RegistrarToken([FromBody] string token)
    {
        if (!_tokens.Contains(token))
            _tokens.Add(token);
        return Ok();
    }

    // Endpoint de prueba: envía notificación a todos los tokens registrados
    [HttpPost("enviar-prueba")]
    public async Task<IActionResult> EnviarPrueba()
    {
        if (_tokens.Count == 0)
            return BadRequest("No hay tokens registrados");

        var multicast = new MulticastMessage
        {
            Tokens = _tokens,
            Notification = new Notification { Title = "Prueba FCM", Body = "Funciona!" }
        };
        var response = await FirebaseMessaging.DefaultInstance.SendEachForMulticastAsync(multicast);

        var detalles = response.Responses.Select((r, i) => new {
            token = _tokens[i][..20] + "...",
            ok = r.IsSuccess,
            error = r.Exception?.Message
        });

        return Ok(new {
            enviados = response.SuccessCount,
            fallidos = response.FailureCount,
            detalles
        });
    }




    
}
