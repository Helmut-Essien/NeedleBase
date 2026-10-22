namespace NeedleBase.Web.Pages;

/// <summary>Previous/Next pager that keeps the page's other query values.</summary>
public sealed class PagerModel
{
    private readonly string _path;
    private readonly IReadOnlyDictionary<string, string?> _query;

    public PagerModel(string path, int page, int totalPages, int totalCount, IReadOnlyDictionary<string, string?> query)
    {
        _path = path;
        Page = page;
        TotalPages = totalPages;
        TotalCount = totalCount;
        _query = query;
    }

    public int Page { get; }
    public int TotalPages { get; }
    public int TotalCount { get; }

    public string Url(int page)
    {
        var parts = _query.Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
            .Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value!)}")
            .Append($"p={page}");
        return $"{_path}?{string.Join('&', parts)}";
    }
}
