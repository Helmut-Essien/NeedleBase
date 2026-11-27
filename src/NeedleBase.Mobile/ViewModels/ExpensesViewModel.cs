using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Expenses;
using NeedleBase.Application.Contracts.Licensing;
using NeedleBase.Application.Expenses;
using NeedleBase.Application.Reports;
using NeedleBase.Domain.Enums;
using NeedleBase.Mobile.Data.Services;
using NeedleBase.Mobile.Data.Sync;
using NeedleBase.Mobile.Helpers;
using NeedleBase.Mobile.Services;

namespace NeedleBase.Mobile.ViewModels;

/// <summary>Who sees expenses on the phone (expenses plan section 1): Owner/Manager, a plan with reports, never Free.</summary>
public static class ExpenseAccess
{
    public const string FreeMessage =
        "Expenses come with the Workshop and Production plans. Go online with a licence key first.";

    public const string Subtitle = "Money the shop paid out: fabric, staff pay, rent and other costs";

    /// <summary>Why expenses can't be shown, or null when they can. Free and unknown status stay closed (same as reports).</summary>
    public static string? Gate(bool isFree, string? role, SubscriptionStatusDto? status)
    {
        if (isFree)
        {
            return FreeMessage;
        }

        if (!ExpenseRules.CanManage(role))
        {
            return ExpenseRules.ForbiddenMessage;
        }

        return status?.CanReports == true ? null : ExpenseRules.NotAllowedMessage;
    }

    /// <summary>Lapsed shop: view only.</summary>
    public static bool IsReadOnly => LocalSubscriptionGate.IsReadOnly;

    public static string ReadOnlyMessage => NeedleBase.Application.Licensing.LicenseErrors.ReadOnlyMessage;

    public static string MonthLabel(DateOnly month) => month.ToString("MMMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("en-GB"));

    public static string CountText(int count) => count == 1 ? "1 expense" : $"{count} expenses";
}

/// <summary>One row in the expenses list / order section.</summary>
public sealed record ExpenseRow(LocalExpense Item)
{
    public const string PendingText = "Not synced yet";

    public string Id => Item.Id;
    public string Category => Item.Expense.Category;
    public string CategoryLabel => Item.Expense.CategoryDisplay;
    public string AmountDisplay => Item.Expense.AmountDisplay;
    public string DateDisplay => Item.Expense.SpentOnDisplay;

    /// <summary>First line of the note (the list stays one row per expense).</summary>
    public string? NoteLine => string.IsNullOrWhiteSpace(Item.Expense.Note)
        ? null
        : Item.Expense.Note.Split('\n', 2)[0].Trim();

    public bool HasNote => NoteLine is not null;

    /// <summary>"#0042 · Efua Mensah", "Order removed", or null.</summary>
    public string? OrderChip => Item.IsOrderRemoved ? "Order removed"
        : Item.Expense.OrderDisplayRef is { } displayRef ? $"#{displayRef} · {Item.Expense.CustomerName}"
        : null;

    public bool HasOrderChip => OrderChip is not null;
    public bool IsVoided => Item.IsVoided;
    public bool ShowPending => Item.IsPending && !Item.IsVoided;

    /// <summary>Whole row for screen readers (status in words, not colour only).</summary>
    public string Description
    {
        get
        {
            var parts = new List<string> { CategoryLabel, AmountDisplay, DateDisplay };
            if (NoteLine is { } note) parts.Add(note);
            if (OrderChip is { } order) parts.Add(order == "Order removed" ? order : "Order " + order);
            if (IsVoided) parts.Add("Voided");
            if (ShowPending) parts.Add(PendingText);
            return string.Join(", ", parts);
        }
    }
}

/// <summary>A category filter or form chip ("All" has a null code).</summary>
public partial class CategoryChip : ObservableObject
{
    public CategoryChip(string? code, string label)
    {
        Code = code;
        Label = label;
    }

    public string? Code { get; }
    public string Label { get; }

    [ObservableProperty] private bool isSelected;
}

/// <summary>
/// Expenses list (expenses plan section 6.2): one Accra month (or one order across all months), category filter,
/// totals that never count voided rows. Works from this phone's records (offline); pull to refresh syncs.
/// </summary>
public partial class ExpensesViewModel : BaseViewModel, IQueryAttributable
{
    public const int ListLimit = 100;

    private readonly IAuthService _auth;
    private readonly IExpenseLocalService _expenses;
    private readonly LiveListRefresh _live;
    private bool _queryApplied;

