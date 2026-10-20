using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NeedleBase.Mobile.Data.Services;
using NeedleBase.Mobile.Helpers;
using NeedleBase.Mobile.Services;
using MauiIcons.Material;

namespace NeedleBase.Mobile.ViewModels;

public sealed class HomeStageItem
{
    public HomeStageItem(string name, Color progressColor)
    {
        Name = name;
        ProgressColor = progressColor;
    }

    public string Name { get; }
    public Color ProgressColor { get; }
    public int Count { get; set; }
    // Set by whoever wires a real order service: Count / active orders (0 when none).
    public double Fraction { get; set; }
    public string CountText => Count.ToString();
}

public sealed class HomeAttentionItem
{
    public required string OrderId { get; init; }
    public required string Initials { get; init; }
    public required string Name { get; init; }
    public required string Detail { get; init; }
    public required string StatusText { get; init; }
    public bool IsReady { get; init; }
    public bool IsOverdue { get; init; }
    public ImageSource? StatusIconImage { get; init; }
    public Color StatusBackground { get; init; } = Colors.LightGray;
    public Color StatusForeground { get; init; } = Colors.Gray;
    public IRelayCommand? OpenCommand { get; set; }
}

public partial class HomeViewModel : BaseViewModel
{
    private readonly IAuthService _auth;
    private readonly ICustomerLocalService _customers;
    private readonly IOrderLocalService _orders;

    public HomeViewModel(IAuthService auth, ICustomerLocalService customers, IOrderLocalService orders)
    {
        _auth = auth;
        _customers = customers;
        _orders = orders;
        Title = "Home";

        foreach (var status in HomeStageStatuses)
        {
            Stages.Add(new HomeStageItem(StageLabel(status), OrderStatusPresentation.Accent(status)));
        }
    }

    // Kept for compatibility with the previous Home screen.
    public string Tagline => "You're signed in. Add customers and measurements from the Customers tab.";

