using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace TaskManagementSystem.Configuration;

/// <summary>
/// Strongly-typed "Jwt" configuration section (Options pattern).
/// Issuer/Audience/lifetime live in appsettings.json; the SigningKey is a SECRET and comes from
/// User Secrets (Development) or the Jwt__SigningKey environment variable (Production).
/// Validated at startup (ValidateOnStart) so a missing key stops the app immediately.
/// </summary>
public sealed class JwtSettings
{
    public const string SectionName = "Jwt";

    /// <summary>Who issued the token ("iss" claim).</summary>
    [Required]
    public string Issuer { get; set; } = string.Empty;

    /// <summary>Who the token is intended for ("aud" claim).</summary>
    [Required]
    public string Audience { get; set; } = string.Empty;

    /// <summary>HMAC-SHA256 key. At least 32 characters (256 bits) — shorter keys are brute-forceable.</summary>
    [Required]
    [MinLength(32)]
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>Access tokens are short-lived because they cannot be revoked before they expire.</summary>
    [Range(1, 60)]
    public int AccessTokenLifetimeMinutes { get; set; } = 15;

    /// <summary>The same key object is used to SIGN tokens (TokenService) and to VALIDATE them (JwtBearer).</summary>
    public SymmetricSecurityKey CreateSigningKey() => new(Encoding.UTF8.GetBytes(SigningKey));
}
