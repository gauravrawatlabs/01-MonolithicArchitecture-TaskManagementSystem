namespace TaskManagementSystem.Configuration;

/// <summary>
/// "Cors" configuration section: which BROWSER origins (front-end apps) may call this API.
/// CORS is enforced by browsers only — it is not an authentication mechanism and does nothing
/// against curl/Postman/server-to-server calls. Authorization stays the API's job.
/// </summary>
public sealed class CorsSettings
{
    public const string SectionName = "Cors";
    public const string PolicyName = "Frontend";

    /// <summary>Exact origins, e.g. "http://localhost:3000". Never "*" for an authenticated API.</summary>
    public string[] AllowedOrigins { get; set; } = [];
}
