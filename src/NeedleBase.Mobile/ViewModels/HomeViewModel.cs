using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NeedleBase.Mobile.Data.Services;
using NeedleBase.Mobile.Services;

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
    public required string Initials { get; init; }
    public required string Name { get; init; }
    public required string Detail { get; init; }
    public required string StatusText { get; init; }
    public bool IsReady { get; init; }
}

public partial class HomeViewModel : BaseViewModel
{
    private readonly IAuthService _auth;
    private readonly ICustomerLocalService _customers;

    public HomeViewModel(IAuthService auth, ICustomerLocalService customers)
    {
        _auth = auth;
        _customers = customers;
        Title = "Home";

        Stages.Add(new HomeStageItem("New", Color.FromArgb("#2E9E36")));
        Stages.Add(new HomeStageItem("Cutting", Color.FromArgb("#2563EB")));
        Stages.Add(new HomeStageItem("Sewing", Color.FromArgb("#D97706")));
        Stages.Add(new HomeStageItem("Fitting", Color.FromArgb("#7C3AED")));
        Stages.Add(new HomeStageItem("Ready", Color.FromArgb("#2E9E36")));
    }

    // Kept for compatibility with the previous Home screen.
    public string Tagline => "You're signed in. Add customers and measurements from the Customers tab.";

    [ObservableProperty] private string greeting = "Good morning";
    [ObservableProperty] private string businessName = "NeedleBase";
    [ObservableProperty] private string userInitials = "NB";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCustomersSavedText))]
    private string customersSavedText = string.Empty;

    // Orders/Payments are placeholder slices today: these stay at zero / empty
    // until a real order service exists. Do not seed them with sample data.
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
                    1 => "1 customer saved on this phone",
                    >= 50 => "50+ customers saved on this phone",
                    var n => $"{n} customers saved on this phone"
                };
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Home load failed: {ex.Message}");
        }
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
    private async Task NewOrderAsync() => await Shell.Current.GoToAsync("//main/OrdersPage");

    [RelayCommand]
    private async Task RecordPaymentAsync() => await Shell.Current.GoToAsync("//main/PaymentsPage");

    [RelayCommand]
    private async Task MeasureAsync() => await Shell.Current.GoToAsync("//main/CustomersPage");

    [RelayCommand]
    private async Task ViewAllOrdersAsync() => await Shell.Current.GoToAsync("//main/OrdersPage");

    [RelayCommand]
    private async Task GoCustomersAsync() => await Shell.Current.GoToAsync("//main/CustomersPage");
}
