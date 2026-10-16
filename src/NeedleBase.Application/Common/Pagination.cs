namespace NeedleBase.Application.Common;

public static class PaginationDefaults
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;
}

public sealed class PaginatedResponse<T>
{
    public required IReadOnlyList<T> Data { get; init; }
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalPages { get; init; }
    public bool HasPrevious { get; init; }
    public bool HasNext { get; init; }

    public static PaginatedResponse<T> Create(IReadOnlyList<T> data, int totalCount, int page, int pageSize)
    {
        var totalPages = pageSize > 0
            ? (int)Math.Ceiling(totalCount / (double)pageSize)
            : 0;

        return new PaginatedResponse<T>
        {
            Data = data,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = totalPages,
            HasPrevious = page > 1,
            HasNext = page < totalPages
        };
    }

    public static (int Page, int PageSize) Normalize(
        int page,
        int pageSize,
        int defaultPageSize = PaginationDefaults.DefaultPageSize,
        int maxPageSize = PaginationDefaults.MaxPageSize)
    {
        if (page < 1)
        {
            page = 1;
        }

        if (pageSize < 1)
        {
            pageSize = defaultPageSize;
        }

        if (pageSize > maxPageSize)
        {
            pageSize = maxPageSize;
        }

        return (page, pageSize);
    }
}
