using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using MusicReviews.Application.Auth;
using MusicReviews.Domain.Entities;

namespace MusicReviews.Infrastructure.Identity;

/// <inheritdoc cref="ITokenService"/>
public sealed class TokenService : ITokenService
{
    /// <summary>Bytes de entropia del refresh token (256 bits).</summary>
    private const int RefreshTokenBytes = 32;

    private readonly JwtOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly SigningCredentials _signingCredentials;

    public TokenService(IOptions<JwtOptions> options, TimeProvider timeProvider)
    {
        _options = options.Value;
        _timeProvider = timeProvider;

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        _signingCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    }

    public AccessToken CreateAccessToken(ApplicationUser user, IEnumerable<string> roles)
    {
        var now = _timeProvider.GetUtcNow();
        var expiresAt = now.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Name, user.UserName ?? string.Empty),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty)
        };

        // Un claim "role" por rol: asi [Authorize(Roles = "Admin")] funciona sin mapeos extra.
        claims.AddRange(roles.Select(role => new Claim(AuthClaimTypes.Role, role)));

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = _signingCredentials
        };

        var handler = new JsonWebTokenHandler();
        var token = handler.CreateToken(descriptor);

        return new AccessToken(token, expiresAt);
    }

    public GeneratedRefreshToken CreateRefreshToken()
    {
        // Aleatoriedad criptografica, no Guid: un Guid v4 no esta pensado como secreto.
        var bytes = RandomNumberGenerator.GetBytes(RefreshTokenBytes);
        var value = Base64UrlEncoder.Encode(bytes);

        var expiresAt = _timeProvider.GetUtcNow().AddDays(_options.RefreshTokenDays);

        return new GeneratedRefreshToken(value, HashRefreshToken(value), expiresAt);
    }

    public string HashRefreshToken(string refreshToken)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken));
        return Convert.ToHexStringLower(hash);
    }
}

/// <summary>Nombres de claim usados por la aplicacion, sin el mapeo legacy de .NET.</summary>
public static class AuthClaimTypes
{
    public const string Sub = JwtRegisteredClaimNames.Sub;
    public const string Name = JwtRegisteredClaimNames.Name;
    public const string Email = JwtRegisteredClaimNames.Email;
    public const string Role = "role";
}
