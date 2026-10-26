using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Orders;
using NeedleBase.Application.Orders;
using NeedleBase.Domain.Common;
using NeedleBase.Domain.Enums;
using NeedleBase.Mobile.Data.Services;
using NeedleBase.Mobile.Services;

namespace NeedleBase.Mobile.ViewModels;

/// <summary>One editable garment line. Existing lines keep their garment and measurement snapshot.</summary>
public partial class EditOrderLineRow : ObservableObject
{
    public EditOrderLineRow(string? id, string garmentTemplate, int quantity, decimal unitPrice, string? notes, string measurementNote,
        string? measurementProfileId = null, string unit = MeasurementUnit.Centimetres)
    {
        Id = id;
        GarmentTemplate = garmentTemplate;
        GarmentDisplay = Domain.Enums.GarmentTemplate.DisplayName(garmentTemplate);
        quantityText = quantity.ToString(CultureInfo.InvariantCulture);
        priceText = unitPrice.ToString("0.00", CultureInfo.InvariantCulture);
        this.notes = notes ?? string.Empty;
        MeasurementNote = measurementNote;
        MeasurementProfileId = measurementProfileId;
        Unit = unit;
        Quantity = quantity;
        UnitPrice = unitPrice;
    }

    public string? Id { get; }
    public string GarmentTemplate { get; }
    public string GarmentDisplay { get; }
    public string MeasurementNote { get; }
    public string? MeasurementProfileId { get; }
    public string Unit { get; }
    public bool IsNew => Id is null;

    [ObservableProperty] private string quantityText;
    [ObservableProperty] private string priceText;
    [ObservableProperty] private string notes;

    public string QuantityDescription => $"Quantity of {GarmentDisplay}";
    public string PriceDescription => $"Price for one {GarmentDisplay} in Ghana cedis";
    public string RemoveDescription => $"Remove {GarmentDisplay}";

    // Parsed once per keystroke in the changed field, not on every total recalculation.
    public int? Quantity { get; private set; }

    public decimal? UnitPrice { get; private set; }

    partial void OnQuantityTextChanged(string value)
    {
        Quantity = ParseQuantity(value);
        OnPropertyChanged(nameof(Quantity));
    }

    partial void OnPriceTextChanged(string value)
    {
        UnitPrice = EditOrderViewModel.ParseMoney(value);
        OnPropertyChanged(nameof(UnitPrice));
    }

    private static int? ParseQuantity(string? text) =>
        int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var q) ? q : null;
}

/// <summary>
/// Owner/Manager edit of an order (due date, notes, garment lines incl. price). Saved offline and queued for sync;
/// the same rules as the server (<see cref="OrderEditor"/>) are checked in <see cref="IOrderLocalService.UpdateAsync"/>.
/// </summary>
public partial class EditOrderViewModel : BaseViewModel, IQueryAttributable
{
    private static readonly CultureInfo Ghana = CultureInfo.GetCultureInfo("en-GH");
    private readonly IOrderLocalService _orders;
    private readonly IAuthService _auth;
    private OrderDetailDto? _order;
    private bool _loadedOnce;
    private bool _suspendTotal;

    public EditOrderViewModel(IOrderLocalService orders, IAuthService auth)
    {
        _orders = orders;
        _auth = auth;
        Title = "Edit order";
        foreach (var template in Domain.Enums.GarmentTemplate.All)
        {
            TemplateChoices.Add(new TemplateChoice(template, Domain.Enums.GarmentTemplate.DisplayName(template)));
        }

        SelectedTemplate = TemplateChoices.FirstOrDefault();
        Lines.CollectionChanged += (_, _) => RecalculateTotal();
    }

    public string OrderId { get; private set; } = string.Empty;
    public ObservableCollection<EditOrderLineRow> Lines { get; } = new();
    public ObservableCollection<TemplateChoice> TemplateChoices { get; } = new();

    [ObservableProperty] private string heading = "Edit order";
    [ObservableProperty] private bool isEditable;
    [ObservableProperty] private bool hasDueDate;
    [ObservableProperty] private DateTime dueDate = DateTime.Today;
    [ObservableProperty] private string notes = string.Empty;
    [ObservableProperty] private TemplateChoice? selectedTemplate;
    [ObservableProperty] private string totalText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? errorMessage;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    // Screen-reader users don't see the red text appear at the top of a long form: read it out.
    partial void OnErrorMessageChanged(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        try
        {
            SemanticScreenReader.Default.Announce(value);
        }
        catch (Exception)
        {
            // No screen reader platform (tests) - the label still shows the message.
        }
    }

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
    public async Task LoadAsync()
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
            var user = await _auth.GetCachedUserAsync();
            if (string.IsNullOrWhiteSpace(businessId))
            {
                ErrorMessage = "Missing business session.";
                IsEditable = false;
                return;
            }

