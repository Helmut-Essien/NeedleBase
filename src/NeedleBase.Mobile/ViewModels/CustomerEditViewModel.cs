using NeedleBase.Application.Common;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NeedleBase.Application.Contracts.Customers;
using NeedleBase.Domain.Common;
using NeedleBase.Mobile.Data.Services;
using NeedleBase.Mobile.Services;

namespace NeedleBase.Mobile.ViewModels;

public partial class CustomerEditViewModel : BaseViewModel, IBackAware, IQueryAttributable
{
    private bool _returnSelects;
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
    [NotifyPropertyChangedFor(nameof(HasNameError))]
    private string? nameError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPhoneError))]
    private string? phoneError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoBack))]
    private bool isSaving;

    public bool HasNameError => !string.IsNullOrWhiteSpace(NameError);
    public bool HasPhoneError => !string.IsNullOrWhiteSpace(PhoneError);
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    partial void OnNameChanged(string value) => NameError = null;

    partial void OnPhoneChanged(string value) => PhoneError = null;
    public bool CanGoBack => !IsSaving;

    public bool TryHandleBack() => false;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("customerId", out var value) && value is not null)
        {
            CustomerId = Uri.UnescapeDataString(value.ToString() ?? string.Empty);
        }

        // Opened from the New order picker's "Add new customer": prefill what was typed.
        if (query.TryGetValue("name", out var prefillName) && prefillName is string typedName)
        {
            Name = typedName;
        }

        if (query.TryGetValue("phone", out var prefillPhone) && prefillPhone is string typedPhone)
        {
            Phone = typedPhone;
        }

        _returnSelects = query.TryGetValue("returnSelects", out var returns) &&
            string.Equals(returns?.ToString(), "true", StringComparison.OrdinalIgnoreCase);

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
            _loadedPhone = detail.Phone;
            Notes = detail.Notes ?? string.Empty;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private string? _loadedPhone;

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy)
        {
            return;
        }

        ErrorMessage = null;

        NameError = string.IsNullOrWhiteSpace(Name) ? "Name is required." : null;
        PhoneError = string.IsNullOrWhiteSpace(Phone) ? "Phone is required." : null;
        if (HasNameError || HasPhoneError)
        {
            return;
        }

        if (Name.Trim().Length > FieldLimits.CustomerName)
        {
            NameError = "Name is too long.";
        }

        if (Phone.Trim().Length > FieldLimits.Phone)
        {
            PhoneError = "Phone is too long.";
        }

        // Format rule only for a new or changed number, so an old customer's notes stay editable.
        if (!HasPhoneError && (_loadedPhone is null || !PhoneNumbers.Same(Phone, _loadedPhone)))
        {
            PhoneError = PhoneNumbers.Check(Phone);
        }

        if (HasNameError || HasPhoneError)
        {
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
                // Non-blocking duplicate warning: same number already saved on this device.
                var matches = await _customers.FindByPhoneAsync(businessId, Phone, null);
                if (matches.Count > 0)
                {
                    var names = string.Join(", ", matches.Select(m => m.Name));
                    var keep = await Shell.Current.DisplayAlert(
                        "Same phone number",
                        $"{names} already has this number. Save another customer anyway?",
                        "Save anyway",
                        "Cancel");
                    if (!keep)
                    {
                        return;
                    }
                }

                var result = await _customers.SaveAsync(
                    businessId,
                    new CreateCustomerRequest(Name, Phone, Notes, null));
                if (result.Error == RecordError.PhoneInvalid)
                {
                    PhoneError = result.Message;
                    return;
                }

                if (!result.IsSuccess)
                {
                    ErrorMessage = result.Message ?? "Could not save customer.";
                    return;
                }

                var created = result.Value!;

                if (_returnSelects)
                {
                    // Back to New order with the new customer selected (no extra alert tap).
                    await Shell.Current.GoToAsync(
                        "..",
                        new Dictionary<string, object> { ["createdCustomerId"] = created.Id });
                    return;
                }

                await Shell.Current.DisplayAlert("Saved", SavedCopy, "OK");
                await Shell.Current.GoToAsync("..");
                return;
            }

            var updated = await _customers.UpdateAsync(
                businessId,
                CustomerId,
                new UpdateCustomerRequest(Name, Phone, Notes, null));
            if (updated.Error == RecordError.PhoneInvalid)
            {
                PhoneError = updated.Message;
                return;
            }

            if (!updated.IsSuccess)
            {
                ErrorMessage = updated.Message ?? "Could not update customer.";
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
