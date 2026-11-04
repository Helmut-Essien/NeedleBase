namespace NeedleBase.Api.Configuration;

/// <summary>
/// Single-site hosting (docs/deployment.md): the Web is the IIS site root and the Api is the IIS sub-application
/// <c>/api</c>. IIS integration then moves "/api" from the path into <see cref="HttpRequest.PathBase"/>, but every Api
/// route already starts with "api/" (clients call <c>{site}/api/...</c>). Putting the segment back keeps one URL
/// scheme for every layout: <c>https://site/api/orders</c> works the same on its own host, in the sub-application
/// and in tests, and links/Location headers keep the "/api" prefix. Any other PathBase is left alone.
/// </summary>
public static class ApiPathBase
{
    public const string Segment = "/api";

    public static void Restore(HttpRequest request)
    {
        if (request.PathBase.Equals(new PathString(Segment), StringComparison.OrdinalIgnoreCase))
        {
            request.Path = new PathString(Segment).Add(request.Path);
            request.PathBase = PathString.Empty;
        }
    }
}
