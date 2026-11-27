using System.Collections.ObjectModel;
using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Expenses;
using NeedleBase.Application.Contracts.Orders;
using NeedleBase.Application.Expenses;
using NeedleBase.Domain.Common;
using NeedleBase.Domain.Entities;
using NeedleBase.Domain.Enums;
using NeedleBase.Mobile.Data.Services;
using NeedleBase.Mobile.Data.Sync;
using NeedleBase.Mobile.Services;

namespace NeedleBase.Mobile.ViewModels;

/// <summary>
/// Record expense (expenses plan section 6.3). Saved on this phone first; sync sends it while the plan includes
/// expenses. One expense id per screen, so a double tap or retry never records twice. Same rules as the server.
/// </summary>
public partial class RecordExpenseViewModel : BaseViewModel, IQueryAttributable, IBackAware
{
    public const string LeaveTitle = "Discard this expense?";
    public const string LeaveMessage = "You haven't saved it. It won't be recorded.";
    public const string SavedOffline = "Saved on this device";
    public const string SavedSynced = "Expense recorded";

    private readonly IExpenseLocalService _expenses;
    private readonly IAuthService _auth;
    private readonly IOrderLocalService? _orders;
    private readonly string _expenseId = IdGenerator.NewId();
    private string? _initialOrderId;
    private bool _leaving;
    private bool _loadedOnce;

    public RecordExpenseViewModel(IExpenseLocalService expenses, IAuthService auth, IOrderLocalService? orders = null)
    {
        _expenses = expenses;
        _auth = auth;
        _orders = orders;
        Title = "Record expense";
        foreach (var code in ExpenseCategory.All)
        {
            Categories.Add(new CategoryChip(code, ExpenseCategory.DisplayName(code)));
        }

        SpentOn = Today.ToDateTime(TimeOnly.MinValue);
    }

    /// <summary>Clock for tests.</summary>
    public Func<DateTimeOffset> UtcNow { get; set; } = () => DateTimeOffset.UtcNow;

    public Func<bool> IsOnline { get; set; } = ExpensesViewModel.DefaultIsOnline;

    /// <summary>Sync right after saving when online (the app wires <c>SyncService.SyncForRefreshAsync</c>).</summary>
    public Func<Task>? SyncNow { get; set; }

    /// <summary>Yes/no prompt (title, message, accept, cancel). Tests answer it.</summary>
    public Func<string, string, string, string, Task<bool>> Confirm { get; set; } = (title, message, accept, cancel) =>
        Shell.Current is null ? Task.FromResult(true) : Shell.Current.DisplayAlert(title, message, accept, cancel);

    public Func<string, Task> ShowToast { get; set; } = async text =>
    {
        if (Shell.Current is not null)
        {
            await Toast.Make(text, ToastDuration.Short, 14).Show();
        }
    };

    public Func<Task> GoBack { get; set; } = () => Shell.Current is null ? Task.CompletedTask : Shell.Current.GoToAsync("..");

    public DateOnly Today => BusinessTime.Today(UtcNow());
    public DateTime MinDate => Expense.EarliestSpentOn.ToDateTime(TimeOnly.MinValue);
    public DateTime MaxDate => Today.ToDateTime(TimeOnly.MinValue);

    public ObservableCollection<CategoryChip> Categories { get; } = new();
    public ObservableCollection<OrderListItemDto> OrderResults { get; } = new();

