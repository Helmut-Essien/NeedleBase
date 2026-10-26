using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NeedleBase.Application.Contracts.Measurements;
using NeedleBase.Domain.Enums;
using NeedleBase.Mobile.Data.Services;
using NeedleBase.Mobile.Services;

namespace NeedleBase.Mobile.ViewModels;

public partial class CustomerDetailViewModel : BaseViewModel, IQueryAttributable
{
    private readonly ICustomerLocalService _customers;
    private readonly IMeasurementLocalService _measurements;
    private readonly IAuthService _auth;

    private readonly WhatsAppConsentSync? _consent;
    private readonly IWhatsAppConsentApi? _consentApi;
    private int _loadGeneration;

    public CustomerDetailViewModel(
        ICustomerLocalService customers,
        IMeasurementLocalService measurements,
        IAuthService auth,
        WhatsAppConsentSync? consent = null,
        IWhatsAppConsentApi? consentApi = null)
    {
        _customers = customers;
        _measurements = measurements;
        _auth = auth;
        _consent = consent;
        _consentApi = consentApi;
        Title = "Customer";
    }

    public string CustomerId { get; private set; } = string.Empty;

    public ObservableCollection<MeasurementListItemDto> Measurements { get; } = new();

    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private string phone = string.Empty;

    [ObservableProperty]
    private string notes = string.Empty;

    [ObservableProperty]
    private string emptyMeasurements = "No measurements yet. Capture a dress or shirt template.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? errorMessage;

