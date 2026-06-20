namespace WebDispositivosMqtt.Services.Auth;

public interface ITokenService
{
    Task<TokenResponse?> LoginAsync(string email, string password);
    Task<TokenResponse?> RefreshAsync(string refreshToken);
    Task<bool> RevokeAsync(string refreshToken);
}
