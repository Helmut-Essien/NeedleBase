using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NeedleBase.Application.Contracts.Payments;
using NeedleBase.Application.Payments;
using NeedleBase.Mobile.Data.Services;
using NeedleBase.Mobile.Services;

namespace NeedleBase.Mobile.ViewModels;

/// <summary>Receipt preview: business header, customer, order totals and this payment. Share as text; void (Owner/Manager).</summary>
public partial class PaymentReceiptViewModel : BaseViewModel, IQueryAttributable
{
    private readonly IPaymentLocalService _payments;
    private readonly IAuthService _auth;
    private bool _loadedOnce;

    public PaymentReceiptViewModel(IPaymentLocalService payments, IAuthService auth)
    {
        _payments = payments;
        _auth = auth;
        Title = "Receipt";
    }

    public string PaymentId { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReceipt))]
    [NotifyPropertyChangedFor(nameof(CanVoid))]
    private LocalPaymentReceipt? receipt;

    [ObservableProperty] private string businessName = string.Empty;
    [ObservableProperty] private string totalText = string.Empty;
    [ObservableProperty] private string paidText = string.Empty;
    [ObservableProperty] private string balanceText = string.Empty;
    [ObservableProperty] private string dateText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanVoid))]
    private bool canManage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? errorMessage;

    public bool HasReceipt => Receipt is not null;
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool CanVoid => CanManage && Receipt is { Payment.IsVoided: false };

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("paymentId", out var value) && value is not null)
        {
            PaymentId = Uri.UnescapeDataString(value.ToString() ?? string.Empty);
        }

        if (!_loadedOnce)
        {
            _loadedOnce = true;
            MainThread.BeginInvokeOnMainThread(async () => await LoadAsync());
        }
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        ErrorMessage = null;
        try
        {
            var businessId = await _auth.GetBusinessIdAsync();
            var user = await _auth.GetCachedUserAsync();
            if (string.IsNullOrWhiteSpace(businessId))
            {
                ErrorMessage = "Missing business session.";
                return;
            }

            CanManage = PaymentRules.CanManage(user?.Role);
            if (!CanManage)
            {
                ErrorMessage = "Payments are only shown to the owner and managers.";
                Receipt = null;
                return;
            }

            BusinessName = user?.BusinessName ?? string.Empty;
            var receipt = await _payments.GetReceiptAsync(businessId, PaymentId);
            if (receipt is null)
            {
                ErrorMessage = "This payment isn't available on this device.";
                Receipt = null;
                return;
            }

            Receipt = receipt;
            TotalText = Money.Format(receipt.Totals.TotalAmount);
            PaidText = Money.Format(receipt.Totals.PaidAmount);
            BalanceText = receipt.Totals.BalanceDisplay;
            DateText = Money.Date(receipt.Payment.Payment.RecordedAtUtc);
        }
        catch (Exception)
        {
            ErrorMessage = "Couldn't load this receipt.";
        }
    }

    [RelayCommand]
    private async Task ShareAsync()
    {
        if (Receipt is null)
        {
            return;
        }

        await Share.Default.RequestAsync(new ShareTextRequest
        {
            Title = "Payment receipt",
            Text = Receipt.ToShareText(BusinessName)
        });
    }

    [RelayCommand]
    private async Task VoidAsync()
    {
        if (IsBusy || !CanVoid || Shell.Current?.CurrentPage is not { } page)
        {
            return;
        }

        var reason = await page.DisplayPromptAsync(
            "Void this payment?",
            "It stays on record but no longer counts toward the order. This can't be undone.",
            accept: "Void payment",
            cancel: "Keep",
            placeholder: "Reason (optional)",
            maxLength: 200);
        if (reason is null)
        {
            return; // cancelled
        }

        IsBusy = true;
        try
        {
            var businessId = await _auth.GetBusinessIdAsync();
            var result = await _payments.VoidAsync(businessId ?? string.Empty, PaymentId, reason);
            if (!result.IsSuccess)
            {
                ErrorMessage = result.Message;
                return;
            }

            await Toast.Make("Payment voided on this device", ToastDuration.Short, 14).Show();
        }
        catch (Exception)
        {
            ErrorMessage = "Couldn't void the payment. Try again.";
        }
        finally
        {
            IsBusy = false;
        }

        await LoadAsync();
    }

    [RelayCommand]
    private async Task GoBackAsync() => await Shell.Current.GoToAsync("..");
}