    [ObservableProperty] private string greeting = "Good morning";
    [ObservableProperty] private string businessName = "NeedleBase";
    [ObservableProperty] private string userInitials = "NB";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCustomersSavedText))]
    private string customersSavedText = string.Empty;

    [ObservableProperty] private int activeOrdersCount;
    [ObservableProperty] private int readyToCollectCount;

    public string ActiveOrdersCaption => "New to Ready";
    public string ReadyToCollectCaption => "Waiting for pickup";

    public ObservableCollection<HomeStageItem> Stages { get; } = new();
    public ObservableCollection<HomeAttentionItem> AttentionItems { get; } = new();

    public bool HasCustomersSavedText => !string.IsNullOrWhiteSpace(CustomersSavedText);
    public bool HasAttentionItems => AttentionItems.Count > 0;
    public bool ShowNoAttention => AttentionItems.Count == 0;

    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            var user = await _auth.GetCachedUserAsync();
            if (user is not null)
            {
                var hour = DateTime.Now.Hour;
                var dayPart = hour < 12 ? "Good morning" : hour < 17 ? "Good afternoon" : "Good evening";
                Greeting = string.IsNullOrWhiteSpace(user.FirstName) ? dayPart : $"{dayPart}, {user.FirstName.Trim()}";
                BusinessName = string.IsNullOrWhiteSpace(user.BusinessName) ? "NeedleBase" : user.BusinessName.Trim();
                UserInitials = BuildInitials(user.FirstName, user.Surname, user.BusinessName);
            }

            var businessId = await _auth.GetBusinessIdAsync();
            if (!string.IsNullOrWhiteSpace(businessId))
            {
                var customers = await _customers.SearchAsync(businessId, string.Empty);
                CustomersSavedText = customers.Count switch
                {
                    0 => string.Empty,
                    1 => "1 customer saved on this device",
                    >= 50 => "50+ customers saved on this device",
                    var n => $"{n} customers saved on this device"
                };

                var counts = await _orders.GetCountsAsync(businessId);
                ActiveOrdersCount = counts.ActiveCount;
                ReadyToCollectCount = counts.ReadyToCollectCount;
                var active = Math.Max(counts.ActiveCount, 1);
                Stages.Clear();
                foreach (var status in HomeStageStatuses)
                {
                    Stages.Add(BuildStage(status, counts, active));
                }

                AttentionItems.Clear();
                var attention = await _orders.ListAttentionAsync(businessId, take: 5);
                foreach (var order in attention)
                {
                    var fg = order.IsOverdue
                        ? OrderStatusPresentation.OverdueForeground
                        : OrderStatusPresentation.Foreground(order.Status);
                    var bg = order.IsOverdue
                        ? OrderStatusPresentation.OverdueBackground
                        : OrderStatusPresentation.Background(order.Status);
                    var item = new HomeAttentionItem
                    {
                        OrderId = order.Id,
                        Initials = BuildInitials(order.CustomerName, null, null),
                        Name = order.CustomerName,
                        Detail = $"#{order.DisplayRef} · {order.DueDateDisplay}",
                        StatusText = order.IsOverdue ? "Overdue" : order.StatusDisplay,
                        IsReady = order.Status == Domain.Enums.OrderStatus.Ready && !order.IsOverdue,
                        IsOverdue = order.IsOverdue,
                        StatusIconImage = order.IsOverdue
                            ? OrderStatusPresentation.OverdueIconImage(14)
                            : OrderStatusPresentation.IconImage(order.Status, 14),
                        StatusBackground = bg,
                        StatusForeground = fg
                    };
                    item.OpenCommand = new RelayCommand(() => _ = OpenAttentionAsync(item));
                    AttentionItems.Add(item);
                }

                OnPropertyChanged(nameof(HasAttentionItems));
                OnPropertyChanged(nameof(ShowNoAttention));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Home load failed: {ex.Message}");
        }
    }

    /// <summary>Active production stages shown on Home (colours from OrderStatusPresentation).</summary>
    private static readonly string[] HomeStageStatuses =
    [
        Domain.Enums.OrderStatus.New,
        Domain.Enums.OrderStatus.Cutting,
        Domain.Enums.OrderStatus.Sewing,
        Domain.Enums.OrderStatus.Fitting,
        Domain.Enums.OrderStatus.Ready
    ];

    // Short label fits the stage row; chips elsewhere use "Ready to collect".
    private static string StageLabel(string status) => status;

    private static HomeStageItem BuildStage(string status, OrderCountsDto counts, int active)
    {
        counts.ByStatus.TryGetValue(status, out var count);
        return new HomeStageItem(StageLabel(status), OrderStatusPresentation.Accent(status))
        {
            Count = count,
            Fraction = (double)count / active
        };
    }

    private static string BuildInitials(string? first, string? surname, string? business)
    {
        var parts = new[] { first, surname }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.Trim()[0].ToString().ToUpperInvariant())
            .ToList();
        if (parts.Count > 0) return string.Concat(parts.Take(2));
        if (!string.IsNullOrWhiteSpace(business))
        {
            var words = business.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return string.Concat(words.Take(2).Select(w => char.ToUpperInvariant(w[0])));
        }
        return "NB";
    }

    [RelayCommand]
    private async Task AddCustomerAsync() => await Shell.Current.GoToAsync("CustomerEditPage");

    [RelayCommand]
    private async Task NewOrderAsync() => await Shell.Current.GoToAsync(nameof(Views.CreateOrderPage));

    [RelayCommand]
    private async Task RecordPaymentAsync() => await Shell.Current.GoToAsync("//main/PaymentsPage");

    [RelayCommand]
    private async Task MeasureAsync() => await Shell.Current.GoToAsync("//main/CustomersPage");

    [RelayCommand]
    private async Task ViewAllOrdersAsync() => await Shell.Current.GoToAsync("//main/OrdersPage");

    // Bound per row via HomeAttentionItem.OpenCommand (no separate page-level command).
    private static async Task OpenAttentionAsync(HomeAttentionItem? item)
    {
        if (item is null || string.IsNullOrWhiteSpace(item.OrderId))
        {
            return;
        }

        await Shell.Current.GoToAsync(
            $"{nameof(Views.OrderDetailPage)}?orderId={Uri.EscapeDataString(item.OrderId)}");
    }

    [RelayCommand]
    private async Task GoCustomersAsync() => await Shell.Current.GoToAsync("//main/CustomersPage");
}
