using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using TaskManagementSystem.Configuration;
using TaskManagementSystem.Constants;
using TaskManagementSystem.Models.Entities;

namespace TaskManagementSystem.Data.Seed;

/// <summary>
/// Startup seeding for data that can't go into a migration (HasData):
/// users need UserManager to hash the password, and the password is a secret from configuration.
/// Idempotent: safe to run on every startup.
/// </summary>
public sealed class DbSeeder(
    UserManager<ApplicationUser> userManager,
    IOptions<AdminSeedSettings> adminOptions,
    TimeProvider timeProvider,
    ILogger<DbSeeder> logger)
{
    public async Task SeedAsync()
    {
        await SeedAdminAsync();
    }

    private async Task SeedAdminAsync()
    {
        var settings = adminOptions.Value;
        if (string.IsNullOrWhiteSpace(settings.Email) || string.IsNullOrWhiteSpace(settings.Password))
        {
            logger.LogWarning(
                "Admin seeding skipped: configure {EmailKey} and {PasswordKey} (User Secrets or environment variables).",
                $"{AdminSeedSettings.SectionName}:Email", $"{AdminSeedSettings.SectionName}:Password");
            return;
        }

        var admin = await userManager.FindByEmailAsync(settings.Email);
        if (admin is null)
        {
            admin = new ApplicationUser
            {
                UserName = settings.Email,
                Email = settings.Email,
                EmailConfirmed = true,
                FirstName = settings.FirstName,
                LastName = settings.LastName,
                CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime
            };

            var createResult = await userManager.CreateAsync(admin, settings.Password);
            if (!createResult.Succeeded)
            {
                throw new InvalidOperationException(
                    "Could not seed the admin user: " + string.Join("; ", createResult.Errors.Select(e => e.Description)));
            }

            logger.LogInformation("Seeded admin user {UserId}", admin.Id);
        }

        if (!await userManager.IsInRoleAsync(admin, Roles.Admin))
        {
            var roleResult = await userManager.AddToRoleAsync(admin, Roles.Admin);
            if (!roleResult.Succeeded)
            {
                throw new InvalidOperationException(
                    "Could not add the admin user to the Admin role: " +
                    string.Join("; ", roleResult.Errors.Select(e => e.Description)));
            }
        }
    }
}
