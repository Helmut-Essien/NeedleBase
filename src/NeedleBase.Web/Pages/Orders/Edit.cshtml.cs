using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Errors;
using NeedleBase.Application.Contracts.Orders;
using NeedleBase.Application.Contracts.Payments;
using NeedleBase.Domain.Common;
using NeedleBase.Domain.Enums;
using NeedleBase.Web.Services;

namespace NeedleBase.Web.Pages.Orders;

/// <summary>
/// Owner/Manager order edit: due date, notes, garment lines (quantity, price, notes, add/remove). Existing lines keep
/// their measurement snapshot; a new garment snapshots the customer's latest measurements for it (server rule).
/// The Api enforces role, the collected/cancelled lock, "total not below paid" and STALE_UPDATE.
/// </summary>
public class EditModel : AppPageModel
{
    public const string StaleMessage = "Someone changed this order while you were editing. The latest version is shown below. Make your changes again.";

    private readonly INeedleBaseApi _api;

    public EditModel(INeedleBaseApi api)
    {
        _api = api;
    }

    [BindProperty(SupportsGet = true)] public string Id { get; set; } = string.Empty;

    [BindProperty, MaxLength(64)] public string? BaseModified { get; set; }

    [BindProperty, DataType(DataType.Date)] public DateOnly? DueDate { get; set; }

    [BindProperty, MaxLength(FieldLimits.Notes, ErrorMessage = "Notes are too long.")] public string? Notes { get; set; }

    [BindProperty] public List<EditLineInput> Lines { get; set; } = [];

    [BindProperty, MaxLength(FieldLimits.GarmentTemplate)] public string? NewGarment { get; set; }
    [BindProperty] public int? NewQuantity { get; set; } = 1;
    [BindProperty, MaxLength(20)] public string? NewPrice { get; set; }
    [BindProperty, MaxLength(FieldLimits.Notes, ErrorMessage = "Notes are too long.")] public string? NewNotes { get; set; }

    public OrderDetailDto? Order { get; private set; }
    public decimal? Paid { get; private set; }
    public string? FormError { get; private set; }
    public bool Locked => Order is not null && OrderStatus.IsTerminal(Order.Status);

    public sealed class EditLineInput
    {
        [MaxLength(FieldLimits.Id)] public string Id { get; set; } = string.Empty;
        public int? Quantity { get; set; }
        [MaxLength(20)] public string? Price { get; set; }
        [MaxLength(FieldLimits.Notes, ErrorMessage = "Notes are too long.")] public string? Notes { get; set; }
        public bool Remove { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!User.CanEditOrders())
        {
            return Forbidden();
        }

        var failed = await LoadAsync(cancellationToken);
        if (failed is not null || Order is null)
        {
            return failed ?? Page();
        }

        FillFrom(Order);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!User.CanEditOrders())
        {
            return Forbidden();
        }

        var failed = await LoadAsync(cancellationToken);
        if (failed is not null || Order is null)
        {
            return failed ?? Page();
        }

        if (Locked)
        {
            return Page();
        }

        var request = BuildRequest(Order);
        if (request is null)
        {
            return Page();
        }

        var result = await _api.UpdateOrderAsync(Id, request, cancellationToken);
        if (result.IsSuccess)
        {
            TempData["Flash"] = "Order updated.";
            return RedirectToPage("/Orders/Details", new { id = Id });
        }

        if (result.Status == System.Net.HttpStatusCode.Unauthorized)
        {
            return Fail(result)!;
        }

        if (result.Code == StaleUpdate.Code)
        {
            ModelState.Clear();
            FillFrom(Order); // the freshly loaded version
            FormError = StaleMessage;
            return Page();
        }

