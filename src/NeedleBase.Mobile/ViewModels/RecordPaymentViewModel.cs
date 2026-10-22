using System.Globalization;
using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Errors;
using NeedleBase.Application.Contracts.Payments;
using NeedleBase.Application.Payments;
using NeedleBase.Domain.Entities;
using NeedleBase.Domain.Enums;
using NeedleBase.Mobile.Data.Services;
using NeedleBase.Mobile.Services;

namespace NeedleBase.Mobile.ViewModels;

/// <summary>
/// Record a deposit or instalment against one order (Owner/Manager). Saved on this device first; the sync queue
/// sends it. One payment id per screen, so a double tap or retry never records twice.
/// </summary>
public partial class RecordPaymentViewModel : BaseViewModel, IQueryAttributable
{
    private readonly IPaymentLocalService _payments;
    private readonly IAuthService _auth;
    private readonly string _paymentId = IdGenerator.NewId();
    private bool _loadedOnce;

    public RecordPaymentViewModel(IPaymentLocalService payments, IAuthService auth)
    {
        _payments = payments;
        _auth = auth;
        Title = "Record payment";
    }

    public string OrderId { get; private set; } = string.Empty;

    public IReadOnlyList<string> Methods { get; } = PaymentMethod.All;

    [ObservableProperty] private string amountText = string.Empty;
    [ObservableProperty] private string method = PaymentMethod.Cash;
    [ObservableProperty] private string note = string.Empty;
    [ObservableProperty] private string paidSummary = string.Empty;
    [ObservableProperty] private string balanceText = string.Empty;
    [ObservableProperty] private decimal balance;
    [ObservableProperty] private bool canRecord;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAmountError))]
    private string? amountError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? errorMessage;

    public bool HasAmountError => !string.IsNullOrWhiteSpace(AmountError);
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    /// <summary>Set after a successful save (tests; the page has navigated back by then).</summary>
    public PaymentDto? Saved { get; private set; }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("orderId", out var value) && value is not null)
        {
            OrderId = Uri.UnescapeDataString(value.ToString() ?? string.Empty);
        }

        if (!_loadedOnce)
        {
            _loadedOnce = true;
            MainThread.BeginInvokeOnMainThread(async () => await LoadAsync());
        }
    }

    partial void OnAmountTextChanged(string value) => AmountError = null;

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

            if (!PaymentRules.CanManage(user?.Role))
            {
                CanRecord = false;
                ErrorMessage = RecordErrorMessages.For(RecordError.PaymentForbidden);
                return;
            }

            var order = await _payments.GetForOrderAsync(businessId, OrderId);
            if (order is null)
            {
                CanRecord = false;
                ErrorMessage = "This order isn't available on this device.";
                return;
            }

            Balance = order.Summary.Balance;
            PaidSummary = order.Summary.PaidSummary;
            BalanceText = order.Summary.BalanceDisplay;
            CanRecord = order.CanRecord;
            if (!order.CanRecord)
            {
                ErrorMessage = order.Summary.OrderStatus == OrderStatus.Cancelled
                    ? RecordErrorMessages.For(RecordError.PaymentOrderLocked)
                    : "This order is already paid in full.";
            }
        }
        catch (Exception)
        {
            ErrorMessage = "Couldn't load this order.";
        }
    }

    [RelayCommand]
    private void SelectMethod(string? value)
    {
        if (PaymentMethod.IsValid(value))
        {
            Method = value!;
        }
    }

    /// <summary>"Pay balance": fills the amount with what's left.</summary>
    [RelayCommand]
    private void FillBalance()
    {
        if (Balance > 0)
        {
            AmountText = Balance.ToString("0.00", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Accepts "300", "300.5", "1,200.00", "GH₵ 300"; never rounds (more than 2 decimals is an error).</summary>
    public static bool TryParseAmount(string? text, out decimal amount)
    {
        amount = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var cleaned = text.Replace("GH₵", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("GHS", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(",", string.Empty).Replace(" ", string.Empty).Trim();
        return decimal.TryParse(cleaned, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy)
        {
            return;
        }

        ErrorMessage = null;
        if (!TryParseAmount(AmountText, out var amount))
        {
            AmountError = "Enter the amount received, e.g. 200.";
            return;
        }

        if (Payment.CheckAmount(amount) is { } amountProblem)
        {
            AmountError = amountProblem;
            return;
        }

        if (amount > Balance)
        {
            AmountError = Balance > 0
                ? $"Only {Money.Format(Balance)} is left to pay on this order."
                : "This order is already paid in full.";
            return;
        }

        IsBusy = true;
        try
        {
            var businessId = await _auth.GetBusinessIdAsync();
            if (string.IsNullOrWhiteSpace(businessId))
            {
                ErrorMessage = "Missing business session.";
                return;
            }

            var result = await _payments.RecordAsync(businessId, OrderId, new RecordPaymentRequest
            {
                Id = _paymentId,
                Amount = amount,
                Method = Method,
                Note = Note
            });
            if (!result.IsSuccess)
            {
                if (result.Error == RecordError.PaymentExceedsBalance)
                {
                    AmountError = result.Message;
                }
                else
                {
                    ErrorMessage = result.Message;
                }

                return;
            }

            Saved = result.Value;
            await ShowToastAsync($"{Method} payment recorded on this device");
            await NavigateBackAsync();
        }
        catch (Exception)
        {
            ErrorMessage = "Couldn't save the payment. Try again.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task GoBackAsync() => IsBusy ? Task.CompletedTask : NavigateBackAsync();

    private static async Task ShowToastAsync(string text)
    {
        if (Shell.Current is null)
        {
            return; // unit tests
        }

        await Toast.Make(text, ToastDuration.Short, 14).Show();
    }

    private static Task NavigateBackAsync() => Shell.Current is null ? Task.CompletedTask : Shell.Current.GoToAsync("..");
}
