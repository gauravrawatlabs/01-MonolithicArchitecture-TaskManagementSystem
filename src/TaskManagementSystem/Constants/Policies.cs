namespace TaskManagementSystem.Constants;

/// <summary>
/// Named authorization policies, used as [Authorize(Policy = Policies.X)].
/// A policy is a named set of requirements; controllers say WHAT is required,
/// the policy definition (AddAuthorizationPolicies) says HOW it is checked.
/// </summary>
public static class Policies
{
    /// <summary>Role-based: the user must have the Admin role.</summary>
    public const string AdminOnly = nameof(AdminOnly);

    /// <summary>Claims-based: the token must contain email_verified = true.</summary>
    public const string EmailVerified = nameof(EmailVerified);

    /// <summary>Composite: Admin role AND verified email — create/update/delete categories.</summary>
    public const string CanManageCategories = nameof(CanManageCategories);
}
