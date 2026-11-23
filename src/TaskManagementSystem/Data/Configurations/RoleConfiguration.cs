using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskManagementSystem.Constants;
using TaskManagementSystem.Data.Seed;

namespace TaskManagementSystem.Data.Configurations;

/// <summary>
/// Seeds the three application roles into AspNetRoles through the migration (HasData).
/// Roles are reference data: every environment needs exactly these rows.
/// </summary>
public class RoleConfiguration : IEntityTypeConfiguration<IdentityRole<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityRole<Guid>> builder)
    {
        builder.HasData(
            CreateRole(SeedData.AdminRoleId, Roles.Admin, SeedData.AdminRoleConcurrencyStamp),
            CreateRole(SeedData.ManagerRoleId, Roles.Manager, SeedData.ManagerRoleConcurrencyStamp),
            CreateRole(SeedData.UserRoleId, Roles.User, SeedData.UserRoleConcurrencyStamp));
    }

    private static IdentityRole<Guid> CreateRole(Guid id, string name, string concurrencyStamp) => new()
    {
        Id = id,
        Name = name,
        NormalizedName = name.ToUpperInvariant(), // Identity looks roles up by NormalizedName
        ConcurrencyStamp = concurrencyStamp
    };
}