        FormError = DetailsModel.StatusErrorFor(result);
        return Page();
    }

    /// <summary>Validates the form against the loaded order; sets <see cref="FormError"/> and returns null when invalid.</summary>
    public UpdateOrderRequest? BuildRequest(OrderDetailDto order)
    {
        if (!ModelState.IsValid)
        {
            FormError = "Check the highlighted details and try again.";
            return null;
        }

        var existing = order.Lines.ToDictionary(l => l.Id, StringComparer.Ordinal);
        var lines = new List<CreateOrderLineRequest>();
        var errors = new List<string>();
        foreach (var (input, index) in Lines.Select((l, i) => (l, i)))
        {
            if (!existing.TryGetValue(input.Id ?? string.Empty, out var line))
            {
                continue; // a line removed elsewhere: the stale check reports it
            }

            if (input.Remove)
            {
                continue;
            }

            var label = $"{line.GarmentDisplay} (garment {index + 1})";
            var price = ParseMoney(input.Price);
            if (input.Quantity is not { } q || q < 1 || q > FieldLimits.OrderLineQuantityMax)
            {
                errors.Add($"{label}: quantity must be from 1 to {FieldLimits.OrderLineQuantityMax}.");
                continue;
            }

            if (price is null)
            {
                errors.Add($"{label}: enter a price, like 150.00.");
                continue;
            }

            if (AmountRules.CheckUnitPrice(price.Value) is { } priceError)
            {
                errors.Add($"{label}: {priceError}");
                continue;
            }

            lines.Add(new CreateOrderLineRequest
            {
                Id = line.Id,
                GarmentTemplate = line.GarmentTemplate,
                Quantity = q,
                UnitPrice = price.Value,
                Unit = line.Unit,
                MeasurementProfileId = line.MeasurementProfileId,
                Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim()
            });
        }

        if (!string.IsNullOrWhiteSpace(NewGarment))
        {
            var garment = NewGarment.Trim().ToLowerInvariant();
            var price = ParseMoney(NewPrice);
            if (!GarmentTemplate.IsValid(garment))
            {
                errors.Add("New garment: choose a garment type.");
            }
            else if (NewQuantity is not { } q || q < 1 || q > FieldLimits.OrderLineQuantityMax)
            {
                errors.Add($"New garment: quantity must be from 1 to {FieldLimits.OrderLineQuantityMax}.");
            }
            else if (price is null)
            {
                errors.Add("New garment: enter a price, like 150.00.");
            }
            else if (AmountRules.CheckUnitPrice(price.Value) is { } priceError)
            {
                errors.Add($"New garment: {priceError}");
            }
            else
            {
                // No profile id and no values: the Api snapshots the customer's latest measurements for this garment.
                lines.Add(new CreateOrderLineRequest
                {
                    GarmentTemplate = garment,
                    Quantity = q,
                    UnitPrice = price.Value,
                    Notes = string.IsNullOrWhiteSpace(NewNotes) ? null : NewNotes.Trim()
                });
            }
        }

        if (errors.Count == 0 && lines.Count == 0)
        {
            errors.Add("An order needs at least one garment. Keep one, or add a new garment before removing the last.");
        }

        if (errors.Count == 0 && lines.Count > AmountRules.OrderLinesMax)
        {
            errors.Add($"An order can have at most {AmountRules.OrderLinesMax} garments.");
        }

        if (errors.Count > 0)
        {
            FormError = string.Join(" ", errors);
            return null;
        }

        DateTimeOffset? baseModified = DateTimeOffset.TryParse(BaseModified, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
        return new UpdateOrderRequest
        {
            BaseModifiedUtc = baseModified,
            DueDate = DueDate,
            FittingDate = order.FittingDate,
            AssignedToUserId = order.AssignedToUserId,
            Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim(),
            Lines = lines
        };
    }

    /// <summary>Same cedi rules as the phone: "1,250.50", "GH₵ 150", "10,50" (= 10.50); "1.000" and minus refused.</summary>
    public static decimal? ParseMoney(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var cleaned = text;
        foreach (var token in new[] { "GH₵", "GH¢", "GHS", "GHC" })
        {
            cleaned = cleaned.Replace(token, string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        cleaned = cleaned.Replace(" ", string.Empty).Replace("\u00A0", string.Empty).Trim();
        if (cleaned.Length == 0 || cleaned.Contains('-'))
        {
            return null;
        }

        if (GhanaGrouped.IsMatch(cleaned))
        {
            return decimal.TryParse(cleaned.Replace(",", string.Empty), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var grouped)
                ? grouped
                : null;
        }

        return DecimalInput.TryParse(cleaned, out var value) ? value : null;
    }

    private static readonly System.Text.RegularExpressions.Regex GhanaGrouped =
        new(@"^\d{1,3}(,\d{3})+(\.\d{1,2})?$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private void FillFrom(OrderDetailDto order)
    {
        BaseModified = order.DateModifiedUtc.ToString("o", CultureInfo.InvariantCulture);
        DueDate = order.DueDate;
        Notes = order.Notes;
        Lines = order.Lines.OrderBy(l => l.SortOrder).Select(l => new EditLineInput
        {
            Id = l.Id,
            Quantity = l.Quantity,
            Price = l.UnitPrice.ToString("0.00", CultureInfo.InvariantCulture),
            Notes = l.Notes
        }).ToList();
        NewGarment = null;
        NewQuantity = 1;
        NewPrice = null;
        NewNotes = null;
    }

    private IActionResult Forbidden()
    {
        PageError = OrderErrorMessages.For(OrderErrorMessages.EditForbidden);
        CanRetry = false;
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return Page();
    }

    private async Task<IActionResult?> LoadAsync(CancellationToken cancellationToken)
    {
        var orderTask = _api.GetOrderAsync(Id, cancellationToken);
        var paymentsTask = _api.GetOrderPaymentsAsync(Id, cancellationToken);
        await Task.WhenAll(orderTask, paymentsTask);

        var order = await orderTask;
        if (!order.IsSuccess)
        {
            if (order.Status == System.Net.HttpStatusCode.NotFound)
            {
                PageError = "This order wasn't found. It may have been deleted, or it belongs to another business.";
                CanRetry = false;
                return null;
            }

            return Fail(order);
        }

        Order = order.Value;
        if ((await paymentsTask) is { IsSuccess: true } payments)
        {
            Paid = payments.Value!.PaidAmount;
        }

        Loaded = true;
        return null;
    }

    /// <summary>Line row for the view (input + the frozen line it edits).</summary>
    public IEnumerable<(EditLineInput Input, OrderLineDto Line, int Index)> Rows()
    {
        if (Order is null)
        {
            yield break;
        }

        var byId = Order.Lines.ToDictionary(l => l.Id, StringComparer.Ordinal);
        for (var i = 0; i < Lines.Count; i++)
        {
            if (byId.TryGetValue(Lines[i].Id ?? string.Empty, out var line))
            {
                yield return (Lines[i], line, i);
            }
        }
    }

    public static string PaidText(decimal paid) => Money.Format(paid);
}
