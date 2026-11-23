namespace TaskManagementSystem.Constants;

/// <summary>
/// Single source of truth for password rules: used by BOTH the Identity options (server-side
/// enforcement when hashing) and the FluentValidation validators (friendly 400 messages).
/// </summary>
public static class PasswordRules
{
    public const int MinLength = 8;
    public const int MaxLength = 128;
    public const int RequiredUniqueChars = 4;
}
