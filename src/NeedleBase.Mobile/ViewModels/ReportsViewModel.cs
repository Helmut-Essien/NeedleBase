using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Licensing;
using NeedleBase.Application.Contracts.Reports;
using NeedleBase.Application.Reports;
using NeedleBase.Mobile.Data.Services;
using NeedleBase.Mobile.Data.Sync;
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

    public ReportsViewModel(IAuthService auth, IReportLocalService reports)
    {
        _auth = auth;
        _reports = reports;
        Title = "Reports";
    }

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
                chosen = ReportRules.FirstOfMonth(today);
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
