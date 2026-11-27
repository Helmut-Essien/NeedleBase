using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Licensing;
using NeedleBase.Application.Contracts.Payments;
using NeedleBase.Application.Contracts.Reports;
using NeedleBase.Application.Reports;
using NeedleBase.Mobile.Data.Services;
using NeedleBase.Mobile.Data.Sync;
using NeedleBase.Mobile.Helpers;
using NeedleBase.Mobile.Services;

namespace NeedleBase.Mobile.ViewModels;

/// <summary>
/// Profit and monthly reports on the phone (plan step 7b). Owner/Manager on Workshop, Production or unlimited
/// shops; computed from this phone's records, so it works offline.
/// </summary>
public partial class ReportsViewModel : BaseViewModel
{
    public const string FreeMessage =
        "Monthly reports come with the Workshop and Production plans. Go online with a licence key first.";

    private readonly IAuthService _auth;
    private readonly IReportLocalService _reports;

    [ObservableProperty] private MonthlyReportDto? report;
    [ObservableProperty] private string? message;

    private readonly LiveListRefresh _live;

    public ReportsViewModel(IAuthService auth, IReportLocalService reports)
    {
        _auth = auth;
        _reports = reports;
        Title = "Reports";
        _live = new LiveListRefresh(() => LoadAsync(Report?.Month));
    }

    /// <summary>Navigation (tests replace it; no Shell there).</summary>
    public Func<string, Task> Navigate { get; set; } = route => Shell.Current is null ? Task.CompletedTask : Shell.Current.GoToAsync(route);

    /// <summary>Page shown: load (keeping the month) and follow sync changes (expenses plan 6.6).</summary>
    public async Task AppearAsync()
    {
        _live.Attach();
        await LoadAsync(Report?.Month);
    }

    public void Disappear() => _live.Detach();

    /// <summary>"GH₵ 40.00 more than October 2026", "… less than …", or "Same as …" (money spent vs the month before).</summary>
    public static string SpentChange(MonthlyReportDto r)
    {
        var previous = r.Trend.Count >= 2 ? r.Trend[^2].Label : "last month";
        var diff = r.Spent - r.PreviousSpent;
        return diff switch
        {
            > 0 => $"{Money.Format(diff)} more than {previous}",
            < 0 => $"{Money.Format(-diff)} less than {previous}",
            _ => $"Same as {previous}"
        };
    }

    /// <summary>Opens the expenses list on the report's month.</summary>
    [RelayCommand]
    private Task SeeExpensesAsync() =>
        Report is { } r ? Navigate($"{nameof(Views.ExpensesPage)}?month={Uri.EscapeDataString(r.Month)}") : Task.CompletedTask;

    /// <summary>Clock for tests.</summary>
    public Func<DateTimeOffset> UtcNow { get; set; } = () => DateTimeOffset.UtcNow;

    /// <summary>Why reports can't be shown, or null when they can. Free and unknown status stay closed.</summary>
    public static string? Gate(bool isFree, string? role, SubscriptionStatusDto? status)
    {
        if (isFree)
        {
            return FreeMessage;
        }

        if (!ReportRules.CanView(role))
        {
            return ReportRules.ForbiddenMessage;
        }

        return status?.CanReports == true ? null : ReportRules.NotAllowedMessage;
    }

    /// <summary>Load a month (<c>yyyy-MM</c>; null = this month).</summary>
    public async Task LoadAsync(string? month = null)
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
            var blocked = Gate(isFree, user?.Role, LocalSubscriptionGate.Current);
            var businessId = await _auth.GetBusinessIdAsync();
            if (blocked is null && string.IsNullOrWhiteSpace(businessId))
            {
                blocked = "Sign in again to see reports.";
            }

            if (blocked is not null)
            {
                Report = null;
                Message = blocked;
                return;
            }

            var today = BusinessTime.Today(UtcNow());
            if (!ReportRules.TryResolveMonth(month, today, out var chosen))
            {
                // Same as the Api/web: bad month → message, not silent fall-back to this month.
                Report = null;
                Message = ReportRules.MonthInvalidMessage;
                return;
            }

            Report = await _reports.GetAsync(businessId!, chosen, today);
            Message = null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Reports: load failed: {ex.Message}");
            Message = "Couldn't build the report. Try again.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task PreviousAsync() => Report?.PreviousMonth is { } m ? LoadAsync(m) : Task.CompletedTask;

    [RelayCommand]
    private Task NextAsync() => Report?.NextMonth is { } m ? LoadAsync(m) : Task.CompletedTask;
}
