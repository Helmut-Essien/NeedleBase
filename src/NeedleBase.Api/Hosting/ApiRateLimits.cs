/// <summary>Sync endpoint rate limit (per user). Generous: 200-row pages, so a 60,000-row first pull fits the burst.</summary>
internal static class AuthRateLimits
{
    public const string RefreshPolicy = "refresh";
    public const string RegisterPolicy = "register";
}

internal static class SyncRateLimit
{
    public const string PolicyName = "sync";
    public const int Burst = 300;
    public const int TokensPerSecond = 10;

    public static string PartitionKey(HttpContext context) =>
        context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
        ?? context.User.FindFirst("sub")?.Value
        ?? "ip:" + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
}
