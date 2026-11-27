using MauiIcons.Material;
using NeedleBase.Application.Contracts.Licensing;
using NeedleBase.Application.Licensing;
using NeedleBase.Application.Reports;
using NeedleBase.Application.Staff;
using NeedleBase.Mobile.Data.Services;
using NeedleBase.Mobile.Services;
using NeedleBase.Mobile.Services.Updates;
using NeedleBase.Mobile.ViewModels;

namespace NeedleBase.Mobile.Views;

/// <summary>More tab: Free tools (Save a copy) or Staff / account for online shops.</summary>
public sealed class MorePage : ContentPage
{
    private readonly IAuthService _auth;
    private readonly IFreeModeLocalService _freeMode;
    private readonly IFreeCopyService _freeCopy;
    private readonly ShopExportApi _export;
    private readonly ContentView _dynamicSlot = new();
    private string? _modeKey;
    private Label? _freeUsageLabel;
    private Label? _planUsageLabel;
    private bool _exporting;

    public MorePage(
        IAuthService auth,
        AppUpdateService updates,
        IFreeModeLocalService freeMode,
        IFreeCopyService freeCopy,
        ShopExportApi export)
    {
        _auth = auth;
        _freeMode = freeMode;
        _freeCopy = freeCopy;
        _export = export;
        var body = new VerticalStackLayout { Spacing = 8 };
        body.Add(Ui.Heading("More"));
        body.Add(_dynamicSlot);

        body.Add(Ui.Heading("About", 2));
        var about = new VerticalStackLayout
        {
            Children =
            {
                Ui.LinkRow("Terms and Conditions", MaterialIcons.Description, () => Shell.Current.GoToAsync(nameof(TermsPage)), "MoreTermsLink"),
                Ui.Divider(),
                Ui.LinkRow("Privacy Policy", MaterialIcons.PrivacyTip, () => Shell.Current.GoToAsync(nameof(PrivacyPage)), "MorePrivacyLink")
            }
        };
        if (updates.IsEnabled)
        {
            about.Add(Ui.Divider());
            about.Add(Ui.LinkRow("Check for updates", MaterialIcons.SystemUpdate, () => CheckForUpdatesAsync(updates), "MoreCheckUpdates",
                "Download the newest NeedleBase"));
        }

        body.Add(Ui.Card(about));
        body.Add(Ui.Body($"NeedleBase version {AppInfo.Current.VersionString}"));
        Ui.Chrome(this, "More", body, backButton: false);
    }

    private bool _checking;
    private bool _savingCopy;

    private async Task CheckForUpdatesAsync(AppUpdateService updates)
    {
        if (_checking)
        {
            return;
        }

        _checking = true;
        try
        {
            var result = await updates.CheckAsync(manual: true);
            if (result.Kind != UpdateKind.None && result.Manifest is not null)
            {
                await UpdatePrompter.ShowAsync(updates, result);
            }
            else
            {
                await DisplayAlert("Check for updates", result.Error ?? $"You have the newest NeedleBase ({AppInfo.Current.VersionString}).", "OK");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"More: update check failed: {ex.Message}");
            await DisplayAlert("Check for updates", "Couldn't check for updates. Please try again later.", "OK");
        }
        finally
        {
            _checking = false;
        }
    }

    private async Task SaveCopyAsync()
    {
        if (_savingCopy)
        {
            return;
        }

        _savingCopy = true;
        try
        {
            var user = await _auth.GetCachedUserAsync();
            var businessId = user?.BusinessId;
            if (string.IsNullOrWhiteSpace(businessId))
            {
                await DisplayAlert("Save a copy", "Couldn't find this Free shop on the phone.", "OK");
                return;
            }

            var json = await _freeCopy.BuildCopyJsonAsync(businessId, user!.BusinessName);
            if (string.IsNullOrEmpty(json))
            {
                await DisplayAlert("Save a copy", "Couldn't build the copy. Try again.", "OK");
                return;
            }

            var dir = Path.Combine(FileSystem.CacheDirectory, "free-copies");
            Directory.CreateDirectory(dir);
            var safe = string.Concat((user.BusinessName ?? "shop").Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_').Take(40));
            if (string.IsNullOrEmpty(safe))
            {
                safe = "shop";
            }

            var path = Path.Combine(dir, $"NeedleBase-Free-{safe}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
            await File.WriteAllTextAsync(path, json);
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "Save a copy",
                File = new ShareFile(path)
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"More: Save a copy failed: {ex.Message}");
            await DisplayAlert("Save a copy", "Couldn't share the copy. Try again.", "OK");
        }
        finally
        {
            _savingCopy = false;
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            var isFree = await _auth.IsFreeModeAsync();
            var role = (await _auth.GetCachedUserAsync())?.Role;
            var key = isFree ? "free" : (role ?? "none");
            if (key == _modeKey && _dynamicSlot.Content is not null)
            {
                // Free: refresh meters only. Online: rebuild so plan/export pick up LocalSubscriptionGate after sync
                // (a restored pre-enrichment cache must not leave Export permanently hidden).
                if (isFree && _freeUsageLabel is not null)
                {
                    await FillFreeUsageAsync(_freeUsageLabel);
                    return;
                }

                if (!isFree)
                {
                    _dynamicSlot.Content = BuildOnlineSlot(role);
                    return;
                }

                return;
            }

            _modeKey = key;
            _dynamicSlot.Content = isFree ? BuildFreeSlot() : BuildOnlineSlot(role);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"More: layout failed: {ex.Message}");
        }
    }