    [ObservableProperty]
    private MeasurementListItemDto? selectedMeasurement;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    // ---- WhatsApp consent (works offline; Owner/Manager change it, Tailors see it) ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConsentText), nameof(ConsentButtonText), nameof(HasConsent))]
    private WhatsAppConsentView? consent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConsentButtonText))]
    private bool canChangeConsent;

    /// <summary>UI hook (tests replace it).</summary>
    public Func<string, string, Task<bool>> Confirm { get; set; } =
        (title, message) => Shell.Current.DisplayAlert(title, message, "Turn on", "Cancel");

    /// <summary>Clock (tests replace it).</summary>
    public Func<DateTimeOffset> Now { get; set; } = () => DateTimeOffset.UtcNow;

    public bool HasConsent => Consent is not null && _consent is not null;

    public string ConsentText => DescribeConsent(Consent);

    public string ConsentButtonText => Consent?.OptedIn == true ? "Turn off WhatsApp updates" : "Turn on WhatsApp updates";

    public static string DescribeConsent(WhatsAppConsentView? c) => c switch
    {
        null => string.Empty,
        { RepliedStop: true } => "WhatsApp updates: off. The customer replied STOP.",
        { OptedIn: true, Pending: true } => "WhatsApp updates: on (saved on this phone, sends when you're online).",
        { OptedIn: false, Pending: true } => "WhatsApp updates: off (saved on this phone, sends when you're online).",
        { OptedIn: true } => "WhatsApp updates: on. The customer agreed to order updates.",
        _ => "WhatsApp updates: off. Ask the customer before turning them on."
    };

    [RelayCommand]
    private async Task ToggleConsentAsync()
    {
        if (_consent is null || Consent is null || !CanChangeConsent || string.IsNullOrWhiteSpace(CustomerId) || _consentBusy)
        {
            return;
        }

        _consentBusy = true; // a quick double tap must not ask twice or save twice
        try
        {
            await ToggleConsentCoreAsync();
        }
        finally
        {
            _consentBusy = false;
        }
    }

    private bool _consentBusy;

    private async Task ToggleConsentCoreAsync()
    {
        if (Consent is not { } current || _consent is not { } consent)
        {
            return;
        }

        var turnOn = !current.OptedIn;
        if (turnOn && !await Confirm("Turn on WhatsApp updates?",
                current.RepliedStop
                    ? "This customer replied STOP. Only turn updates back on if they asked you to."
                    : "Only turn this on if the customer agreed to get order updates on WhatsApp."))
        {
            return;
        }

        try
        {
            var businessId = await _auth.GetBusinessIdAsync();
            if (string.IsNullOrWhiteSpace(businessId))
            {
                return;
            }

            var customerId = CustomerId;
            Consent = await consent.SetAsync(businessId, customerId, turnOn, Now());
            if (_consentApi is not null)
            {
                // Best effort now; otherwise it goes with the next sync.
                await consent.PushAsync(businessId, _consentApi);
                var fresh = await consent.GetAsync(businessId, customerId);
                if (customerId == CustomerId)
                {
                    Consent = fresh;
                }
            }
        }
        catch (Exception)
        {
            ErrorMessage = "Couldn't save the WhatsApp choice. Try again.";
        }
    }

    private async Task LoadConsentAsync(string businessId, string customerId, int generation)
    {
        if (_consent is null)
        {
            return;
        }

        try
        {
            var role = (await _auth.GetCachedUserAsync())?.Role;
            CanChangeConsent = role is BusinessRole.Owner or BusinessRole.Manager;
            var local = await _consent.GetAsync(businessId, customerId);
            if (generation != _loadGeneration)
            {
                return;
            }

            Consent = local;
            if (_consentApi is null)
            {
                return;
            }

            // Refresh from the server in the background (STOP replies, other phones); offline keeps the local copy.
            var result = await _consentApi.GetConsentAsync(customerId);
            if (result.Outcome == ConsentPushOutcome.Saved && result.Value is { } dto && dto.CustomerId == customerId)
            {
                var view = await _consent.ApplyServerAsync(businessId, dto);
                if (generation == _loadGeneration)
                {
                    Consent = view;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Consent load failed: {ex.Message}");
        }
    }
    public bool HasNotes => !string.IsNullOrWhiteSpace(Notes);
    public bool HasMeasurements => Measurements.Count > 0;

    /// <summary>Show empty CTA only when idle (not while loading).</summary>
    public bool ShowEmptyMeasurements => !IsBusy && !HasMeasurements;

    protected override void OnBusyChanged() => OnPropertyChanged(nameof(ShowEmptyMeasurements));

    partial void OnSelectedMeasurementChanged(MeasurementListItemDto? value)
    {
        if (value is null)
        {
            return;
        }

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await OpenMeasurementAsync(value);
            SelectedMeasurement = null;
        });
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("customerId", out var value) && value is not null)
        {
            CustomerId = Uri.UnescapeDataString(value.ToString() ?? string.Empty);
        }

        MainThread.BeginInvokeOnMainThread(async () => await LoadAsync());
    }

    public async Task LoadAsync()
    {
        if (IsBusy || string.IsNullOrWhiteSpace(CustomerId))
        {
            return;
        }

        IsBusy = true;
        var generation = ++_loadGeneration;
        Consent = null;
        OnPropertyChanged(nameof(ShowEmptyMeasurements));
        ErrorMessage = null;
        // Clear before fetch so a reused page never keeps prior customer PII.
        ClearCustomerFields();
        try
        {
            var businessId = await _auth.GetBusinessIdAsync();
            if (string.IsNullOrWhiteSpace(businessId))
            {
                ErrorMessage = "Missing business session.";
                return;
            }

            var detail = await _customers.GetByIdAsync(businessId, CustomerId);
            if (detail is null)
            {
                ErrorMessage = "Customer not found on this device.";
                return;
            }

            Name = detail.Name;
            Phone = detail.Phone;
            Notes = detail.Notes ?? string.Empty;
            Title = detail.Name;
            OnPropertyChanged(nameof(HasNotes));

            var list = await _measurements.ListForCustomerAsync(businessId, CustomerId);
            Measurements.Clear();
            foreach (var item in list)
            {
                Measurements.Add(item);
            }

            OnPropertyChanged(nameof(HasMeasurements));
            _ = LoadConsentAsync(businessId, CustomerId, generation);
        }
        catch (Exception)
        {
            ClearCustomerFields();
            ErrorMessage = "Couldn't load this customer. Try again.";
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(ShowEmptyMeasurements));
        }
    }

    private void ClearCustomerFields()
    {
        Name = string.Empty;
        Phone = string.Empty;
        Notes = string.Empty;
        Title = "Customer";
        Measurements.Clear();
        OnPropertyChanged(nameof(HasNotes));
        OnPropertyChanged(nameof(HasMeasurements));
    }

    [RelayCommand]
    private async Task GoBackAsync() => await Shell.Current.GoToAsync("..");

    [RelayCommand]
    private async Task EditAsync() =>
        await Shell.Current.GoToAsync($"CustomerEditPage?customerId={Uri.EscapeDataString(CustomerId)}");

    [RelayCommand]
    private async Task AddMeasurementAsync() =>
        await Shell.Current.GoToAsync(
            $"MeasurementEditPage?customerId={Uri.EscapeDataString(CustomerId)}&template={GarmentTemplate.Dress}");

    [RelayCommand]
    private async Task CreateOrderAsync() =>
        await Shell.Current.GoToAsync(
            $"{nameof(Views.CreateOrderPage)}?customerId={Uri.EscapeDataString(CustomerId)}");

    [RelayCommand]
    private async Task OpenMeasurementAsync(MeasurementListItemDto? item)
    {
        if (item is null)
        {
            return;
        }

        await Shell.Current.GoToAsync(
            $"MeasurementEditPage?customerId={Uri.EscapeDataString(CustomerId)}&measurementId={Uri.EscapeDataString(item.Id)}");
    }
}
