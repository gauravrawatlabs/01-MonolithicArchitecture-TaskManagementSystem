namespace TaskManagementSystem.Data.Configurations;

/// <summary>Builds SQL for CHECK constraints so they stay in sync with the C# enums.</summary>
internal static class CheckConstraintSql
{
    /// <summary>
    /// Produces e.g. <c>[Status] IN (N'Todo', N'InProgress', ...)</c> from the enum's member names.
    /// Adding an enum member changes this SQL, so EF Core generates a migration that updates the constraint.
    /// </summary>
    public static string EnumValues<TEnum>(string columnName) where TEnum : struct, Enum
    {
        var allowedValues = string.Join(", ", Enum.GetNames<TEnum>().Select(name => $"N'{name}'"));
        return $"[{columnName}] IN ({allowedValues})";
    }
}
