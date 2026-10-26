using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NeedleBase.Application.Contracts.WhatsApp;
using NeedleBase.Domain.Enums;
using NeedleBase.Mobile.Services;

namespace NeedleBase.Mobile.ViewModels;

/// <summary>One order status that can send an automatic message.</summary>
public partial class WhatsAppStatusChoice : ObservableObject
{
    public WhatsAppStatusChoice(string status, bool isChecked, bool available)
    {
        Status = status;
        this.isChecked = isChecked;
        Available = available;
    }

    public string Status { get; }
    public bool Available { get; }
    public string Label => Available ? $"When an order is {Status}" : $"When an order is {Status} (no message set up on the server)";

    [ObservableProperty] private bool isChecked;
}

/// <summary>One automatic message in the log (phone already masked by the server).</summary>
public sealed class WhatsAppMessageRow
{
    private static readonly CultureInfo Gh = CultureInfo.GetCultureInfo("en-GH");

    public WhatsAppMessageRow(WhatsAppMessageDto m)
    {
        Title = $"{m.OrderRef ?? "Order"} · {m.OrderStatus}" + (string.IsNullOrWhiteSpace(m.CustomerName) ? string.Empty : $" · {m.CustomerName}");
        Detail = $"{m.State} · {m.UpdatedAtUtc.ToLocalTime().ToString("d MMM, HH:mm", Gh)}" + (m.MaskedPhone is { Length: > 0 } p ? $" · {p}" : string.Empty);
        Error = string.IsNullOrWhiteSpace(m.Error) ? null : m.Error;
    }

    public string Title { get; }
    public string Detail { get; }
    public string? Error { get; }
    public bool HasError => Error is not null;
}

/// <summary>
/// WhatsApp settings (online only): automatic messages on/off, which statuses send, and the message log.
/// Owner/Manager change it; anyone else sees it read-only.
/// </summary>
public partial class WhatsAppSettingsViewModel : BaseViewModel
{
    private readonly IWhatsAppAdminApi _api;
    private readonly IAuthService _auth;
    private int _page;

    public WhatsAppSettingsViewModel(IWhatsAppAdminApi api, IAuthService auth)
    {
        _api = api;
        _auth = auth;
        Title = "WhatsApp";
    }

    public ObservableCollection<WhatsAppStatusChoice> Statuses { get; } = new();
    public ObservableCollection<WhatsAppMessageRow> Messages { get; } = new();

    [ObservableProperty][NotifyPropertyChangedFor(nameof(HasError))] private string? errorMessage;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(HasStatus))] private string? statusMessage;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(ShowSettings), nameof(ShowEmptyLog), nameof(CanSave))] private bool loaded;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(CanSave), nameof(NotConfigured))] private bool configured;
    [ObservableProperty] private bool enabled;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(CanSave), nameof(ReadOnly))] private bool canEdit;
    [ObservableProperty] private bool hasMore;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(ShowEmptyLog))] private bool hasMessages;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusMessage);
    public bool ShowSettings => Loaded;
    public bool NotConfigured => Loaded && !Configured;
    public bool ReadOnly => Loaded && !CanEdit;
    public bool CanSave => Loaded && Configured && CanEdit && !IsBusy;
    public bool ShowEmptyLog => Loaded && !HasMessages;

    protected override void OnBusyChanged() => OnPropertyChanged(nameof(CanSave));

    public async Task LoadAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = null;
        try
        {
            var role = (await _auth.GetCachedUserAsync())?.Role;
            CanEdit = role is BusinessRole.Owner or BusinessRole.Manager;
            var settings = await _api.GetSettingsAsync();
            if (!settings.IsSuccess || settings.Value is null)
            {
                ErrorMessage = settings.Error ?? "Couldn't load WhatsApp settings. Try again.";
                return;
            }

            Apply(settings.Value);
            _page = 0;
            Messages.Clear();
            await LoadPageAsync();
            Loaded = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Apply(WhatsAppSettingsDto s)
    {
        Configured = s.Configured;
        Enabled = s.Enabled;
        Statuses.Clear();
        var available = new HashSet<string>(s.AvailableStatuses, StringComparer.Ordinal);
        foreach (var status in s.AvailableStatuses.Concat(s.Statuses.Where(x => !available.Contains(x))).Distinct(StringComparer.Ordinal))
        {
            Statuses.Add(new WhatsAppStatusChoice(status, s.Statuses.Contains(status), available.Contains(status)));
        }
    }

    private async Task LoadPageAsync()
    {
        var next = _page + 1;
        var page = await _api.GetMessagesAsync(next);
        if (!page.IsSuccess || page.Value is null)
        {
            ErrorMessage = page.Error ?? "Couldn't load the message log. Try again.";
            return;
        }

        foreach (var m in page.Value.Items)
        {
            Messages.Add(new WhatsAppMessageRow(m));
        }

        _page = next;
        HasMore = page.Value.HasMore;
        HasMessages = Messages.Count > 0;
    }

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (IsBusy || !HasMore)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await LoadPageAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task RetryAsync() => LoadAsync();

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!CanSave)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = null;
        try
        {
            var chosen = Statuses.Where(s => s.IsChecked && s.Available).Select(s => s.Status).ToList();
            if (Enabled && chosen.Count == 0)
            {
                ErrorMessage = "Choose at least one status, or turn automatic messages off.";
                return;
            }

            var result = await _api.SaveSettingsAsync(Enabled, chosen);
            if (!result.IsSuccess || result.Value is null)
            {
                ErrorMessage = result.Error ?? "Couldn't save. Try again.";
                return;
            }

            Apply(result.Value);
            StatusMessage = "Saved.";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
