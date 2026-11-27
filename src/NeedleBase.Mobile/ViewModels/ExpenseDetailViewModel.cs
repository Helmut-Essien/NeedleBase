using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NeedleBase.Application.Contracts.Payments;
using NeedleBase.Application.Expenses;
using NeedleBase.Mobile.Data.Services;
using NeedleBase.Mobile.Data.Sync;
using NeedleBase.Mobile.Helpers;
using NeedleBase.Mobile.Services;

namespace NeedleBase.Mobile.ViewModels;

/// <summary>
/// One expense (expenses plan section 6.4): every field, linked order, who recorded it, sync state, voided block,
/// and "Void expense" (Owner/Manager, not voided, not read-only). Expenses are never edited: void and record again.
/// </summary>
public partial class ExpenseDetailViewModel : BaseViewModel, IQueryAttributable
{
    private readonly IExpenseLocalService _expenses;
    private readonly IAuthService _auth;
    private readonly IStaffDirectory? _staff;
    private readonly LiveListRefresh _live;
    private IReadOnlyDictionary<string, string> _names = new Dictionary<string, string>();
    private string? _currentUserId;

    public ExpenseDetailViewModel(IExpenseLocalService expenses, IAuthService auth, IStaffDirectory? staff = null)
    {
        _expenses = expenses;
        _auth = auth;
        _staff = staff;
        Title = "Expense";
        _live = new LiveListRefresh(LoadAsync);
    }

    public Func<string, string, string, string, Task<bool>> Confirm { get; set; } = (title, message, accept, cancel) =>
        Shell.Current is null ? Task.FromResult(true) : Shell.Current.DisplayAlert(title, message, accept, cancel);

    public Func<string, Task> Navigate { get; set; } = route => Shell.Current is null ? Task.CompletedTask : Shell.Current.GoToAsync(route);

    public Func<string, Task> ShowToast { get; set; } = async text =>
    {
        if (Shell.Current is not null)
        {
            await Toast.Make(text, ToastDuration.Short, 14).Show();
        }
    };

