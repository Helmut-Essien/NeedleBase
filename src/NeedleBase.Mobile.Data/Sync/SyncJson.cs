using System.Text.Json;

namespace NeedleBase.Mobile.Data.Sync;

public static class SyncJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T? Deserialize<T>(string? json) =>
        string.IsNullOrWhiteSpace(json) ? default : JsonSerializer.Deserialize<T>(json, Options);
}

/// <summary>Raised after local changes are queued, so the sync service can schedule a push.</summary>
public static class LocalSyncSignals
{
    public static event EventHandler? QueueChanged;

    public static void RaiseQueueChanged() => QueueChanged?.Invoke(null, EventArgs.Empty);
}
