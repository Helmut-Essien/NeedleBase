using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NeedleBase.Application.Contracts.Customers;
using NeedleBase.Domain.Common;
using NeedleBase.Mobile.Data.Services;
using NeedleBase.Mobile.Services;

namespace NeedleBase.Mobile.ViewModels;

public partial class CustomerEditViewModel : BaseViewModel, IQueryAttributable
{
    private const string SavedCopy = "Saved on this device. Will sync when you're online.";
    private readonly ICustomerLocalService _customers;
    private readonly IAuthService _auth;
    private bool _initialized;

    public CustomerEditViewModel(ICustomerLocalService customers, IAuthService auth)
    {
        _customers = customers;
        _auth = auth;
        Title = "Add customer";
    }

    public string CustomerId { get; private set; } = string.Empty;

    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private string phone = string.Empty;

    [ObservableProperty]
    private string notes = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoBack))]
    private bool isSaving;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool CanGoBack => !IsSaving;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("customerId", out var value) && value is not null)
        {
            CustomerId = Uri.UnescapeDataString(value.ToString() ?? string.Empty);
        }

        // Prefer this path so edit customerId is applied before _initialized latches.
        MainThread.BeginInvokeOnMainThread(async () => await InitializeAsync());
    }

    /// <summary>
    /// OnAppearing fallback for add (no query). Yields so ApplyQueryAttributes can win for edit.
    /// </summary>
    public async Task InitializeIfPendingAsync()
    {
        await Task.Yield();
        if (_initialized)
        {
            return;
        }

        await InitializeAsync();
    }

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        if (string.IsNullOrWhiteSpace(CustomerId))
        {
            Title = "Add customer";
            return;
        }

        Title = "Edit customer";
        IsBusy = true;
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
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy)
        {
            return;
        }

        ErrorMessage = null;

        if (string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(Phone))
        {
            ErrorMessage = "Name and phone are required.";
            return;
        }

        if (Name.Trim().Length > FieldLimits.CustomerName || Phone.Trim().Length > FieldLimits.Phone)
        {
            ErrorMessage = "Name or phone is too long.";
            return;
        }

        if (Notes.Length > FieldLimits.Notes)
        {
            ErrorMessage = $"Notes must be at most {FieldLimits.Notes} characters.";
            return;
        }

        IsBusy = true;
        IsSaving = true;
        try
        {
            var businessId = await _auth.GetBusinessIdAsync();
            if (string.IsNullOrWhiteSpace(businessId))
            {
                ErrorMessage = "Missing business session.";
                return;
            }

            if (string.IsNullOrWhiteSpace(CustomerId))
            {
                var (created, error) = await _customers.SaveAsync(
                    businessId,
                    new CreateCustomerRequest(Name, Phone, Notes, null));
                if (error is not null || created is null)
                {
                    ErrorMessage = error ?? "Could not save customer.";
                    return;
                }

                await Shell.Current.DisplayAlert("Saved", SavedCopy, "OK");
                await Shell.Current.GoToAsync("..");
                return;
            }

            var (updated, updateError) = await _customers.UpdateAsync(
                businessId,
                CustomerId,
                new UpdateCustomerRequest(Name, Phone, Notes, null));
            if (updateError is not null || updated is null)
            {
                ErrorMessage = updateError ?? "Could not update customer.";
                return;
            }

            await Shell.Current.DisplayAlert("Saved", SavedCopy, "OK");
            await Shell.Current.GoToAsync("..");
        }
        finally
        {
            IsBusy = false;
            IsSaving = false;
        }
    }

    [RelayCommand]
    private async Task GoBackAsync()
    {
        if (IsSaving)
        {
            return;
        }

        await Shell.Current.GoToAsync("..");
    }
}
