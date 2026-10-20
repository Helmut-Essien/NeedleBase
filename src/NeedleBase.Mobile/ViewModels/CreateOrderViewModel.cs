using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Customers;
using NeedleBase.Application.Contracts.Measurements;
using NeedleBase.Application.Contracts.Orders;
using NeedleBase.Domain.Common;
using NeedleBase.Domain.Enums;
using NeedleBase.Mobile.Data.Services;
using NeedleBase.Mobile.Helpers;
using NeedleBase.Mobile.Services;

namespace NeedleBase.Mobile.ViewModels;

public sealed class CreateOrderLineDraft : ObservableObject
{
    public string GarmentTemplate { get; init; } = string.Empty;
    public string GarmentDisplay { get; init; } = string.Empty;
    public int Quantity { get; init; } = 1;
    public decimal UnitPrice { get; init; }
    public string? MeasurementProfileId { get; init; }
    public string Unit { get; init; } = MeasurementUnit.Centimetres;
    public Dictionary<string, decimal?> MeasurementFields { get; init; } = new();
    public string SnapshotLabel { get; init; } = "No measurements snapped";
    public string LineSummary => $"{Quantity}× {GarmentDisplay} · GH₵ {UnitPrice.ToString("N2", CultureInfo.GetCultureInfo("en-GH"))}";
    public IRelayCommand? RemoveCommand { get; set; }
}

public partial class CreateOrderViewModel : BaseViewModel, IQueryAttributable
{
    private readonly IOrderLocalService _orders;
    private readonly ICustomerLocalService _customers;
    private readonly IMeasurementLocalService _measurements;
    private readonly IAuthService _auth;
    private bool _initialized;
    private string? _clientOrderId;
    private string? _previousCustomerId;

    public CreateOrderViewModel(
        IOrderLocalService orders,
        ICustomerLocalService customers,
        IMeasurementLocalService measurements,
        IAuthService auth)
    {
        _orders = orders;
        _customers = customers;
        _measurements = measurements;
        _auth = auth;
        Title = "New order";
        foreach (var template in GarmentTemplate.All)
        {
            TemplateChoices.Add(new TemplateChoice(template, GarmentTemplate.DisplayName(template)));
        }

        SelectedTemplate = TemplateChoices[0];
    }

    // TemplateChoice lives in MeasurementEditViewModel.cs (shared picker model).

    /// <summary>Picker rows: recent customers when the query is empty, else matches (max <see cref="PickerLimit"/>).</summary>
    public ObservableCollection<CustomerPickRow> CustomerResults { get; } = new();

