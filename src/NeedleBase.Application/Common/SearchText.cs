namespace NeedleBase.Application.Common;

/// <summary>
/// Case-insensitive search helpers that translate on both Postgres and SQLite
/// (<c>lower(col) LIKE %term%</c>). Order refs are stored upper case.
/// </summary>
public static class SearchText
{
    public static string Lower(string term) => term.Trim().ToLowerInvariant();

    public static string Ref(string term) => term.Trim().TrimStart('#').ToUpperInvariant();
}
