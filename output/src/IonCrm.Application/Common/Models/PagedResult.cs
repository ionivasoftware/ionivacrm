namespace IonCrm.Application.Common.Models;

/// <summary>Wraps a paginated list result with metadata.</summary>
/// <typeparam name="T">The item type.</typeparam>
public class PagedResult<T>
{
    /// <summary>Gets the items on the current page.</summary>
    public IReadOnlyList<T> Items { get; }

    /// <summary>Gets the total number of items across all pages.</summary>
    public int TotalCount { get; }

    /// <summary>Gets the current page number (1-based).</summary>
    public int Page { get; }

    /// <summary>Gets the number of items per page.</summary>
    public int PageSize { get; }

    /// <summary>Gets the total number of pages.</summary>
    public int TotalPages { get; }

    /// <summary>Gets a value indicating whether there is a previous page.</summary>
    public bool HasPreviousPage { get; }

    /// <summary>Gets a value indicating whether there is a next page.</summary>
    public bool HasNextPage { get; }

    /// <summary>Initialises a new <see cref="PagedResult{T}"/>.</summary>
    public PagedResult(IReadOnlyList<T> items, int totalCount, int page, int pageSize)
    {
        Items = items;
        TotalCount = totalCount;
        Page = page;
        PageSize = pageSize;
        TotalPages = pageSize > 0 ? (int)Math.Ceiling(totalCount / (double)pageSize) : 0;
        HasPreviousPage = page > 1;
        HasNextPage = page < TotalPages;
    }
}
