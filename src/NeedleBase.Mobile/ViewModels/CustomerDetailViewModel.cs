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

    public CustomerDetailViewModel(
        ICustomerLocalService customers,
        IMeasurementLocalService measurements,
        IAuthService auth)
    {
        _customers = customers;
        _measurements = measurements;
        _auth = auth;
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
    private async Task EditAsync() =>
        await Shell.Current.GoToAsync($"CustomerEditPage?customerId={Uri.EscapeDataString(CustomerId)}");

    [RelayCommand]
    private async Task AddMeasurementAsync() =>
        await Shell.Current.GoToAsync(
            $"MeasurementEditPage?customerId={Uri.EscapeDataString(CustomerId)}&template={GarmentTemplate.Dress}");

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
