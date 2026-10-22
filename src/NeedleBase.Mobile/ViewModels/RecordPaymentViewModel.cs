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
public partial class RecordPaymentViewModel : BaseViewModel, IQueryAttributable, IBackAware
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

    /// <summary>Android Back is swallowed while the save runs (the payment is written once, then the page pops).</summary>
    public bool CanGoBack => !IsBusy;

    public bool TryHandleBack() => false;

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

    private static readonly string[] CurrencyTokens = ["GH₵", "GH¢", "GHS", "GHC", "₵", "¢"];

    private static readonly System.Text.RegularExpressions.Regex GhanaGrouped =
        new(@"^\d{1,3}(,\d{3})+(\.\d{1,2})?$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// Typed cedi amount, never rounded and never guessed:
    /// "300", "300.5", "GH₵ 75", "GH¢75" and the en-GH display form "1,200.00" / "1,000" are accepted;
    /// "10,50" is a decimal comma (GH₵ 10.50), as in <see cref="DecimalInput"/>; "1.000" is ambiguous and refused;
    /// a minus sign is refused. More than 2 decimals parses, then fails the amount rule (no silent rounding).
    /// </summary>
    public static bool TryParseAmount(string? text, out decimal amount)
    {
        amount = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var cleaned = text;
        foreach (var token in CurrencyTokens)
        {
            cleaned = cleaned.Replace(token, string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        cleaned = cleaned.Replace(" ", string.Empty).Replace("\u00A0", string.Empty).Trim();
        if (cleaned.Length == 0 || cleaned.Contains('-'))
        {
            return false;
        }

        if (GhanaGrouped.IsMatch(cleaned))
        {
            return decimal.TryParse(cleaned.Replace(",", string.Empty), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount);
        }

        return DecimalInput.TryParse(cleaned, out amount);
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
            AmountError = "Enter the amount received, e.g. 200 or 1,250.50.";
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
