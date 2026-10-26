using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NeedleBase.Application.Contracts.Sync;
using NeedleBase.Domain.Enums;
using NeedleBase.Mobile.Data.Data;
using NeedleBase.Mobile.Data.Sync;
using NeedleBase.Mobile.Services;

namespace NeedleBase.Mobile.ViewModels;

/// <summary>Home SyncChip + Sync details page. Text only states what is true (offline-sync.md honesty).</summary>
public partial class SyncStatusViewModel : BaseViewModel
{
    private readonly SyncService _sync;
    private readonly IAuthService _auth;
    private readonly INavigationService _navigation;

    public SyncStatusViewModel(SyncService sync, IAuthService auth, INavigationService navigation)
    {
        _sync = sync;
        _auth = auth;
        _navigation = navigation;
        Title = "Sync";
        _sync.StatusChanged += (_, s) => MainThread.BeginInvokeOnMainThread(() => Apply(s));
        Apply(_sync.Current);
    }

    [ObservableProperty] private string chipText = "Synced";
    [ObservableProperty] private string chipDetail = string.Empty;
    [ObservableProperty] private Color chipColor = Colors.Green;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSyncNow))]
    private bool isSyncing;

    [ObservableProperty] private string lastSyncText = "Not synced yet";
    [ObservableProperty] private string connectionText = string.Empty;
    [ObservableProperty] private string? errorText;
    [ObservableProperty] private bool needsSignIn;

    public ObservableCollection<SyncItemRow> Items { get; } = new();
    public bool HasItems => Items.Count > 0;

    /// <summary>"Everything synced" only after the list really loaded empty (not before loading, not after an error).</summary>
    public bool IsEmpty => _itemsLoaded && ListError is null && Items.Count == 0;

    private bool _itemsLoaded;

    /// <summary>The waiting list couldn't be read from this device; the page offers Try again.</summary>
    [ObservableProperty] private string? listError;

    public bool HasListError => ListError is not null;

    /// <summary>Failed + conflicting changes (shown first, they need a choice).</summary>
    [ObservableProperty] private int attentionCount;

    public bool HasAttention => AttentionCount > 0;

    public string AttentionText => AttentionCount == 1 ? "1 change needs your choice" : $"{AttentionCount} changes need your choice";

    public string WaitingHeading => AttentionCount > 0 && AttentionCount < Items.Count
        ? $"{Items.Count - AttentionCount} more waiting to sync"
        : AttentionCount == 0 ? "Waiting on this device" : string.Empty;

    partial void OnListErrorChanged(string? value)
    {
        OnPropertyChanged(nameof(HasListError));
        OnPropertyChanged(nameof(IsEmpty));
    }

    partial void OnAttentionCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasAttention));
        OnPropertyChanged(nameof(AttentionText));
        OnPropertyChanged(nameof(WaitingHeading));
    }

    /// <summary>Needs-attention rows first (conflicts, then failures), then waiting rows in push order.</summary>
    public static IReadOnlyList<SyncQueueEntry> Ordered(IEnumerable<SyncQueueEntry> entries) =>
        entries.Select((e, i) => (e, i))
            .OrderBy(x => x.e.State == SyncQueueState.Conflict ? 0 : x.e.State == SyncQueueState.Failed ? 1 : 2)
            .ThenBy(x => x.i)
            .Select(x => x.e)
            .ToList();
    public bool CanSyncNow => !IsSyncing;
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);

    partial void OnErrorTextChanged(string? value) => OnPropertyChanged(nameof(HasError));

    public static (string Text, string Detail, string Color) Describe(SyncStatusSnapshot s) => s.State switch
    {
        SyncChipState.Syncing => ("Syncing…", "Sending and receiving changes", "#1565C0"),
        SyncChipState.Offline => (s.Pending > 0 ? $"Offline · {s.Pending} to sync" : "Offline",
            "Saved on this device · syncs when you're back online", "#616161"),
        SyncChipState.Waiting => (s.Pending > 0 ? $"{s.Pending} to sync" : "Waiting to sync", "Saved on this device", "#B26A00"),
        SyncChipState.Failed => ("Couldn't sync", "Saved on this device · we'll retry automatically", "#C62828"),
        SyncChipState.NeedsReview => ($"{s.NeedsAttention} need review", "Some changes need your choice · tap to review", "#C62828"),
        SyncChipState.SignInNeeded => ("Sign in to sync", "Your changes are kept on this device · tap to sign in", "#C62828"),
        SyncChipState.NoAccess => ("No access", "This account can't sync this business · saved on this device", "#C62828"),
        SyncChipState.Blocked => ("Sync stopped", "Update the app, then tap Sync now · saved on this device", "#C62828"),
        _ => ("Synced", s.LastSyncUtc is { } t ? $"Last synced {t.ToLocalTime():d MMM, HH:mm}" : "Up to date", "#2E7D32")
    };

    private void Apply(SyncStatusSnapshot s)
    {
        IsSyncing = s.State == SyncChipState.Syncing;
        NeedsSignIn = s.State == SyncChipState.SignInNeeded;
        var (text, detail, color) = Describe(s);
        ChipText = text;
        ChipDetail = detail;
        ChipColor = Color.FromArgb(color);
        LastSyncText = s.LastSyncUtc is { } last ? $"Last synced {last.ToLocalTime():d MMM yyyy, HH:mm}" : "Not synced yet";
        ConnectionText = _sync.IsOnline ? "Online" : "Offline";
        ErrorText = s.LastError;
        if (s.State != SyncChipState.Syncing)
        {
            _ = LoadItemsAsync();
        }
    }

    [RelayCommand]
    private Task OpenDetailsAsync() => Shell.Current.GoToAsync(nameof(Views.SyncDetailsPage));

    [RelayCommand]
    private Task SyncNowAsync() => _sync.SyncNowAsync();

    /// <summary>Session expired: sign in again without wiping (same account keeps its unsynced changes).</summary>
    [RelayCommand]
    private Task SignInAsync() => _navigation.ShowLoginAsync();

    private async Task ResolveAsync(SyncItemRow row, bool keepMine)
    {
        if (!await _sync.ResolveAsync(row.Id, keepMine))
        {
            ErrorText = "That customer is no longer on this device, so it can't be restored. Discard this change instead.";
        }

        await LoadItemsAsync();
    }

    /// <summary>Asks the user (title, message, accept, cancel). Replaceable in tests.</summary>
    public Func<string, string, string, string, Task<bool>> Confirm { get; set; } =
        (title, message, accept, cancel) => Shell.Current.DisplayAlert(title, message, accept, cancel);

    public static (string Title, string Message, string Accept) DiscardPrompt(DiscardImpact impact)
    {
        if (impact.Children > 0)
        {
            var parts = new List<string>();
            if (impact.Orders > 0)
            {
                parts.Add(impact.Orders == 1 ? "1 order" : $"{impact.Orders} orders");
            }

            if (impact.Measurements > 0)
            {
                parts.Add(impact.Measurements == 1 ? "1 measurement" : $"{impact.Measurements} measurements");
            }

            return ("Discard customer and their records?",
                $"This customer never synced. Discarding also deletes their {string.Join(" and ", parts)} from this device. This can't be undone.",
                "Discard all");
        }

        return impact.NeverSynced
            ? ("Discard this change?", "It never synced, so it will be deleted from this device. This can't be undone.", "Discard")
            : ("Discard this change?", "Your change will be dropped and the version saved in NeedleBase will be shown again.", "Discard");
    }

    private async Task DiscardAsync(SyncItemRow row)
    {
        var (title, message, accept) = DiscardPrompt(await _sync.GetDiscardImpactAsync(row.Id));
        if (!await Confirm(title, message, accept, "Cancel"))
        {
            return;
        }

        await _sync.DiscardAsync(row.Id);
        await LoadItemsAsync();
    }

    [RelayCommand]
    private async Task LoadItemsAsync()
    {
        try
        {
            var businessId = await _auth.GetBusinessIdAsync();
            if (string.IsNullOrWhiteSpace(businessId))
            {
                return;
            }

            var entries = Ordered(await _sync.Engine.ListAsync(businessId));
            MainThread.BeginInvokeOnMainThread(() =>
            {
                Items.Clear();
                foreach (var e in entries)
                {
                    var row = new SyncItemRow(e);
                    row.PrimaryCommand = new AsyncRelayCommand(() => ResolveAsync(row, keepMine: true));
                    row.SecondaryCommand = row.IsFailed
                        ? new AsyncRelayCommand(() => DiscardAsync(row))
                        : new AsyncRelayCommand(() => ResolveAsync(row, keepMine: false));
                    Items.Add(row);
                }

                _itemsLoaded = true;
                ListError = null;
                AttentionCount = Items.Count(r => r.IsConflict || r.IsFailed);
                OnPropertyChanged(nameof(HasItems));
                OnPropertyChanged(nameof(IsEmpty));
                OnPropertyChanged(nameof(WaitingHeading));
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Sync list failed: {ex.Message}");
            MainThread.BeginInvokeOnMainThread(() => ListError = "Couldn't read the changes waiting on this device. Try again.");
        }
    }
}

/// <summary>One waiting/failed/conflicting change, with the actions that make sense for it.</summary>
public sealed class SyncItemRow
{
    public SyncItemRow(SyncQueueEntry e)
    {
        Id = e.Id;
        Title = e.Label;
        var kind = e.EntityType switch
        {
            SyncEntityType.Customer => "Customer",
            SyncEntityType.Measurement => "Measurement",
            SyncEntityType.Order => "Order",
            SyncEntityType.Payment => "Payment",
            _ => "Item"
        };
        IsConflict = e.State == SyncQueueState.Conflict;
        IsFailed = e.State == SyncQueueState.Failed;

        (Subtitle, PrimaryText, SecondaryText) = (e.State, e.Code) switch
        {
            (SyncQueueState.Conflict, SyncErrorCodes.ParentDeleted) when e.EntityType == SyncEntityType.Payment =>
                ("Payment · its order was deleted on another device.", "Restore order", "Discard"),
            (SyncQueueState.Conflict, SyncErrorCodes.ParentDeleted) =>
                ($"{kind} · its customer was deleted on another device.", "Restore customer", "Discard"),
            (SyncQueueState.Conflict, SyncErrorCodes.DeleteRefused) when e.EntityType == SyncEntityType.Order =>
                ("Order has payments recorded, so it wasn't deleted.", "Restore order", null),
            (SyncQueueState.Conflict, SyncErrorCodes.DeleteRefused) =>
                ("Customer has orders on another device, so it wasn't deleted.", "Restore customer", null),
            (SyncQueueState.Conflict, SyncErrorCodes.PaymentMismatch) =>
                ("Payment · a different payment was saved under the same record. Keep both keeps them as two payments; Use theirs drops this phone's copy.",
                    "Keep both", "Use theirs"),
            (SyncQueueState.Conflict, _) when e.ServerCopyDeleted =>
                (e.EntityType == SyncEntityType.Customer && e.Problem is { Length: > 0 } p && p.Contains("still has", StringComparison.Ordinal)
                    ? "Customer deleted on another device, but it still has orders or changes here."
                    : $"{kind} deleted on another device.", "Restore", "Delete here"),
            (SyncQueueState.Conflict, _) =>
                ($"{kind} changed on another device. Choose which version to keep.", "Keep mine", "Keep theirs"),
            (SyncQueueState.Failed, _) => ($"{kind} · {e.Problem ?? "Couldn't be saved"}", null, "Discard"),
            _ when e.EntityType == SyncEntityType.Order && e.Action == "New" =>
                ("Order · new · waiting to sync. Its order number is confirmed when it syncs.", null, null),
            _ => ($"{kind} · {e.Action} · waiting to sync", null, null)
        };
    }

    public string Id { get; }
    public string Title { get; }
    public string Subtitle { get; }
    public bool IsConflict { get; }
    public bool IsFailed { get; }
    public string? PrimaryText { get; }
    public string? SecondaryText { get; }
    public bool HasPrimary => PrimaryText is not null;
    public bool HasSecondary => SecondaryText is not null;
    public IAsyncRelayCommand? PrimaryCommand { get; set; }
    public IAsyncRelayCommand? SecondaryCommand { get; set; }
}
