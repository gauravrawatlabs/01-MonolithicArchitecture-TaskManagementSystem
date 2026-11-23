using System.ComponentModel.DataAnnotations;

namespace TaskManagementSystem.Configuration;

/// <summary>
/// "Email" configuration section.
/// Development delivers emails as .html files into PickupDirectory (gitignored) instead of sending
/// them, so verification/reset links can be opened locally and tokens never end up in log files.
/// </summary>
public sealed class EmailSettings
{
    public const string SectionName = "Email";

    [Required]
    [EmailAddress]
    public string FromAddress { get; set; } = "no-reply@taskmanagement.local";

    /// <summary>Folder (relative to the content root, or absolute) where email files are written.</summary>
    [Required]
    public string PickupDirectory { get; set; } = "emails";

    /// <summary>
    /// Base URL of the client application that hosts the "confirm email" and "reset password" pages.
    /// Links in emails point there; the page then calls this API with the userId/email and token.
    /// </summary>
    [Required]
    [Url]
    public string ClientAppBaseUrl { get; set; } = "http://localhost:3000";
}
