using Microsoft.Extensions.Caching.Memory;
using NeedleBase.Application.Common;

namespace NeedleBase.Infrastructure.Licensing;

/// <summary>
/// The shop id a registration sends to Platform as its device id. A retry with the same key + email within 30 minutes
/// reuses it, so a timeout after Platform already activated the device (or a failure after the check) doesn't use up
/// the key's one device slot on a shop that was never created.
/// </summary>
public sealed class PendingShopIds
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(30);
    private readonly IMemoryCache _cache;

    public PendingShopIds(IMemoryCache cache)
    {
        _cache = cache;
    }

    public string For(string fingerprint) =>
        _cache.GetOrCreate("pending-shop:" + fingerprint, e =>
        {
            e.AbsoluteExpirationRelativeToNow = Ttl;
            return IdGenerator.NewId();
        })!;

    public void Done(string fingerprint) => _cache.Remove("pending-shop:" + fingerprint);
}
