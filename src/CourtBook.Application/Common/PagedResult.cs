namespace CourtBook.Application.Common;

/// <summary>
/// Wraps a paginated list result returned by the API.
/// </summary>
public sealed class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = [];
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(TotalCount / (double)PageSize) : 0;
    public bool HasNextPage => Page < TotalPages;
    public bool HasPreviousPage => Page > 1;

    public static PagedResult<T> From(IReadOnlyList<T> items, int totalCount, int page, int pageSize)
        => new() { Items = items, TotalCount = totalCount, Page = page, PageSize = pageSize };

    public static PagedResult<T> Empty(int page = 1, int pageSize = 10)
        => new() { Items = [], TotalCount = 0, Page = page, PageSize = pageSize };
}

/// <summary>
/// Incoming pagination and sorting parameters from clients.
/// </summary>
public class PagedRequest
{
    private const int DefaultPageSize = 12;
    private const int MaxPageSize = 50;
    private int _pageSize = DefaultPageSize;

    /// <summary>1-based page number.</summary>
    public int Page { get; set; } = 1;

    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = Math.Clamp(value, 1, MaxPageSize);
    }

    /// <summary>Optional full-text search query.</summary>
    public string? Search { get; set; }

    /// <summary>Property name to sort by (e.g. "Name", "Price").</summary>
    public string? SortBy { get; set; }

    public bool SortDescending { get; set; } = false;

    /// <summary>Zero-based offset for EF Core queries.</summary>
    public int Skip => (Page - 1) * PageSize;
}
