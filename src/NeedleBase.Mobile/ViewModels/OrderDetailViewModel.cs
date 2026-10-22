using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MauiIcons.Material;
using NeedleBase.Application.Contracts.Orders;
using NeedleBase.Domain.Enums;
using NeedleBase.Mobile.Data.Services;
using NeedleBase.Mobile.Helpers;
using NeedleBase.Mobile.Services;

namespace NeedleBase.Mobile.ViewModels;

public sealed class OrderStatusOption
{
    public OrderStatusOption(string status)
    {
        Status = status;
        Display = Helpers.OrderStatusPresentation.DisplayName(status);
    }

    public string Status { get; }
    public string Display { get; }
}

public partial class OrderDetailViewModel : BaseViewModel, IQueryAttributable
{
    private readonly IOrderLocalService _orders;
    private readonly IAuthService _auth;
    private readonly IPaymentLocalService? _payments;
    private bool _loadedOnce;

    public OrderDetailViewModel(IOrderLocalService orders, IAuthService auth, IPaymentLocalService? payments = null)
    {
        _orders = orders;
        _auth = auth;
        _payments = payments;
        Title = "Order";
        foreach (var status in OrderStatus.All)
        {
            StatusOptions.Add(new OrderStatusOption(status));
        }
    }

    public string OrderId { get; private set; } = string.Empty;

    public ObservableCollection<OrderLineDto> Lines { get; } = new();
    public ObservableCollection<OrderStatusOption> StatusOptions { get; } = new();
    public ObservableCollection<LocalPayment> Payments { get; } = new();

    // Payments (Slice 5): owner/manager only; a tailor never sees amounts paid.
    [ObservableProperty] private bool showPayments;
    [ObservableProperty] private bool canRecordPayment;
    [ObservableProperty] private string paidSummary = string.Empty;
    [ObservableProperty] private string balanceText = string.Empty;
    [ObservableProperty] private bool hasPendingPayments;
    [ObservableProperty] private LocalPayment? selectedPayment;

    public bool HasPayments => Payments.Count > 0;

    /// <summary>Reloads the money section (after Record payment / a void, or a sync). Never throws.</summary>
    public async Task RefreshPaymentsAsync()
    {
        if (_payments is null || string.IsNullOrWhiteSpace(OrderId))
        {
            return;
        }

        try
        {
            var businessId = await _auth.GetBusinessIdAsync();
            var user = await _auth.GetCachedUserAsync();
            ShowPayments = NeedleBase.Application.Payments.PaymentRules.CanManage(user?.Role) && !string.IsNullOrWhiteSpace(businessId);
            if (!ShowPayments)
            {
                Payments.Clear();
                CanRecordPayment = false;
                OnPropertyChanged(nameof(HasPayments));
                return;
            }

            var money = await _payments.GetForOrderAsync(businessId!, OrderId);
            if (money is null)
            {
                ShowPayments = false;
                return;
            }

            PaidSummary = money.Summary.PaidSummary;
            BalanceText = money.Summary.BalanceDisplay;
            CanRecordPayment = money.CanRecord;
            HasPendingPayments = money.HasPending;
            CollectionSync.Apply(Payments, money.Payments, (a, b) => a == b, p => p);
            OnPropertyChanged(nameof(HasPayments));
        }
        catch (Exception)
        {
            // Money section is secondary: keep the order visible.
        }
    }

    [RelayCommand]
    private async Task RecordPaymentAsync()
    {
        if (!CanRecordPayment || string.IsNullOrWhiteSpace(OrderId))
        {
            return;
        }

        await Shell.Current.GoToAsync($"RecordPaymentPage?orderId={Uri.EscapeDataString(OrderId)}");
    }

