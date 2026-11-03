using System.Text.Json;
using NeedleBase.Application.Contracts.Licensing;
using NeedleBase.Mobile.Data.Sync;

namespace NeedleBase.Mobile.Services;

/// <summary>
/// Keeps the last license status seen by sync across app restarts, so the banner and the read-only pause still apply
/// when the phone starts offline. Holds no key or token (the status DTO has neither). Cleared with the local data.
/// </summary>
public sealed class SubscriptionStatusStore
{
    public const string StorageKey = "subscription_status";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly ISecureStorageService _storage;

    // Saves run one at a time and each writes the gate's state at that moment, so a slow save can never put back a
    // status that a later change (sign-out wipe, new shop) already replaced.
    private readonly SemaphoreSlim _saving = new(1, 1);

    public SubscriptionStatusStore(ISecureStorageService storage)
    {
        _storage = storage;
        LocalSubscriptionGate.Changed += OnChanged;
    }

    private sealed record Saved(string BusinessId, SubscriptionStatusDto Status);

    /// <summary>Loads the saved status when it belongs to <paramref name="businessId"/>; anything else is dropped.</summary>
    public async Task RestoreAsync(string? businessId)
    {
        if (string.IsNullOrWhiteSpace(businessId) || LocalSubscriptionGate.Current is not null)
        {
            return;
        }

        try
        {
            var json = await _storage.GetAsync(StorageKey).ConfigureAwait(false);
            if (string.IsNullOrEmpty(json))
            {
                return;
            }

            var saved = JsonSerializer.Deserialize<Saved>(json, Json);
            if (saved is not null && saved.BusinessId == businessId)
            {
                LocalSubscriptionGate.Set(businessId, saved.Status);
            }
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            _storage.Remove(StorageKey); // unreadable copy: wait for the next sync rather than guess
        }
    }

    private void OnChanged(object? sender, EventArgs e) => _ = SaveAsync();

    private async Task SaveAsync()
    {
        await _saving.WaitAsync().ConfigureAwait(false);
        try
        {
            var businessId = LocalSubscriptionGate.BusinessId;
            var status = LocalSubscriptionGate.Current;
            if (businessId is null || status is null)
            {
                _storage.Remove(StorageKey);
                return;
            }

            await _storage.SetAsync(StorageKey, JsonSerializer.Serialize(new Saved(businessId, status), Json)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Saving license status failed: {ex.Message}");
        }
        finally
        {
            _saving.Release();
        }
    }
}
