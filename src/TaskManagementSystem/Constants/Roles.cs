namespace TaskManagementSystem.Constants;

/// <summary>
/// Global application role names (ASP.NET Core Identity roles).
/// Use these constants instead of magic strings, e.g. [Authorize(Roles = Roles.Admin)].
/// </summary>
public static class Roles
{
    public const string Admin = "Admin";
    public const string Manager = "Manager";
    public const string User = "User";

    public static readonly IReadOnlyList<string> All = [Admin, Manager, User];
}
