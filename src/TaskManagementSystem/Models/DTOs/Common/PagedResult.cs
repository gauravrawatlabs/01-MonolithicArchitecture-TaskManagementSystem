namespace TaskManagementSystem.Models.DTOs.Common;

/// <summary>One page of results plus the information a client needs to render paging controls.</summary>
/// <param name="Items">The items on this page.</param>
/// <param name="Page">1-based page number.</param>
/// <param name="PageSize">Maximum items per page.</param>
/// <param name="TotalCount">Total number of items matching the filters (all pages).</param>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    /// <summary>Total number of pages.</summary>
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public bool HasPreviousPage => Page > 1;

    public bool HasNextPage => Page < TotalPages;
}
