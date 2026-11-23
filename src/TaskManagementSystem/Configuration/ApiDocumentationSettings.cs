namespace TaskManagementSystem.Configuration;

/// <summary>
/// "ApiDocumentation" configuration section. Enabled in appsettings.Development.json, disabled by
/// default elsewhere: production doesn't advertise its API surface. A flag (instead of hard-coding
/// "Development") lets other environments — e.g. integration tests or an internal staging server —
/// switch the docs on through configuration alone.
/// </summary>
public sealed class ApiDocumentationSettings
{
    public const string SectionName = "ApiDocumentation";

    public bool Enabled { get; set; }
}