    public ExpensesViewModel(IAuthService auth, IExpenseLocalService expenses)
    {
        _auth = auth;
        _expenses = expenses;
        Title = "Expenses";
        _live = new LiveListRefresh(LiveReloadAsync);
        Chips.Add(new CategoryChip(null, "All") { IsSelected = true });
        foreach (var code in ExpenseCategory.All)
        {
            Chips.Add(new CategoryChip(code, ExpenseCategory.DisplayName(code)));
        }

        Month = ReportRules.FirstOfMonth(BusinessTime.Today(UtcNow()));
        MonthLabel = ExpenseAccess.MonthLabel(Month);
    }

    /// <summary>Clock for tests.</summary>
    public Func<DateTimeOffset> UtcNow { get; set; } = () => DateTimeOffset.UtcNow;

    /// <summary>Pull-to-refresh sync (the app wires <c>SyncService.SyncForRefreshAsync</c>).</summary>
    public Func<Task>? SyncForRefresh { get; set; }

    /// <summary>Connection check (tests replace it).</summary>
    public Func<bool> IsOnline { get; set; } = DefaultIsOnline;

    /// <summary>Shell navigation (tests record the route).</summary>
    public Func<string, Task> Navigate { get; set; } = route => Shell.Current is null ? Task.CompletedTask : Shell.Current.GoToAsync(route);

    public ObservableCollection<ExpenseRow> Items { get; } = new();
    public ObservableCollection<CategoryChip> Chips { get; } = new();

    public DateOnly Month { get; private set; }
    public string? SelectedCategory { get; private set; }