    partial void OnSelectedPaymentChanged(LocalPayment? value)
    {
        if (value is null)
        {
            return;
        }

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await Shell.Current.GoToAsync($"PaymentReceiptPage?paymentId={Uri.EscapeDataString(value.Id)}");
            SelectedPayment = null;
        });
    }

    /// <summary>Page shown again (back from Record payment / receipt): refresh the money section only.</summary>
    public Task AppearAsync() => _loadedOnce ? RefreshPaymentsAsync() : Task.CompletedTask;

    [ObservableProperty] private string displayRef = string.Empty;
    [ObservableProperty] private string customerName = string.Empty;
    [ObservableProperty] private string customerPhone = string.Empty;
    [ObservableProperty] private string statusDisplay = string.Empty;
    [ObservableProperty] private string status = string.Empty;
    [ObservableProperty] private Color statusBackground = Colors.LightGray;
    [ObservableProperty] private Color statusForeground = Colors.Gray;
    [ObservableProperty] private ImageSource? statusIconImage;
    [ObservableProperty] private ImageSource? overdueIconImage;
    [ObservableProperty] private string dueDateText = "No due date";
    [ObservableProperty] private string totalText = string.Empty;
    [ObservableProperty] private string notes = string.Empty;
    [ObservableProperty] private bool hasNotes;
    [ObservableProperty] private bool isOverdue;
    [ObservableProperty] private bool showStatusPicker;
    [ObservableProperty] private OrderStatusOption? selectedStatusOption;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? errorMessage;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool HasLines => Lines.Count > 0;
    public bool CanUpdateStatus =>
        !string.IsNullOrWhiteSpace(OrderId) &&
        !string.IsNullOrWhiteSpace(Status) &&
        !OrderStatus.IsTerminal(Status);

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

    /// <summary>Tests: set the order without the Shell navigation load.</summary>
    public void ApplyQueryAttributesForTest(string orderId)
    {
        OrderId = orderId;
        _loadedOnce = true;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsBusy || string.IsNullOrWhiteSpace(OrderId))
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var businessId = await _auth.GetBusinessIdAsync();
            if (string.IsNullOrWhiteSpace(businessId))
            {
                ErrorMessage = "Missing business session.";
                return;
            }

            var detail = await _orders.GetByIdAsync(businessId, OrderId);
            if (detail is null)
            {
                ErrorMessage = "This order isn't available on this device.";
                ClearFields();
                return;
            }

            ApplyDetail(detail);
            await RefreshPaymentsAsync();
        }
        catch (Exception)
        {
            ErrorMessage = "Couldn't load this order.";
            ClearFields();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyDetail(OrderDetailDto detail)
    {
        DisplayRef = $"#{detail.DisplayRef}";
        Title = DisplayRef;
        CustomerName = detail.CustomerName;
        CustomerPhone = detail.CustomerPhone;
        Status = detail.Status;
        StatusDisplay = detail.StatusDisplay;
        StatusBackground = OrderStatusPresentation.Background(detail.Status);
        StatusForeground = OrderStatusPresentation.Foreground(detail.Status);
        StatusIconImage = OrderStatusPresentation.IconImage(detail.Status, 16);
        OverdueIconImage = OrderStatusPresentation.OverdueIconImage(16);
        DueDateText = detail.DueDate is { } due
            ? due.ToString("dd MMM yyyy", CultureInfo.GetCultureInfo("en-GH"))
            : "No due date";
        TotalText = $"GH₵ {detail.TotalAmount.ToString("N2", CultureInfo.GetCultureInfo("en-GH"))}";
        Notes = detail.Notes ?? string.Empty;
        HasNotes = !string.IsNullOrWhiteSpace(Notes);
        IsOverdue = detail.IsOverdue;
        SelectedStatusOption = StatusOptions.FirstOrDefault(s => s.Status == detail.Status);
        ShowStatusPicker = false;

        Lines.Clear();
        foreach (var line in detail.Lines)
        {
            Lines.Add(line);
        }

        OnPropertyChanged(nameof(HasLines));
        OnPropertyChanged(nameof(CanUpdateStatus));
    }

    private void ClearFields()
    {
        DisplayRef = string.Empty;
        CustomerName = string.Empty;
        CustomerPhone = string.Empty;
        StatusDisplay = string.Empty;
        Status = string.Empty;
        StatusBackground = Colors.LightGray;
        StatusForeground = Colors.Gray;
        StatusIconImage = null;
        OverdueIconImage = null;
        DueDateText = "No due date";
        TotalText = string.Empty;
        Notes = string.Empty;
        HasNotes = false;
        IsOverdue = false;
        Lines.Clear();
        OnPropertyChanged(nameof(HasLines));
        OnPropertyChanged(nameof(CanUpdateStatus));
    }

    [RelayCommand]
    private void ToggleStatusPicker()
    {
        if (IsBusy || !CanUpdateStatus)
        {
            return;
        }

        ShowStatusPicker = !ShowStatusPicker;
    }

    [RelayCommand]
    private async Task ApplyStatusAsync()
    {
        if (IsBusy || SelectedStatusOption is null)
        {
            return;
        }

        if (string.Equals(SelectedStatusOption.Status, Status, StringComparison.Ordinal))
        {
            ShowStatusPicker = false;
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var businessId = await _auth.GetBusinessIdAsync();
            if (string.IsNullOrWhiteSpace(businessId))
            {
                ErrorMessage = "Missing business session.";
                return;
            }

            var result = await _orders.UpdateStatusAsync(
                businessId,
                OrderId,
                SelectedStatusOption.Status);
            if (!result.IsSuccess)
            {
                ErrorMessage = result.Message ?? "Couldn't update status.";
                return;
            }

            ApplyDetail(result.Value!);
            await RefreshPaymentsAsync(); // cancelled orders stop taking payments
            ShowStatusPicker = false;
            await Toast.Make("Saved on this device", ToastDuration.Short, 14).Show();
        }
        catch (Exception)
        {
            ErrorMessage = "Couldn't update status. Try again.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task GoBackAsync() => await Shell.Current.GoToAsync("..");
}
