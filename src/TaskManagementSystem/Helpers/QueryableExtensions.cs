using System.Linq.Expressions;
using TaskManagementSystem.Enums;
using TaskManagementSystem.Models.DTOs.Common;

namespace TaskManagementSystem.Helpers;

/// <summary>Small LINQ helpers for sorting and paging IQueryable (translated to SQL by EF Core).</summary>
public static class QueryableExtensions
{
    public static IOrderedQueryable<T> OrderByDirection<T, TKey>(
        this IQueryable<T> source, Expression<Func<T, TKey>> keySelector, SortDirection direction) =>
        direction == SortDirection.Desc ? source.OrderByDescending(keySelector) : source.OrderBy(keySelector);

    public static IOrderedQueryable<T> ThenByDirection<T, TKey>(
        this IOrderedQueryable<T> source, Expression<Func<T, TKey>> keySelector, SortDirection direction) =>
        direction == SortDirection.Desc ? source.ThenByDescending(keySelector) : source.ThenBy(keySelector);

    /// <summary>OFFSET/FETCH paging. Always apply AFTER a deterministic ORDER BY.</summary>
    public static IQueryable<T> ApplyPaging<T>(this IQueryable<T> source, PagedQueryParameters paging) =>
        source.Skip((paging.Page - 1) * paging.PageSize).Take(paging.PageSize);
}
