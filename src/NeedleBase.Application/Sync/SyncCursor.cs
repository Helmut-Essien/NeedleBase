using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NeedleBase.Application.Sync;

/// <summary>
/// Opaque pull cursor (base64url JSON), version 2. Phases run in dependency order: 0 customers,
/// 1 measurements, 2 orders, 3 deletes. Every phase is capped below <see cref="Cap"/> (the snapshot
/// xmin when this sync started): all transactions below it have finished, so nothing can later commit
/// with a smaller id. A fresh cursor starts deletes at the cap: a first pull needs no tombstones.
/// <see cref="IssuedTicks"/> lets the server refuse a cursor older than tombstone retention.
/// </summary>
public sealed class SyncCursor
{
    public const int CurrentVersion = 2;
    public const int PhaseCustomers = 0;
    public const int PhaseMeasurements = 1;
    public const int PhaseOrders = 2;
    public const int PhaseDeletes = 3;

    [JsonPropertyName("v")] public int Version { get; set; } = CurrentVersion;
    [JsonPropertyName("p")] public int Phase { get; set; }
    [JsonPropertyName("x")] public long Cap { get; set; }
    [JsonPropertyName("t")] public long IssuedTicks { get; set; }

    /// <summary>Server sync epoch the positions belong to (0 before any cluster change; absent in older cursors = 0).</summary>
    [JsonPropertyName("e")] public long Epoch { get; set; }
    [JsonPropertyName("c")] public SyncPosition? Customers { get; set; }
    [JsonPropertyName("m")] public SyncPosition? Measurements { get; set; }
    [JsonPropertyName("o")] public SyncPosition? Orders { get; set; }
    [JsonPropertyName("d")] public SyncPosition? Deletes { get; set; }

    /// <summary>Highest transaction position this cursor has read up to (0 if none).</summary>
    [JsonIgnore]
    public long HighestPosition =>
        new[] { Customers?.Tx ?? 0, Measurements?.Tx ?? 0, Orders?.Tx ?? 0, Deletes?.Tx ?? 0 }.Max();

    /// <summary>Never synced (no cursor given): a first pull.</summary>
    public bool IsFresh => Cap == 0 && IssuedTicks == 0;

    /// <summary>Start of a sync run: a new watermark is taken.</summary>
    public bool NeedsWatermark => Cap == 0;

    public string Encode()
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(this);
        return Convert.ToBase64String(json).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static bool TryDecode(string? value, out SyncCursor cursor)
    {
        cursor = new SyncCursor();
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        try
        {
            var base64 = value.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');
            var decoded = JsonSerializer.Deserialize<SyncCursor>(Encoding.UTF8.GetString(Convert.FromBase64String(base64)));
            if (decoded is null || decoded.Version != CurrentVersion || decoded.Phase is < PhaseCustomers or > PhaseDeletes)
            {
                return false;
            }

            cursor = decoded;
            return true;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
        {
            return false;
        }
    }
}

/// <summary>Keyset position: last (transaction id, row id) sent for one stream.</summary>
public sealed record SyncPosition(
    [property: JsonPropertyName("x")] long Tx,
    [property: JsonPropertyName("i")] string Id);