    public string ExpenseId { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasExpense), nameof(AmountDisplay), nameof(CategoryLabel), nameof(DateDisplay), nameof(NoteText),
        nameof(HasNote), nameof(OrderText), nameof(HasOrder), nameof(CanOpenOrder), nameof(RecordedText), nameof(SyncText), nameof(HasSyncText),
        nameof(IsVoided), nameof(VoidedText), nameof(ConfirmText))]
    private LocalExpense? expense;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    private string? message;

    [ObservableProperty] private bool canVoid;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowVoidButton))]
    private bool isVoidFormOpen;

    [ObservableProperty] private string voidReason = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasVoidError))]
    private string? voidError;

    public bool HasExpense => Expense is not null;
    public bool HasMessage => Message is not null;
    public bool HasVoidError => VoidError is not null;
    public bool ShowVoidButton => CanVoid && !IsVoidFormOpen;

    public string AmountDisplay => Expense?.Expense.AmountDisplay ?? string.Empty;
    public string CategoryLabel => Expense?.Expense.CategoryDisplay ?? string.Empty;
    public string DateDisplay => Expense?.Expense.SpentOnDisplay ?? string.Empty;
    public string? NoteText => Expense?.Expense.Note;
    public bool HasNote => !string.IsNullOrWhiteSpace(NoteText);
    public string? OrderText => Expense?.OrderLine;
    public bool HasOrder => OrderText is not null;

    /// <summary>The linked order is on this phone (an "Order removed" link isn't tappable).</summary>
    public bool CanOpenOrder => Expense is { IsOrderRemoved: false, Expense.OrderDisplayRef: not null };

    public string RecordedText => Expense is null
        ? string.Empty
        : Expense.Expense.RecordedByUserId is null
            ? $"Recorded {Money.DateTime(Expense.Expense.CreatedAtUtc)}"
            : $"Recorded by {Name(Expense.Expense.RecordedByUserId)} · {Money.DateTime(Expense.Expense.CreatedAtUtc)}";

    public string? SyncText => Expense is { IsPending: true } ? ExpenseRow.PendingText : null;
    public bool HasSyncText => SyncText is not null;
    public bool IsVoided => Expense?.IsVoided == true;

    public string? VoidedText
    {
        get
        {
            if (Expense is not { IsVoided: true } e)
            {
                return null;
            }

            var when = e.Expense.VoidedAtUtc is { } at ? Money.DateTime(at) : "date not recorded";
            var reason = string.IsNullOrWhiteSpace(e.Expense.VoidReason) ? "No reason given" : e.Expense.VoidReason;
            var who = e.Expense.VoidedByUserId is null ? "Voided" : $"Voided by {Name(e.Expense.VoidedByUserId)}";
            return $"{who} · {when}\nReason: {reason}\nIt doesn't count toward profit.";
        }
    }

    public string ConfirmText => $"Void {AmountDisplay}? It stays in your records and stops counting toward profit.";

    partial void OnCanVoidChanged(bool value) => OnPropertyChanged(nameof(ShowVoidButton));

    partial void OnVoidReasonChanged(string value) => VoidError = null;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("expenseId", out var value) && value?.ToString() is { Length: > 0 } id)
        {
            ExpenseId = Uri.UnescapeDataString(id);
        }
    }

    public async Task AppearAsync()
    {
        _live.Attach();
        await LoadAsync();
    }

    public void Disappear() => _live.Detach();

    [RelayCommand]
    public async Task LoadAsync()
    {
        try
        {
            var isFree = await _auth.IsFreeModeAsync();
            var user = await _auth.GetCachedUserAsync();
            var businessId = await _auth.GetBusinessIdAsync();
            _currentUserId = user?.UserId;
            var blocked = ExpenseAccess.Gate(isFree, user?.Role, LocalSubscriptionGate.Current);
            if (blocked is not null || string.IsNullOrWhiteSpace(businessId))
            {
                Expense = null;
                CanVoid = false;
                Message = blocked ?? "Sign in again to see expenses.";
                return;
            }

            if (_staff is not null)
            {
                try
                {
                    _names = (await _staff.GetCachedAsync(businessId)).ToDictionary(s => s.UserId, s => s.Name, StringComparer.Ordinal);
                }
                catch (Exception)
                {
                    // Names are a nicety; the expense still shows.
                }
            }

            var found = await _expenses.GetAsync(businessId, ExpenseId);
            if (found is null)
            {
                Expense = null;
                CanVoid = false;
                Message = "This expense isn't on this phone.";
                return;
            }

            Message = null;
            Expense = found;
            CanVoid = !found.IsVoided && !ExpenseAccess.IsReadOnly;
            if (!CanVoid)
            {
                IsVoidFormOpen = false;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Expense detail: load failed: {ex.Message}");
            Message = "Couldn't load this expense. Try again.";
        }
    }

    [RelayCommand]
    private void StartVoid()
    {
        if (CanVoid)
        {
            VoidReason = string.Empty;
            VoidError = null;
            IsVoidFormOpen = true;
        }
    }

    [RelayCommand]
    private void CancelVoid()
    {
        IsVoidFormOpen = false;
        VoidError = null;
    }

    [RelayCommand]
    public async Task ConfirmVoidAsync()
    {
        if (IsBusy || !CanVoid || Expense is null)
        {
            return;
        }

        if (ExpenseRules.CheckVoidReason(VoidReason, lenient: false) is { } problem)
        {
            VoidError = problem;
            return;
        }

        if (!await Confirm("Void expense", ConfirmText, "Void", "Keep it"))
        {
            return;
        }

        IsBusy = true;
        try
        {
            var businessId = await _auth.GetBusinessIdAsync();
            var user = await _auth.GetCachedUserAsync();
            var result = await _expenses.VoidAsync(businessId!, ExpenseId, VoidReason.Trim(), user?.UserId);
            if (!result.IsSuccess)
            {
                VoidError = result.Message ?? "Couldn't void the expense. Try again.";
                return;
            }

            IsVoidFormOpen = false;
            await LoadAsync();
            await ShowToast(Expense is { IsPending: true } ? "Expense voided on this device" : "Expense voided");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Expense detail: void failed: {ex.Message}");
            VoidError = "Couldn't void the expense. Try again.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task OpenOrderAsync() =>
        CanOpenOrder && Expense?.Expense.OrderId is { } id
            ? Navigate($"{nameof(Views.OrderDetailPage)}?orderId={Uri.EscapeDataString(id)}")
            : Task.CompletedTask;

    private string Name(string? userId) =>
        userId is null ? "a team member"
        : string.Equals(userId, _currentUserId, StringComparison.Ordinal) ? "you"
        : _names.TryGetValue(userId, out var name) && !string.IsNullOrWhiteSpace(name) ? name
        : "a team member";
}
