using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using TaskManagementSystem.Configuration;
using TaskManagementSystem.Constants;
using TaskManagementSystem.Interfaces.Services;
using TaskManagementSystem.Models.Entities;

namespace TaskManagementSystem.Services;

/// <summary>
/// Builds and signs JWT access tokens (HMAC-SHA256).
///
/// A JWT is three Base64Url parts: header.payload.signature
///   header    → {"alg":"HS256","typ":"JWT"}
///   payload   → claims: sub, email, role, email_verified, iss, aud, exp, ...
///   signature → HMACSHA256(header + "." + payload, SigningKey)
/// The payload is only ENCODED, not encrypted — anyone can read it, so never put secrets in claims.
/// The signature proves the server issued it and nobody changed it.
///
/// Registered as a SINGLETON: it is stateless and depends only on singletons (options, TimeProvider).
/// </summary>
public sealed class TokenService(IOptions<JwtSettings> jwtOptions, TimeProvider timeProvider) : ITokenService
{
    private readonly JsonWebTokenHandler _tokenHandler = new();

    public AccessToken CreateAccessToken(ApplicationUser user, IEnumerable<string> roles)
    {
        var settings = jwtOptions.Value;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var expiresAtUtc = now.AddMinutes(settings.AccessTokenLifetimeMinutes);

        var claims = new List<Claim>
        {
            new(AppClaimTypes.Subject, user.Id.ToString()),
            new(AppClaimTypes.Email, user.Email ?? string.Empty),
            new(AppClaimTypes.GivenName, user.FirstName),
            new(AppClaimTypes.FamilyName, user.LastName),
            new(AppClaimTypes.TokenId, Guid.NewGuid().ToString()),
            new(AppClaimTypes.EmailVerified, user.EmailConfirmed ? "true" : "false", ClaimValueTypes.Boolean)
        };
        claims.AddRange(roles.Select(role => new Claim(AppClaimTypes.Role, role)));

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = expiresAtUtc,
            SigningCredentials = new SigningCredentials(settings.CreateSigningKey(), SecurityAlgorithms.HmacSha256)
        };

        return new AccessToken(_tokenHandler.CreateToken(descriptor), expiresAtUtc);
    }
}