    private const int PickerLimit = 30;
    private readonly SearchDebouncer _customerSearch = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCustomerQuery))]
    private string customerQuery = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoMatches))]
    [NotifyPropertyChangedFor(nameof(ShowResults))]
    private bool hasCustomerResults;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoMatches))]
    private bool isSearchingCustomers;

    [ObservableProperty]
    private string customerResultsHeader = "Recent customers";

    [ObservableProperty]
    private CustomerPickRow? selectedPickRow;

    public bool HasCustomerQuery => !string.IsNullOrWhiteSpace(CustomerQuery);
    public bool ShowCustomerPicker => SelectedCustomer is null;
    public bool ShowResults => HasCustomerResults;
    public bool ShowNoMatches => HasCustomerQuery && !HasCustomerResults && !IsSearchingCustomers;
    public string NoMatchesText => $"No customers match \"{CustomerQuery.Trim()}\"";
    public string SelectedCustomerName => SelectedCustomer?.Name ?? string.Empty;
    public string SelectedCustomerInitials => SelectedCustomer?.Initials ?? string.Empty;

    partial void OnCustomerQueryChanged(string value)
    {
        OnPropertyChanged(nameof(NoMatchesText));
        OnPropertyChanged(nameof(ShowNoMatches));
        _ = _customerSearch.Run(SearchCustomersAsync);
    }

    partial void OnSelectedPickRowChanged(CustomerPickRow? value)
    {
        if (value is null)
        {
            return;
        }

        SelectedCustomer = value.Customer;
        SelectedPickRow = null;
    }

    [RelayCommand]
    private void ClearCustomerQuery() => CustomerQuery = string.Empty;

    [RelayCommand]
    private void ChangeCustomer()
    {
        SelectedCustomer = null;
        CustomerQuery = string.Empty;
        _ = _customerSearch.Run(SearchCustomersAsync);
    }

    /// <summary>Empty state action: create the customer, then come back with them selected.</summary>
    [RelayCommand]
    private async Task AddNewCustomerAsync()
    {
        var query = CustomerQuery.Trim();
        var key = query.Any(char.IsLetter) ? "name" : "phone";
        await Shell.Current.GoToAsync(
            "CustomerEditPage",
            new Dictionary<string, object> { [key] = query, ["returnSelects"] = "true" });
    }

    private async Task SearchCustomersAsync(CancellationToken cancellationToken)
    {
        var businessId = await _auth.GetBusinessIdAsync();
        if (string.IsNullOrWhiteSpace(businessId) || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        var text = CustomerQuery;
        var query = CustomerSearchKeys.Parse(text);
        IsSearchingCustomers = true;
        try
        {
            var list = query.IsEmpty
                ? await _customers.RecentAsync(businessId, 8, cancellationToken)
                : await _customers.SearchAsync(businessId, text, PickerLimit, cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            CustomerResults.Clear();
            foreach (var customer in list)
            {
                CustomerResults.Add(new CustomerPickRow(customer, query));
            }

            CustomerResultsHeader = query.IsEmpty ? "Recent customers" : "Matching customers";
            HasCustomerResults = CustomerResults.Count > 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Superseded by a newer keystroke.
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Customer search failed: {ex}");
            ErrorMessage = "Couldn't search customers. Try again.";
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                IsSearchingCustomers = false;
            }
        }
    }
    public ObservableCollection<MeasurementListItemDto> Measurements { get; } = new();
    public ObservableCollection<CreateOrderLineDraft> Lines { get; } = new();
    public ObservableCollection<TemplateChoice> TemplateChoices { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStepCustomer))]
    [NotifyPropertyChangedFor(nameof(IsStepGarment))]
    [NotifyPropertyChangedFor(nameof(IsStepDetails))]
    [NotifyPropertyChangedFor(nameof(ShowBackStep))]
    [NotifyPropertyChangedFor(nameof(StepLabel))]
    [NotifyPropertyChangedFor(nameof(PrimaryButtonText))]
    private int step;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedCustomer))]
    [NotifyPropertyChangedFor(nameof(SelectedCustomerPhone))]
    [NotifyPropertyChangedFor(nameof(SelectedCustomerName))]
    [NotifyPropertyChangedFor(nameof(SelectedCustomerInitials))]
    [NotifyPropertyChangedFor(nameof(ShowCustomerPicker))]
    private CustomerListItemDto? selectedCustomer;

    [ObservableProperty]
    private MeasurementListItemDto? selectedMeasurement;

    [ObservableProperty]
    private TemplateChoice? selectedTemplate;

    [ObservableProperty]
    private string quantityText = "1";

    [ObservableProperty]
    private string unitPriceText = "0";

    [ObservableProperty]
    private string totalAmountText = "0";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DueDateWarning), nameof(HasDueDateWarning))]
    private DateTime dueDate = DateTime.Today.AddDays(7);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DueDateWarning), nameof(HasDueDateWarning))]
    private bool hasDueDate = true;

    /// <summary>Past due dates are allowed (back-entering an order) but flagged, not blocked.</summary>
    public string? DueDateWarning =>
        HasDueDate && DateOnly.FromDateTime(DueDate.Date) < BusinessTime.Today(DateTimeOffset.UtcNow)
            ? "Due date is in the past."
            : null;

    public bool HasDueDateWarning => DueDateWarning is not null;

    [ObservableProperty]
    private string notes = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? errorMessage;

    [ObservableProperty]
    private bool isSaving;

    public bool CanGoBack => !IsSaving;
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool HasLines => Lines.Count > 0;
    public bool HasSelectedCustomer => SelectedCustomer is not null;
    public string SelectedCustomerPhone => CustomerSearchKeys.FormatPhone(SelectedCustomer?.Phone);
    public bool IsStepCustomer => Step == 0;
    public bool IsStepGarment => Step == 1;
    public bool IsStepDetails => Step == 2;
    public bool ShowBackStep => Step > 0;
    public string StepLabel => Step switch
    {
        0 => "1 of 3 · Customer",
        1 => "2 of 3 · Garments",
        _ => "3 of 3 · Price & due date"
    };
    public string PrimaryButtonText => Step == 2 ? "Create order" : "Continue";

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        // Returning from "Add new customer": select the customer just created.
        if (query.TryGetValue("createdCustomerId", out var created) && created is not null)
        {
            var createdId = created.ToString() ?? string.Empty;
            query.Clear();
            MainThread.BeginInvokeOnMainThread(async () => await SelectCustomerByIdAsync(createdId));
            return;
        }

        if (query.TryGetValue("customerId", out var value) && value is not null)
        {
            PrefillCustomerId = Uri.UnescapeDataString(value.ToString() ?? string.Empty);
        }

        // Prefer this path so customerId is applied before _initialized latches.
        MainThread.BeginInvokeOnMainThread(async () => await InitializeAsync());
    }

    public string PrefillCustomerId { get; private set; } = string.Empty;

    /// <summary>
    /// OnAppearing fallback when opened with no query. Yields so ApplyQueryAttributes can win.
    /// </summary>
    public async Task InitializeIfPendingAsync()
    {
        await Task.Yield();
        if (_initialized)
        {
            return;
        }

        await InitializeAsync();
    }

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        _clientOrderId ??= IdGenerator.NewId();
        await LoadCustomersAsync();
    }

    protected override void OnBusyChanged() => OnPropertyChanged(nameof(CanGoBack));

    partial void OnIsSavingChanged(bool value) => OnPropertyChanged(nameof(CanGoBack));

    partial void OnSelectedCustomerChanged(CustomerListItemDto? value)
    {
        if (value is null)
        {
            return;
        }

        if (_previousCustomerId is not null &&
            !string.Equals(_previousCustomerId, value.Id, StringComparison.Ordinal) &&
            Lines.Count > 0)
        {
            Lines.Clear();
            OnPropertyChanged(nameof(HasLines));
            RefreshTotal();
        }

        _previousCustomerId = value.Id;
        MainThread.BeginInvokeOnMainThread(async () => await LoadMeasurementsAsync());
    }

    [RelayCommand]
    private async Task LoadCustomersAsync()
    {
        if (IsBusy)
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

            if (!string.IsNullOrWhiteSpace(PrefillCustomerId) &&
                await SelectCustomerByIdAsync(PrefillCustomerId))
            {
                return;
            }

            await SearchCustomersAsync(CancellationToken.None);
        }
        catch (Exception)
        {
            ErrorMessage = "Couldn't load customers.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<bool> SelectCustomerByIdAsync(string customerId)
    {
        var businessId = await _auth.GetBusinessIdAsync();
        if (string.IsNullOrWhiteSpace(businessId) || string.IsNullOrWhiteSpace(customerId))
        {
            return false;
        }

        var detail = await _customers.GetByIdAsync(businessId, customerId);
        if (detail is null)
        {
            return false;
        }

        SelectedCustomer = new CustomerListItemDto(detail.Id, detail.Name, detail.Phone, detail.DateModifiedUtc);
        return true;
    }

    private async Task LoadMeasurementsAsync()
    {
        Measurements.Clear();
        SelectedMeasurement = null;
        if (SelectedCustomer is null)
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

            var list = await _measurements.ListForCustomerAsync(businessId, SelectedCustomer.Id);
            foreach (var m in list)
            {
                Measurements.Add(m);
            }
        }
        catch (Exception)
        {
            ErrorMessage = "Couldn't load measurements for this customer.";
        }
    }

    [RelayCommand]
    private async Task PrimaryAsync()
    {
        if (IsBusy || IsSaving)
        {
            return;
        }

        ErrorMessage = null;
        if (Step == 0)
        {
            if (SelectedCustomer is null)
            {
                ErrorMessage = "Choose a customer.";
                return;
            }

            Step = 1;
            return;
        }

        if (Step == 1)
        {
            if (Lines.Count == 0)
            {
                ErrorMessage = "Add at least one garment line.";
                return;
            }

            RefreshTotal();
            Step = 2;
            return;
        }

        await SaveAsync();
    }

    [RelayCommand]
    private void AddLine()
    {
        ErrorMessage = null;
        if (SelectedTemplate is null)
        {
            ErrorMessage = "Choose a garment template.";
            return;
        }

        if (!int.TryParse(QuantityText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var qty) ||
            qty < 1 || qty > FieldLimits.OrderLineQuantityMax)
        {
            ErrorMessage = $"Quantity must be between 1 and {FieldLimits.OrderLineQuantityMax}.";
            return;
        }

        if (!DecimalInput.TryParse(UnitPriceText, out var price))
        {
            ErrorMessage = "Enter a valid unit price. " + DecimalInput.Hint;
            return;
        }

        if (AmountRules.CheckUnitPrice(price) is { } priceError)
        {
            ErrorMessage = priceError;
            return;
        }

        var fields = new Dictionary<string, decimal?>();
        string? profileId = null;
        var snapshotLabel = "No measurements snapped";
        var template = SelectedTemplate.Key;
        var unit = MeasurementUnit.Centimetres;

        if (SelectedMeasurement is not null)
        {
            // Snapshot applied in SaveAsync from full profile; store id + template from list for now.
            profileId = SelectedMeasurement.Id;
            template = SelectedMeasurement.GarmentTemplate;
            unit = SelectedMeasurement.Unit;
            snapshotLabel =
                $"{GarmentTemplate.DisplayName(SelectedMeasurement.GarmentTemplate)} · {SelectedMeasurement.MeasuredAtUtc:dd MMM yyyy}";
        }

        var draft = new CreateOrderLineDraft
        {
            GarmentTemplate = template,
            GarmentDisplay = GarmentTemplate.DisplayName(template),
            Quantity = qty,
            UnitPrice = decimal.Round(price, 2, MidpointRounding.AwayFromZero),
            MeasurementProfileId = profileId,
            Unit = unit,
            MeasurementFields = fields,
            SnapshotLabel = snapshotLabel
        };
        draft.RemoveCommand = new RelayCommand(() => RemoveLine(draft));
        Lines.Add(draft);
        OnPropertyChanged(nameof(HasLines));

        RefreshTotal();
        QuantityText = "1";
        UnitPriceText = "0";
    }

    [RelayCommand]
    private void RemoveLine(CreateOrderLineDraft? line)
    {
        if (line is null)
        {
            return;
        }

        Lines.Remove(line);
        OnPropertyChanged(nameof(HasLines));
        RefreshTotal();
    }

    /// <summary>Total is always the sum of lines (the server/local service recompute and check it).</summary>
    private decimal ComputedTotal =>
        decimal.Round(Lines.Sum(l => l.UnitPrice * l.Quantity), 2, MidpointRounding.AwayFromZero);

    private void RefreshTotal() =>
        TotalAmountText = $"GH₵ {ComputedTotal.ToString("N2", CultureInfo.GetCultureInfo("en-GH"))}";

    [RelayCommand]
    private void BackStep()
    {
        if (IsSaving)
        {
            return;
        }

        if (Step > 0)
        {
            Step--;
            ErrorMessage = null;
        }
    }

    [RelayCommand]
    private async Task GoBackAsync()
    {
        if (IsSaving)
        {
            return;
        }

        await Shell.Current.GoToAsync("..");
    }

    private async Task SaveAsync()
    {
        if (Notes.Length > FieldLimits.Notes)
        {
            ErrorMessage = $"Notes must be at most {FieldLimits.Notes} characters.";
            return;
        }

        if (SelectedCustomer is null || Lines.Count == 0)
        {
            ErrorMessage = "Order is incomplete.";
            return;
        }

        IsBusy = true;
        IsSaving = true;
        try
        {
            var businessId = await _auth.GetBusinessIdAsync();
            if (string.IsNullOrWhiteSpace(businessId))
            {
                ErrorMessage = "Missing business session.";
                return;
            }

            // Snapshot is applied in OrderLocalService when MeasurementProfileId is set.
            var lineRequests = Lines.Select(draft => new CreateOrderLineRequest
            {
                GarmentTemplate = draft.GarmentTemplate,
                Quantity = draft.Quantity,
                UnitPrice = draft.UnitPrice,
                MeasurementProfileId = draft.MeasurementProfileId,
                Unit = draft.Unit,
                MeasurementFields = draft.MeasurementFields,
                Notes = null
            }).ToList();

            _clientOrderId ??= IdGenerator.NewId();
            var request = new CreateOrderRequest
            {
                Id = _clientOrderId,
                CustomerId = SelectedCustomer.Id,
                DueDate = HasDueDate ? DateOnly.FromDateTime(DueDate.Date) : null,
                TotalAmount = ComputedTotal,
                Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim(),
                Lines = lineRequests
            };

            var (order, error) = await _orders.SaveAsync(businessId, request);
            if (error is not null || order is null)
            {
                ErrorMessage = error ?? "Couldn't save the order.";
                return;
            }

            await Toast.Make("Saved on this device", ToastDuration.Short, 14).Show();
            // Replace wizard with detail on the same nav stack.
            await Shell.Current.GoToAsync(
                $"../{nameof(Views.OrderDetailPage)}?orderId={Uri.EscapeDataString(order.Id)}");
        }
        catch (Exception)
        {
            ErrorMessage = "Couldn't save the order. Try again.";
        }
        finally
        {
            IsBusy = false;
            IsSaving = false;
        }
    }
}