            if (!OrderEditor.CanEdit(user?.Role))
            {
                ErrorMessage = NeedleBase.Application.Contracts.Errors.OrderErrorMessages.For(
                    NeedleBase.Application.Contracts.Errors.OrderErrorMessages.EditForbidden);
                IsEditable = false;
                return;
            }

            var detail = await _orders.GetByIdAsync(businessId, OrderId);
            if (detail is null)
            {
                ErrorMessage = "This order isn't available on this device.";
                IsEditable = false;
                return;
            }

            Apply(detail);
        }
        catch (Exception)
        {
            ErrorMessage = "Couldn't load this order. Go back and try again.";
            IsEditable = false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Apply(OrderDetailDto detail)
    {
        _order = detail;
        Heading = $"Edit order #{detail.DisplayRef}";
        IsEditable = !OrderStatus.IsTerminal(detail.Status);
        if (!IsEditable)
        {
            ErrorMessage = NeedleBase.Application.Contracts.Errors.OrderErrorMessages.For(
                NeedleBase.Application.Contracts.Errors.OrderErrorMessages.OrderLocked);
        }

        HasDueDate = detail.DueDate is not null;
        DueDate = detail.DueDate?.ToDateTime(TimeOnly.MinValue) ?? DateTime.Today;
        Notes = detail.Notes ?? string.Empty;
        foreach (var row in Lines)
        {
            row.PropertyChanged -= OnRowChanged;
        }

        // One total recalculation for the whole reload, not one per row (50-line orders on low-end phones).
        _suspendTotal = true;
        try
        {
            Lines.Clear();
            foreach (var line in detail.Lines.OrderBy(l => l.SortOrder))
            {
                AddRow(new EditOrderLineRow(line.Id, line.GarmentTemplate, line.Quantity, line.UnitPrice, line.Notes,
                    line.HasMeasurements ? $"Measurements kept · {line.MeasurementTakenDisplay}" : "No measurements saved with this order"));
            }
        }
        finally
        {
            _suspendTotal = false;
        }

        RecalculateTotal();
    }

    private void AddRow(EditOrderLineRow row)
    {
        row.PropertyChanged += OnRowChanged;
        Lines.Add(row);
    }

    // Only quantity/price affect the total; notes typing doesn't trigger a recalculation.
    private void OnRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EditOrderLineRow.Quantity) or nameof(EditOrderLineRow.UnitPrice))
        {
            RecalculateTotal();
        }
    }

    private void RecalculateTotal()
    {
        if (_suspendTotal)
        {
            return;
        }

        decimal total = 0;
        foreach (var row in Lines)
        {
            if (row.Quantity is not { } q || row.UnitPrice is not { } p)
            {
                TotalText = "Total: check quantities and prices";
                return;
            }

            total += decimal.Round(decimal.Round(p, 2, MidpointRounding.AwayFromZero) * q, 2, MidpointRounding.AwayFromZero);
        }

        TotalText = $"New total: GH₵ {total.ToString("N2", Ghana)}";
    }

    [RelayCommand]
    private void AddLine()
    {
        if (!IsEditable || SelectedTemplate is null)
        {
            return;
        }

        if (Lines.Count >= AmountRules.OrderLinesMax)
        {
            ErrorMessage = $"An order can have at most {AmountRules.OrderLinesMax} garments.";
            return;
        }

        AddRow(new EditOrderLineRow(null, SelectedTemplate.Key, 1, 0m, null,
            "New: uses the customer's latest saved measurements for this garment, if any"));
    }

    [RelayCommand]
    private void RemoveLine(EditOrderLineRow? row)
    {
        if (row is null || !IsEditable)
        {
            return;
        }

        if (Lines.Count <= 1)
        {
            ErrorMessage = "An order needs at least one garment. Add another before removing this one.";
            return;
        }

        row.PropertyChanged -= OnRowChanged;
        Lines.Remove(row);
    }

    /// <summary>Validates the form; returns the request or sets <see cref="ErrorMessage"/>.</summary>
    public UpdateOrderRequest? BuildRequest()
    {
        if (_order is null)
        {
            return null;
        }

        if (Lines.Count == 0)
        {
            ErrorMessage = "An order needs at least one garment.";
            return null;
        }

        var lines = new List<CreateOrderLineRequest>();
        for (var i = 0; i < Lines.Count; i++)
        {
            var row = Lines[i];
            var label = $"Garment {i + 1} ({row.GarmentDisplay})";
            if (row.Quantity is not { } q || q < 1 || q > FieldLimits.OrderLineQuantityMax)
            {
                ErrorMessage = $"{label}: quantity must be a whole number from 1 to {FieldLimits.OrderLineQuantityMax}.";
                return null;
            }

            if (row.UnitPrice is not { } p)
            {
                ErrorMessage = $"{label}: enter a price, like 150.00.";
                return null;
            }

            if (AmountRules.CheckUnitPrice(p) is { } priceError)
            {
                ErrorMessage = $"{label}: {priceError}";
                return null;
            }

            if (row.Notes?.Length > FieldLimits.Notes)
            {
                ErrorMessage = $"{label}: notes are too long.";
                return null;
            }

            lines.Add(new CreateOrderLineRequest
            {
                Id = row.Id,
                GarmentTemplate = row.GarmentTemplate,
                Quantity = q,
                UnitPrice = p,
                Unit = row.Unit,
                MeasurementProfileId = row.MeasurementProfileId,
                Notes = string.IsNullOrWhiteSpace(row.Notes) ? null : row.Notes.Trim()
            });
        }

        if (Notes.Length > FieldLimits.Notes)
        {
            ErrorMessage = "Order notes are too long.";
            return null;
        }

        return new UpdateOrderRequest
        {
            BaseModifiedUtc = _order.DateModifiedUtc,
            DueDate = HasDueDate ? DateOnly.FromDateTime(DueDate) : null,
            FittingDate = _order.FittingDate,
            AssignedToUserId = _order.AssignedToUserId,
            Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim(),
            Lines = lines
        };
    }

    [RelayCommand]
    public async Task SaveAsync()
    {
        if (IsBusy || !IsEditable)
        {
            return;
        }

        ErrorMessage = null;
        var request = BuildRequest();
        if (request is null)
        {
            return;
        }

        IsBusy = true;
        var saved = false;
        try
        {
            var businessId = await _auth.GetBusinessIdAsync();
            var user = await _auth.GetCachedUserAsync();
            if (string.IsNullOrWhiteSpace(businessId))
            {
                ErrorMessage = "Missing business session.";
                return;
            }

            var result = await _orders.UpdateAsync(businessId, OrderId, request, user?.Role);
            if (!result.IsSuccess)
            {
                ErrorMessage = result.Error == RecordError.Stale
                    ? "This order changed on this device since you opened it. The latest version is shown; make your change again."
                    : result.Message ?? "Couldn't save the order. Try again.";
                if (result.Error == RecordError.Stale && await _orders.GetByIdAsync(businessId, OrderId) is { } fresh)
                {
                    var message = ErrorMessage;
                    Apply(fresh);
                    // Collected/Cancelled meanwhile (e.g. arrived by sync): the lock message from Apply is the real reason.
                    if (IsEditable)
                    {
                        ErrorMessage = message;
                    }
                }

                return;
            }

            saved = true;
        }
        catch (Exception)
        {
            ErrorMessage = "Couldn't save the order. Try again.";
        }
        finally
        {
            IsBusy = false;
        }

        if (saved)
        {
            try
            {
                await Toast.Make("Saved on this device", ToastDuration.Short, 14).Show();
            }
            catch (Exception)
            {
                // Toast is cosmetic.
            }

            await GoBackAsync();
        }
    }

    [RelayCommand]
    private async Task GoBackAsync()
    {
        if (Shell.Current is not null)
        {
            await Shell.Current.GoToAsync("..");
        }
    }

    /// <summary>Same cedi input rules as Record payment: "1,250.50", "GH₵ 150", "10,50" (= 10.50); "1.000" and minus refused.</summary>
    public static decimal? ParseMoney(string? text) =>
        RecordPaymentViewModel.TryParseAmount(text, out var value) ? value : null;
}
