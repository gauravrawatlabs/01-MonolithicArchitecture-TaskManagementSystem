using Microsoft.IdentityModel.JsonWebTokens;

namespace TaskManagementSystem.Constants;

/// <summary>
/// Claim names used inside our JWTs. We use the short standard JWT names ("sub", "role")
/// and disable ASP.NET Core's inbound claim mapping (MapInboundClaims = false), so the names
/// in the token are exactly the names we read from HttpContext.User.
/// </summary>
public static class AppClaimTypes
{
    /// <summary>User id (Guid).</summary>
    public const string Subject = JwtRegisteredClaimNames.Sub;          // "sub"
    public const string Email = JwtRegisteredClaimNames.Email;          // "email"
    public const string GivenName = JwtRegisteredClaimNames.GivenName;  // "given_name"
    public const string FamilyName = JwtRegisteredClaimNames.FamilyName; // "family_name"
    public const string TokenId = JwtRegisteredClaimNames.Jti;          // "jti" — unique per token
    public const string Role = "role";

    /// <summary>"true"/"false". Used by the claims-based "EmailVerified" policy in Phase 7.</summary>
    public const string EmailVerified = "email_verified";
}
