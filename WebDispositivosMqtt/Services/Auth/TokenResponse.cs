namespace WebDispositivosMqtt.Services.Auth;

public record TokenResponse(
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresAtUtc);
