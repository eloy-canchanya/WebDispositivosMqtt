using Microsoft.AspNetCore.Mvc;
using WebDispositivosMqtt.Services.Auth;

namespace WebDispositivosMqtt.Controllers.Api;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly ITokenService _tokenService;

    public AuthController(ITokenService tokenService) => _tokenService = tokenService;

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var result = await _tokenService.LoginAsync(request.Email, request.Password);
        if (result is null)
            return Unauthorized(new { message = "Credenciales inválidas" });
        return Ok(result);
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request)
    {
        var result = await _tokenService.RefreshAsync(request.RefreshToken);
        if (result is null)
            return Unauthorized(new { message = "Refresh token inválido o expirado" });
        return Ok(result);
    }

    [HttpPost("revoke")]
    public async Task<IActionResult> Revoke([FromBody] RefreshRequest request)
    {
        await _tokenService.RevokeAsync(request.RefreshToken);
        return Ok();
    }
}

public record LoginRequest(string Email, string Password);
public record RefreshRequest(string RefreshToken);
