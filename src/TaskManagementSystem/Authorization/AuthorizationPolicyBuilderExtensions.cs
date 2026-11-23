using Microsoft.AspNetCore.Authorization;
using TaskManagementSystem.Constants;

namespace TaskManagementSystem.Authorization;

/// <summary>Reusable requirement building blocks for policy definitions.</summary>
public static class AuthorizationPolicyBuilderExtensions
{
    /// <summary>
    /// CLAIMS-based requirement: the token must contain email_verified = true.
    /// RequireAssertion + bool.TryParse accepts "true"/"True"; RequireClaim(type, "true")
    /// would do an exact, case-sensitive string match.
    /// </summary>
    public static AuthorizationPolicyBuilder RequireVerifiedEmail(this AuthorizationPolicyBuilder policy) =>
        policy.RequireAssertion(context => context.User.HasClaim(claim =>
            claim.Type == AppClaimTypes.EmailVerified &&
            bool.TryParse(claim.Value, out var verified) && verified));
}
