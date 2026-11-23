namespace TaskManagementSystem.Configuration;

/// <summary>
/// "SeedAdmin" configuration section: the first Admin account created at startup.
/// Email and Password are secrets → User Secrets (Development) or environment variables.
/// If they are not configured, seeding is skipped with a warning.
/// </summary>
public sealed class AdminSeedSettings
{
    public const string SectionName = "SeedAdmin";

    public string? Email { get; set; }
    public string? Password { get; set; }
    public string FirstName { get; set; } = "System";
    public string LastName { get; set; } = "Administrator";
}
