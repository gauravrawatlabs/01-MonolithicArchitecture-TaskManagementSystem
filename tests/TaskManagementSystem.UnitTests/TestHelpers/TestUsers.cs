using System.Security.Claims;
using TaskManagementSystem.Constants;

namespace TaskManagementSystem.UnitTests.TestHelpers;

/// <summary>
/// Builds ClaimsPrincipals shaped exactly like the ones the JwtBearer middleware creates
/// ("sub" + "role" claims, role claim type "role"), so services and handlers see a realistic user.
/// </summary>
public static class TestUsers
{
    public static ClaimsPrincipal Create(Guid userId, params string[] roles)
    {
        var claims = new List<Claim> { new(AppClaimTypes.Subject, userId.ToString()) };
        claims.AddRange(roles.Select(role => new Claim(AppClaimTypes.Role, role)));

        var identity = new ClaimsIdentity(
            claims, authenticationType: "Test", nameType: AppClaimTypes.Email, roleType: AppClaimTypes.Role);
        return new ClaimsPrincipal(identity);
    }

    public static ClaimsPrincipal Admin() => Create(Guid.NewGuid(), Roles.Admin);
}
