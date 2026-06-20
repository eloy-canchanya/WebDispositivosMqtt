namespace WebDispositivosMqtt.Services.Auth;

public class JwtOptions
{
    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public int AccessTokenExpiresMinutes { get; set; } = 15;
    public int RefreshTokenExpiresDays { get; set; } = 30;
}