    private View BuildFreeSlot()
    {
        _planUsageLabel = null;
        _freeUsageLabel = Ui.Body();
        _freeUsageLabel.AutomationId = "MoreFreeUsage";
        _ = FillFreeUsageAsync(_freeUsageLabel);

        return new VerticalStackLayout
        {
            Children =
            {
                Ui.Heading("Free on this phone", 2),
                Ui.Card(new VerticalStackLayout
                {
                    Children =
                    {
                        _freeUsageLabel,
                        Ui.Divider(),
                        Ui.LinkRow("Save a copy", MaterialIcons.SaveAlt, SaveCopyAsync, "MoreSaveCopy",
                            "Keep your own backup — NeedleBase does not sync Free records"),
                        Ui.Divider(),
                        Ui.LinkRow("Go online with a licence key", MaterialIcons.CloudUpload,
                            () => Shell.Current.GoToAsync(nameof(FreeUpgradePage)), "MoreGoOnline",
                            "Keep the records on this phone and sync them to your online shop"),
                        Ui.Divider(),
                        Ui.Body(
                            "Free has no account, sync, website or staff invites until you go online with a licence key.")
                    }
                })
            }
        };
    }

    private async Task FillFreeUsageAsync(Label label)
    {
        try
        {
            var businessId = await _auth.GetBusinessIdAsync();
            if (string.IsNullOrWhiteSpace(businessId))
            {
                label.Text = FreePlanLimits.CustomerLimitMessage(0) + " " + FreePlanLimits.OrderLimitMessage(0);
                return;
            }

            var (customers, orders) = await _freeMode.GetUsageAsync(businessId);
            label.Text = FreePlanLimits.UsageMeters(customers, orders)
                + "\nDeleting a customer or order does not free a slot.";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"More: Free usage failed: {ex.Message}");
            label.Text = "Free on this phone — usage unavailable right now.";
        }
    }

    private View BuildOnlineSlot(string? role)
    {
        _freeUsageLabel = null;
        var root = new VerticalStackLayout();
        if (StaffRoles.CanView(role))
        {
            root.Add(Ui.Heading("Team", 2));
            root.Add(Ui.Card(new VerticalStackLayout
            {
                Children =
                {
                    Ui.LinkRow("Staff", MaterialIcons.Group, () => Shell.Current.GoToAsync(nameof(StaffPage)), "MoreStaff",
                        StaffRoles.CanManage(role) ? "Invite someone to help, change roles" : "See who's on the team"),
                    Ui.Divider(),
                    Ui.LinkRow("WhatsApp", MaterialIcons.Chat, () => Shell.Current.GoToAsync(nameof(WhatsAppSettingsPage)), "MoreWhatsApp",
                        "Automatic order updates and message log")
                }
            }));
        }

        if (ReportRules.CanView(role))
        {
            var canReports = NeedleBase.Mobile.Data.Sync.LocalSubscriptionGate.Current?.CanReports == true;
            root.Add(Ui.Heading("Money", 2));
            var canExpenses = canReports && NeedleBase.Application.Expenses.ExpenseRules.CanManage(role);
            var money = new VerticalStackLayout
            {
                Children =
                {
                    Ui.LinkRow(
                        "Monthly reports",
                        MaterialIcons.BarChart,
                        canReports
                            ? () => Shell.Current.GoToAsync(nameof(ReportsPage))
                            : () => DisplayAlert("Monthly reports", ReportRules.NotAllowedMessage, "OK"),
                        "MoreReports",
                        canReports
                            ? "Money in, money spent, profit and what customers still owe"
                            : "Comes with the Workshop and Production plans")
                }
            };
            if (NeedleBase.Application.Expenses.ExpenseRules.CanManage(role))
            {
                // Expenses (expenses plan 6.1): Owner/Manager only; Starter sees why, Free never gets here (online slot).
                money.Add(Ui.Divider());
                money.Add(Ui.LinkRow(
                    "Expenses",
                    MaterialIcons.ReceiptLong,
                    canExpenses
                        ? () => Shell.Current.GoToAsync(nameof(ExpensesPage))
                        : () => DisplayAlert("Expenses", NeedleBase.Application.Expenses.ExpenseRules.NotAllowedMessage, "OK"),
                    "MoreExpenses",
                    canExpenses ? ExpenseAccess.Subtitle : "Comes with the Workshop and Production plans"));
            }

            root.Add(Ui.Card(money));
        }

        if (StaffRoles.CanManage(role))
        {
            root.Add(Ui.Heading("License", 2));
            var licenseChildren = new VerticalStackLayout { Children = { CreatePlanUsageLabel() } };
            if (ShouldOfferExport(NeedleBase.Mobile.Data.Sync.LocalSubscriptionGate.Current))
            {
                licenseChildren.Add(Ui.Divider());
                licenseChildren.Add(Ui.LinkRow(
                    "Export shop data",
                    MaterialIcons.Download,
                    ExportOnlineAsync,
                    "MoreExport",
                    "Download a JSON copy of customers, orders and payments"));
            }

            root.Add(Ui.Card(licenseChildren));
        }

        root.Add(Ui.Heading("This device", 2));
        root.Add(Ui.Card(Ui.LinkRow("Sync details", MaterialIcons.Sync, () => Shell.Current.GoToAsync(nameof(SyncDetailsPage)), "MoreSyncDetails",
            "What's waiting to sync, problems and conflicts")));

        root.Add(Ui.Heading("Your account", 2));
        root.Add(Ui.Card(new VerticalStackLayout
        {
            Children =
            {
                Ui.LinkRow("Change password", MaterialIcons.Lock, () => Shell.Current.GoToAsync(nameof(ChangePasswordPage)), "MoreChangePassword"),
                Ui.Divider(),
                Ui.LinkRow("Delete account", MaterialIcons.PersonOff, () => Shell.Current.GoToAsync(nameof(DeleteAccountPage)), "MoreDeleteAccount")
            }
        }));
        return root;
    }

    private Label CreatePlanUsageLabel()
    {
        _planUsageLabel = Ui.Body(PlanUsageLine(NeedleBase.Mobile.Data.Sync.LocalSubscriptionGate.Current));
        _planUsageLabel.AutomationId = "MorePlanUsage";
        return _planUsageLabel;
    }

    /// <summary>
    /// Export unless an enriched status (has <see cref="SubscriptionStatusDto.PlanCode"/>) explicitly denies it.
    /// Pre-step-5 restored JSON defaults <c>CanExport</c> to false — that must not hide Export.
    /// </summary>
    public static bool ShouldOfferExport(SubscriptionStatusDto? status) =>
        status is null || status.PlanCode is null || status.CanExport;

    /// <summary>Plan/staff line for More. Incomplete restored cache waits for the next sync.</summary>
    public static string PlanUsageLine(SubscriptionStatusDto? status)
    {
        if (status is null || status.PlanCode is null)
        {
            return "Plan and usage appear after the next sync.";
        }

        var plan = status.PlanDisplayName ?? status.Plan ?? "Plan";
        string line;
        if (status.MaxStaffExcludingOwner is { } max)
        {
            line = $"{plan}: {status.StaffUsedExcludingOwner}/{max} staff (Owner not counted).";
        }
        else
        {
            line = $"{plan}: {status.StaffUsedExcludingOwner} staff used (unlimited).";
        }

        if (status.PendingInviteCount > 0)
        {
            var inviteWord = status.PendingInviteCount == 1 ? "invite" : "invites";
            line += $" {status.PendingInviteCount} {inviteWord} waiting.";
        }

        return line;
    }

    private async Task ExportOnlineAsync()
    {
        if (_exporting)
        {
            return;
        }

        _exporting = true;
        try
        {
            var (bytes, fileName, error) = await _export.DownloadAsync();
            if (bytes is null || string.IsNullOrWhiteSpace(fileName))
            {
                await DisplayAlert("Export", error ?? "Couldn't download the export.", "OK");
                return;
            }

            var dir = Path.Combine(FileSystem.CacheDirectory, "exports");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, fileName);
            await File.WriteAllBytesAsync(path, bytes);
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "Export shop data",
                File = new ShareFile(path)
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"More: export failed: {ex.Message}");
            await DisplayAlert("Export", "Couldn't share the export. Try again.", "OK");
        }
        finally
        {
            _exporting = false;
        }
    }
}
