using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using WebDispositivosMqtt.Data;
using WebDispositivosMqtt.Data.Models;
using WebDispositivosMqtt.DataIdentity.Models;

namespace WebDispositivosMqtt.Services.Auth;

public class TokenService : ITokenService
{
    private readonly JwtOptions _jwtOptions;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly DatabaseContext _db;

    public TokenService(
        IOptions<JwtOptions> jwtOptions,
        UserManager<ApplicationUser> userManager,
        DatabaseContext db)
    {
        _jwtOptions = jwtOptions.Value;
        _userManager = userManager;
        _db = db;
    }

    public async Task<TokenResponse?> LoginAsync(string email, string password)
    {
        var user = await _userManager.FindByEmailAsync(email);
        if (user is null || !await _userManager.CheckPasswordAsync(user, password))
            return null;

        if (await _userManager.IsLockedOutAsync(user))
            return null;

        return await GenerateTokenPairAsync(user);
    }

    public async Task<TokenResponse?> RefreshAsync(string refreshToken)
    {
        var stored = await _db.RefreshTokens
            .FirstOrDefaultAsync(r => r.Token == refreshToken);

        if (stored is null || stored.RevokedAtUtc.HasValue || stored.ExpiresAtUtc < DateTime.UtcNow)
            return null;

        var appUser = await _userManager.FindByIdAsync(stored.UserId);
        if (appUser is null || await _userManager.IsLockedOutAsync(appUser))
            return null;

        // Rotación: revocar el token usado y emitir uno nuevo
        stored.RevokedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return await GenerateTokenPairAsync(appUser);
    }

    public async Task<bool> RevokeAsync(string refreshToken)
    {
        var stored = await _db.RefreshTokens
            .FirstOrDefaultAsync(r => r.Token == refreshToken && r.RevokedAtUtc == null);

        if (stored is null) return false;

        stored.RevokedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }

    private async Task<TokenResponse> GenerateTokenPairAsync(ApplicationUser user)
    {
        var roles = await _userManager.GetRolesAsync(user);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.Email, user.Email!),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };
        foreach (var role in roles)
            claims.Add(new Claim(ClaimTypes.Role, role));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtOptions.Key));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiresAt = DateTime.UtcNow.AddMinutes(_jwtOptions.AccessTokenExpiresMinutes);

        var jwtToken = new JwtSecurityToken(
            issuer: _jwtOptions.Issuer,
            audience: _jwtOptions.Audience,
            claims: claims,
            expires: expiresAt,
            signingCredentials: creds);

        var accessToken = new JwtSecurityTokenHandler().WriteToken(jwtToken);

        var newRefreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            Token = newRefreshToken,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(_jwtOptions.RefreshTokenExpiresDays),
        });
        await _db.SaveChangesAsync();

        return new TokenResponse(accessToken, newRefreshToken, expiresAt);
    }
}
