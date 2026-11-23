namespace TaskManagementSystem.Models.DTOs.Common;

/// <summary>
/// Base class for every list endpoint's query parameters: one definition of paging
/// (and one validator, PagedQueryValidator) instead of copies in every query class.
/// </summary>
public abstract class PagedQueryParameters
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    /// <summary>1-based page number (default 1).</summary>
    public int Page { get; init; } = 1;

    /// <summary>Items per page, 1–100 (default 20).</summary>
    public int PageSize { get; init; } = DefaultPageSize;
}
