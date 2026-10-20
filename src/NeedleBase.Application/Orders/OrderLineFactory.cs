using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Orders;
using NeedleBase.Domain.Entities;

namespace NeedleBase.Application.Orders;

/// <summary>
/// Builds order lines from a request (shared by the API service and the mobile local service).
/// Line ids are always generated here; profile-backed lines snapshot the profile's fields.
/// </summary>
public static class OrderLineFactory
{
    /// <summary>
    /// Returns null lines + <see cref="OrderError.MeasurementNotFound"/> if a profile id isn't this customer's and the
    /// request carries no snapshot. A line that already carries its measurement values (e.g. synced from a device after
    /// the profile was deleted) keeps them and drops the link: the snapshot, not the profile, is the order's record.
    /// Throws <see cref="ArgumentException"/> for prices outside <see cref="AmountRules"/> or too many lines.
    /// </summary>
    public static (List<OrderLine>? Lines, OrderError Error) Build(
        string businessId,
        string orderId,
        IReadOnlyList<CreateOrderLineRequest> requests,
        IReadOnlyDictionary<string, MeasurementProfile> profilesById)
    {
        if (requests.Count > AmountRules.OrderLinesMax)
        {
            throw new ArgumentException($"An order can have at most {AmountRules.OrderLinesMax} lines.", nameof(requests));
        }

        var lines = new List<OrderLine>(requests.Count);
        for (var i = 0; i < requests.Count; i++)
        {
            var req = requests[i];
            if (AmountRules.CheckUnitPrice(req.UnitPrice) is { } priceError)
            {
                throw new ArgumentException(priceError, nameof(requests));
            }

            var garmentTemplate = req.GarmentTemplate;
            var unit = req.Unit;
            var fieldsJson = MeasurementFieldsSerializer.Serialize(req.MeasurementFields ?? new Dictionary<string, decimal?>());
            string? profileId = string.IsNullOrWhiteSpace(req.MeasurementProfileId) ? null : req.MeasurementProfileId.Trim();

            if (profileId is not null)
            {
                if (profilesById.TryGetValue(profileId, out var profile))
                {
                    garmentTemplate = profile.GarmentTemplate;
                    unit = profile.Unit;
                    fieldsJson = profile.FieldsJson;
                }
                else if (req.MeasurementFields is { Count: > 0 })
                {
                    profileId = null; // profile gone: keep the line's own snapshot
                }
                else
                {
                    return (null, OrderError.MeasurementNotFound);
                }
            }

            lines.Add(OrderLine.Create(
                IdGenerator.NewId(),
                businessId,
                orderId,
                garmentTemplate,
                req.Quantity,
                req.UnitPrice,
                profileId,
                unit,
                fieldsJson,
                req.Notes,
                i));
        }

        return (lines, OrderError.None);
    }

    public static IReadOnlyList<string> ProfileIds(IReadOnlyList<CreateOrderLineRequest> requests) =>
        requests
            .Select(l => l.MeasurementProfileId)
            .Where(pid => !string.IsNullOrWhiteSpace(pid))
            .Select(pid => pid!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>True when the client sent a total that disagrees with the computed one.</summary>
    public static bool TotalMismatch(decimal? clientTotal, decimal computed) =>
        clientTotal is { } t && decimal.Round(t, 2, MidpointRounding.AwayFromZero) != computed;

    /// <summary>Maps a domain/argument failure to a typed error (detail is for logs, not UI).</summary>
    public static OrderError FromException(ArgumentException ex) =>
        ex is Domain.Exceptions.DomainRuleException rule
            ? rule.Code switch
            {
                Domain.Exceptions.DomainErrorCodes.OrderLocked => OrderError.Locked,
                Domain.Exceptions.DomainErrorCodes.OrderStatusInvalid => OrderError.StatusInvalid,
                _ => OrderError.Invalid
            }
            : OrderError.Invalid;
}