    [ObservableProperty] private string monthLabel = string.Empty;
    [ObservableProperty] private bool canGoPrevious;
    [ObservableProperty] private bool canGoNext;
    [ObservableProperty] private string summaryText = string.Empty;
    [ObservableProperty] private string emptyText = string.Empty;
    [ObservableProperty] private bool isRefreshing;
    [ObservableProperty] private bool isReadOnly;
    [ObservableProperty] private bool isOffline;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowMonthSwitcher))]
    private string? orderId;

    [ObservableProperty] private string? orderTitle;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBlocked), nameof(ShowContent), nameof(ShowMonthSwitcher), nameof(ShowEmpty), nameof(CanRecord))]
    private string? message;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmpty))]
    private bool loaded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? errorMessage;

    public bool IsBlocked => Message is not null;
    public bool ShowContent => Message is null;
    public bool ShowMonthSwitcher => Message is null && OrderId is null;
    public bool ShowEmpty => Loaded && Message is null && Items.Count == 0;
    public bool HasError => ErrorMessage is not null;

    /// <summary>"Record expense" shows only when allowed and not read-only.</summary>
    public bool CanRecord => Message is null && !IsReadOnly;

    public string ReadOnlyMessage => ExpenseAccess.ReadOnlyMessage;

    public string OfflineMessage => "You're offline. These are the expenses on this phone; new ones sync when you're back online.";

    partial void OnIsReadOnlyChanged(bool value) => OnPropertyChanged(nameof(CanRecord));

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("orderId", out var order) && order?.ToString() is { Length: > 0 } id)
        {
            OrderId = Uri.UnescapeDataString(id);
        }

        if (query.TryGetValue("month", out var month) && month?.ToString() is { Length: > 0 } key &&
            ReportRules.TryResolveMonth(Uri.UnescapeDataString(key), BusinessTime.Today(UtcNow()), out var chosen))
        {
            SetMonth(chosen);
        }

        _queryApplied = true;
    }

    public async Task AppearAsync()
    {
        _live.Attach();
        await LoadAsync();
    }

    public void Disappear() => _live.Detach();

    public Task LiveReloadAsync() => LoadAsync();

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var isFree = await _auth.IsFreeModeAsync();
            var user = await _auth.GetCachedUserAsync();
            var businessId = await _auth.GetBusinessIdAsync();
            var blocked = ExpenseAccess.Gate(isFree, user?.Role, LocalSubscriptionGate.Current);
            if (blocked is null && string.IsNullOrWhiteSpace(businessId))
            {
                blocked = "Sign in again to see expenses.";
            }

            IsReadOnly = ExpenseAccess.IsReadOnly;
            IsOffline = !IsOnline();
            if (blocked is not null)
            {
                Items.Clear();
                Message = blocked;
                Loaded = true;
                return;
            }

            var category = SelectedCategory;
            var result = await _expenses.ListAsync(businessId!, new ExpenseListQuery
            {
                Month = OrderId is null ? ReportRules.Key(Month) : null,
                OrderId = OrderId,
                Category = category,
                IncludeVoided = true,
                PageSize = ListLimit
            });
            if (!result.IsSuccess)
            {
                ErrorMessage = result.Message ?? "Couldn't load expenses.";
                return;
            }

            var list = result.Value!;
            CollectionSync.Apply(Items, list.Items.Select(i => new ExpenseRow(i)).ToList(), (a, b) => a == b, r => r);
            var label = category is null ? null : ExpenseCategory.DisplayName(category);
            var total = $"{list.TotalDisplay} · {ExpenseAccess.CountText(list.Count)}";
            if (OrderId is not null)
            {
                OrderTitle ??= list.Items.FirstOrDefault()?.Expense.OrderDisplayRef is { } displayRef ? $"Order #{displayRef}" : "This order";
                SummaryText = label is null ? $"Spent on this order: {total}" : $"{label} on this order: {total}";
                EmptyText = label is null ? "No expenses for this order yet." : $"No {label.ToLowerInvariant()} expenses for this order.";
            }
            else
            {
                SummaryText = label is null ? $"Spent in {MonthLabel}: {total}" : $"{label} in {MonthLabel}: {total}";
                EmptyText = label is null
                    ? $"No expenses in {MonthLabel}. Record fabric, staff pay, rent and other costs to see your profit."
                    : $"No {label.ToLowerInvariant()} expenses in {MonthLabel}. Choose All to see every expense.";
            }

            ErrorMessage = null;
            Message = null;
            Loaded = true;
            OnPropertyChanged(nameof(ShowEmpty));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Expenses: load failed: {ex.Message}");
            ErrorMessage = "Couldn't load expenses. Try again.";
        }
        finally
        {
            IsBusy = false;
            IsRefreshing = false;
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        try
        {
            if (SyncForRefresh is not null)
            {
                await SyncForRefresh();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Expenses: refresh sync failed: {ex.Message}");
        }

        await LoadAsync();
        IsRefreshing = false;
    }

    [RelayCommand]
    private Task PreviousAsync()
    {
        if (!CanGoPrevious)
        {
            return Task.CompletedTask;
        }

        SetMonth(Month.AddMonths(-1));
        return LoadAsync();
    }

    [RelayCommand]
    private Task NextAsync()
    {
        if (!CanGoNext)
        {
            return Task.CompletedTask;
        }

        SetMonth(Month.AddMonths(1));
        return LoadAsync();
    }

    [RelayCommand]
    private Task SelectCategoryAsync(string? code)
    {
        SelectedCategory = ExpenseCategory.IsValid(code) ? code : null;
        foreach (var chip in Chips)
        {
            chip.IsSelected = chip.Code == SelectedCategory;
        }

        return LoadAsync();
    }

    [RelayCommand]
    private Task RecordAsync()
    {
        if (!CanRecord)
        {
            return Task.CompletedTask;
        }

        return Navigate(OrderId is null
            ? nameof(Views.RecordExpensePage)
            : $"{nameof(Views.RecordExpensePage)}?orderId={Uri.EscapeDataString(OrderId)}");
    }

    [RelayCommand]
    private Task OpenAsync(ExpenseRow? row) =>
        row is null ? Task.CompletedTask : Navigate($"{nameof(Views.ExpenseDetailPage)}?expenseId={Uri.EscapeDataString(row.Id)}");

    /// <summary>True once query attributes arrived (the page loads after them).</summary>
    public bool QueryApplied => _queryApplied;

    private void SetMonth(DateOnly month)
    {
        var current = ReportRules.FirstOfMonth(BusinessTime.Today(UtcNow()));
        if (month > current) month = current;
        if (month < ReportRules.EarliestMonth) month = ReportRules.EarliestMonth;
        Month = month;
        MonthLabel = ExpenseAccess.MonthLabel(month);
        CanGoPrevious = month > ReportRules.EarliestMonth;
        CanGoNext = month < current;
    }

    partial void OnMonthLabelChanged(string value)
    {
        var current = ReportRules.FirstOfMonth(BusinessTime.Today(UtcNow()));
        CanGoPrevious = Month > ReportRules.EarliestMonth;
        CanGoNext = Month < current;
    }

    internal static bool DefaultIsOnline()
    {
        try
        {
            return Connectivity.Current.NetworkAccess == NetworkAccess.Internet;
        }
        catch (Exception)
        {
            return true; // unit tests: no platform connectivity
        }
    }
}