    [ObservableProperty] private string amountText = string.Empty;
    [ObservableProperty] private string? category;
    [ObservableProperty] private DateTime spentOn;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoteCounter))]
    private string note = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOrder), nameof(ShowOrderPicker))]
    private string? orderId;

    [ObservableProperty] private string? orderLabel;
    [ObservableProperty] private string orderSearch = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowOrderPicker))]
    private bool isPickingOrder;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAmountError))]
    private string? amountError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCategoryError))]
    private string? categoryError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDateError))]
    private string? dateError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoteError))]
    private string? noteError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowForm))]
    private string? blockedMessage;

    public bool HasAmountError => AmountError is not null;
    public bool HasCategoryError => CategoryError is not null;
    public bool HasDateError => DateError is not null;
    public bool HasNoteError => NoteError is not null;
    public bool HasError => ErrorMessage is not null;
    public bool ShowForm => BlockedMessage is null;
    public bool HasOrder => OrderId is not null;
    public bool ShowOrderPicker => OrderId is null && IsPickingOrder;
    public string NoteCounter => $"{Note.Length} / {FieldLimits.Notes}";

    /// <summary>Set after a successful save (tests; the page has gone back by then).</summary>
    public ExpenseDto? Saved { get; private set; }

    public string? LastToast { get; private set; }

    /// <summary>Anything typed or chosen that would be lost by leaving.</summary>
    public bool IsDirty =>
        Saved is null &&
        (!string.IsNullOrWhiteSpace(AmountText) || Category is not null || !string.IsNullOrWhiteSpace(Note) ||
         DateOnly.FromDateTime(SpentOn) != Today || OrderId != _initialOrderId);

    public bool CanGoBack => !IsBusy;

    /// <summary>Android Back with unsaved changes asks first.</summary>
    public bool TryHandleBack()
    {
        if (_leaving || !IsDirty)
        {
            return false;
        }

        _ = CancelAsync();
        return true;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("orderId", out var value) && value?.ToString() is { Length: > 0 } id)
        {
            OrderId = Uri.UnescapeDataString(id);
            _initialOrderId = OrderId;
        }

        if (!_loadedOnce)
        {
            _loadedOnce = true;
            MainThread.BeginInvokeOnMainThread(async () => await LoadAsync());
        }
    }

    /// <summary>Page shown: load once (opened without a query, ApplyQueryAttributes may not run).</summary>
    public Task AppearAsync()
    {
        if (_loadedOnce)
        {
            return Task.CompletedTask;
        }

        _loadedOnce = true;
        return LoadAsync();
    }

    partial void OnAmountTextChanged(string value) => AmountError = null;

    partial void OnCategoryChanged(string? value)
    {
        CategoryError = null;
        foreach (var chip in Categories)
        {
            chip.IsSelected = chip.Code == value;
        }
    }

    partial void OnSpentOnChanged(DateTime value) => DateError = null;

    partial void OnNoteChanged(string value) => NoteError = null;

    [RelayCommand]
    public async Task LoadAsync()
    {
        try
        {
            var isFree = await _auth.IsFreeModeAsync();
            var user = await _auth.GetCachedUserAsync();
            var businessId = await _auth.GetBusinessIdAsync();
            var blocked = ExpenseAccess.Gate(isFree, user?.Role, LocalSubscriptionGate.Current)
                          ?? (ExpenseAccess.IsReadOnly ? ExpenseAccess.ReadOnlyMessage : null)
                          ?? (string.IsNullOrWhiteSpace(businessId) ? "Sign in again to record expenses." : null);
            BlockedMessage = blocked;
            if (blocked is null && OrderId is not null)
            {
                await LoadOrderLabelAsync(businessId!, OrderId);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Record expense: load failed: {ex.Message}");
            ErrorMessage = "Couldn't open the form. Try again.";
        }
    }

    private async Task LoadOrderLabelAsync(string businessId, string id)
    {
        if (_orders is null)
        {
            OrderLabel = "Linked order";
            return;
        }

        var order = await _orders.GetByIdAsync(businessId, id);
        if (order is null)
        {
            // Not on this phone (deleted elsewhere): don't link to something the server will drop.
            OrderId = null;
            _initialOrderId = null;
            OrderLabel = null;
            ErrorMessage = "That order isn't on this phone any more, so the expense won't be linked to it.";
            return;
        }

        OrderLabel = $"Order #{order.DisplayRef} · {order.CustomerName}";
    }

    [RelayCommand]
    private void SelectCategory(string? code)
    {
        if (ExpenseCategory.IsValid(code))
        {
            Category = code;
        }
    }

    [RelayCommand]
    private Task StartOrderPickAsync()
    {
        IsPickingOrder = true;
        return SearchOrdersAsync();
    }

    [RelayCommand]
    private async Task SearchOrdersAsync()
    {
        OrderResults.Clear();
        if (_orders is null)
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

            var found = await _orders.SearchAsync(businessId, string.IsNullOrWhiteSpace(OrderSearch) ? null : OrderSearch.Trim(), null, 20);
            foreach (var order in found)
            {
                OrderResults.Add(order);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Record expense: order search failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private void PickOrder(OrderListItemDto? order)
    {
        if (order is null)
        {
            return;
        }

        OrderId = order.Id;
        OrderLabel = $"Order #{order.DisplayRef} · {order.CustomerName}";
        IsPickingOrder = false;
        OrderResults.Clear();
    }

    [RelayCommand]
    private void RemoveOrder()
    {
        OrderId = null;
        OrderLabel = null;
    }

    /// <summary>Checks the form like the server (strict). Returns the entry, or null with the field messages set.</summary>
    public ExpenseEntry? Validate(out decimal amount)
    {
        AmountError = CategoryError = DateError = NoteError = ErrorMessage = null;
        amount = 0;
        if (!RecordPaymentViewModel.TryParseAmount(AmountText, out amount))
        {
            AmountError = "Enter the amount paid, e.g. 120 or 1,250.50.";
        }
        else if (MoneyRules.CheckAmount(amount) is { } amountProblem)
        {
            AmountError = amountProblem;
        }

        if (!ExpenseCategory.IsValid(Category))
        {
            CategoryError = "Choose what the money was spent on.";
        }

        var date = DateOnly.FromDateTime(SpentOn);
        if (Expense.CheckSpentOn(date, Today) is { } dateProblem)
        {
            DateError = dateProblem;
        }

        if (Note.Trim().Length > FieldLimits.Notes)
        {
            NoteError = $"Note must be at most {FieldLimits.Notes} characters.";
        }

        if (AmountError is not null || CategoryError is not null || DateError is not null || NoteError is not null)
        {
            // One summary, announced by the banner, so a screen reader hears what to fix.
            var fix = new List<string>();
            if (AmountError is not null) fix.Add("amount");
            if (CategoryError is not null) fix.Add("category");
            if (DateError is not null) fix.Add("date");
            if (NoteError is not null) fix.Add("note");
            ErrorMessage = $"Check the {string.Join(", ", fix)}.";
            return null;
        }

        var (error, detail, entry) = ExpenseRules.Check(amount, Category, date, Note, Today, lenient: false);
        if (error != RecordError.None || entry is null)
        {
            ErrorMessage = detail ?? "Check the details and try again.";
            return null;
        }

        return entry;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy || Saved is not null)
        {
            return;
        }

        if (Validate(out _) is not { } entry)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var businessId = await _auth.GetBusinessIdAsync();
            var user = await _auth.GetCachedUserAsync();
            if (string.IsNullOrWhiteSpace(businessId))
            {
                ErrorMessage = "Sign in again to record expenses.";
                return;
            }

            var result = await _expenses.RecordAsync(businessId, new RecordExpenseRequest
            {
                Id = _expenseId,
                Amount = entry.Amount,
                Category = entry.Category,
                SpentOn = entry.SpentOn,
                Note = entry.Note,
                OrderId = OrderId
            }, user?.UserId);
            if (!result.IsSuccess)
            {
                ErrorMessage = result.Message ?? "Couldn't save the expense. Try again.";
                return;
            }

            Saved = result.Value;
            var pending = true;
            if (IsOnline() && SyncNow is not null)
            {
                try
                {
                    await SyncNow();
                    pending = (await _expenses.GetAsync(businessId, _expenseId))?.IsPending ?? true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Record expense: sync after save failed: {ex.Message}");
                }
            }

            LastToast = pending ? SavedOffline : SavedSynced;
            _leaving = true;
            await ShowToast(LastToast);
            await GoBack();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Record expense: save failed: {ex.Message}");
            ErrorMessage = "Couldn't save the expense. Try again.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Cancel / Back: asks before dropping what was typed.</summary>
    [RelayCommand]
    public async Task CancelAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (IsDirty && !await Confirm(LeaveTitle, LeaveMessage, "Discard", "Keep editing"))
        {
            return;
        }

        _leaving = true;
        await GoBack();
    }
}
