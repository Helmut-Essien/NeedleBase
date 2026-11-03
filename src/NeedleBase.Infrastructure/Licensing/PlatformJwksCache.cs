using System.Net.Http.Json;
using System.Security.Cryptography;

namespace NeedleBase.Infrastructure.Licensing;

/// <summary>
/// Platform's public keys (GET /api/licenses/keys), cached for an hour. An unknown <c>kid</c> (key rotation) forces
/// one refetch, at most every 5 minutes, so a forged kid can't make us hammer Platform.
/// </summary>
public sealed class PlatformJwksCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(1);
    private static readonly TimeSpan MinRefetch = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyDictionary<string, ECParameters> _keys = new Dictionary<string, ECParameters>();
    private DateTimeOffset _fetchedAt = DateTimeOffset.MinValue;

    public async Task<IReadOnlyDictionary<string, ECParameters>> GetAsync(
        HttpClient http, bool keyMissing, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!NeedsFetch(keyMissing, now))
        {
            return _keys;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!NeedsFetch(keyMissing, now))
            {
                return _keys;
            }

            PlatformJwks? jwks;
            try
            {
                jwks = await http.GetFromJsonAsync<PlatformJwks>("api/licenses/keys", cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (_keys.Count > 0 && ex is HttpRequestException or System.Text.Json.JsonException or NotSupportedException
                                           or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                // Stale-if-error: keep the keys we have (rotation is rare) and try again after MinRefetch.
                _fetchedAt = now - Ttl + MinRefetch;
                return _keys;
            }

            var keys = new Dictionary<string, ECParameters>(StringComparer.Ordinal);
            foreach (var k in jwks?.Keys ?? [])
            {
                if (k is { Kty: "EC", Crv: "P-256", Kid: { Length: > 0 } kid, X: { } x, Y: { } y })
                {
                    var p = new ECParameters
                    {
                        Curve = ECCurve.NamedCurves.nistP256,
                        Q = new ECPoint { X = LicenseTokenVerifier.Base64UrlDecode(x), Y = LicenseTokenVerifier.Base64UrlDecode(y) }
                    };
                    if (p.Q.X!.Length == 32 && p.Q.Y!.Length == 32)
                    {
                        keys[kid] = p;
                    }
                }
            }

            if (keys.Count == 0 && _keys.Count > 0)
            {
                // An empty/garbled key set never wipes keys that worked; retry after MinRefetch.
                _fetchedAt = now - Ttl + MinRefetch;
                return _keys;
            }

            _keys = keys;
            _fetchedAt = now;
            return keys;
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool NeedsFetch(bool keyMissing, DateTimeOffset now) =>
        now - _fetchedAt > Ttl || (keyMissing && now - _fetchedAt > MinRefetch);
}
