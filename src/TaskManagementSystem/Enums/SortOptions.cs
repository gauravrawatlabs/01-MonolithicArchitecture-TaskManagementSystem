namespace TaskManagementSystem.Enums;

/// <summary>Sort direction for list endpoints (?sortDirection=Asc|Desc).</summary>
public enum SortDirection
{
    Asc,
    Desc
}

/// <summary>
/// Whitelisted sort fields for tasks (?sortBy=...). Enums instead of free-text column names:
/// the client can only pick from this list (invalid values → 400), and each value maps to a
/// compile-time-checked LINQ expression — no dynamic SQL, no injection, no "unknown column" errors.
/// </summary>
public enum TaskSortField
{
    DueDate,
    Priority,
    Status,
    Title,
    CreatedAt,
    UpdatedAt
}

/// <summary>Whitelisted sort fields for projects.</summary>
public enum ProjectSortField
{
    CreatedAt,
    Name,
    StartDate,
    EndDate
}

/// <summary>Whitelisted sort fields for users (admin list).</summary>
public enum UserSortField
{
    Email,
    Name,
    CreatedAt,
    LastLogin
}
