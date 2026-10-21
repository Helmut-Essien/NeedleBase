using System.ComponentModel.DataAnnotations;
using NeedleBase.Domain.Common;

namespace NeedleBase.Application.Contracts.Customers;

public sealed record CustomerListItemDto(
    string Id,
    string Name,
    string Phone,
    DateTimeOffset DateModifiedUtc)
{
    public string Initials
    {
        get
        {
            var parts = Name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
            {
                return "?";
            }

            if (parts.Length == 1)
            {
                return parts[0][..1].ToUpperInvariant();
            }

            return string.Concat(parts[0].AsSpan(0, 1), parts[^1].AsSpan(0, 1)).ToUpperInvariant();
        }
    }
}

public sealed record CustomerDetailDto(
    string Id,
    string BusinessId,
    string Name,
    string Phone,
    string? Notes,
    string? PhotoRef,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset DateModifiedUtc,
    int MeasurementCount);

public sealed class CreateCustomerRequest
{
    /// <summary>
    /// Set only by sync push (never bound from JSON): skip input rules added after data may already have been
    /// queued on devices (amount/measurement limits, phone format), so legacy data never fails permanently.
    /// Domain invariants still apply.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Lenient { get; set; }

    public CreateCustomerRequest()
    {
    }

    public CreateCustomerRequest(string name, string phone, string? notes, string? photoRef, string? id = null)
    {
        Id = id;
        Name = name;
        Phone = phone;
        Notes = notes;
        PhotoRef = photoRef;
    }

    /// <summary>Optional client ULID for offline idempotency.</summary>
    [MaxLength(FieldLimits.Id)]
    public string? Id { get; set; }

    [Required, MaxLength(FieldLimits.CustomerName)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(FieldLimits.Phone)]
    public string Phone { get; set; } = string.Empty;

    [MaxLength(FieldLimits.Notes)]
    public string? Notes { get; set; }

    [MaxLength(FieldLimits.PhotoRef)]
    public string? PhotoRef { get; set; }
}

public sealed class UpdateCustomerRequest
{
    /// <summary>
    /// Set only by sync push (never bound from JSON): skip input rules added after data may already have been
    /// queued on devices (amount/measurement limits, phone format), so legacy data never fails permanently.
    /// Domain invariants still apply.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Lenient { get; set; }

    /// <summary>Optional optimistic concurrency: the DateModifiedUtc the client last saw. Stale → 409 STALE_UPDATE.</summary>
    public DateTimeOffset? BaseModifiedUtc { get; set; }

    public UpdateCustomerRequest()
    {
    }

    public UpdateCustomerRequest(string name, string phone, string? notes, string? photoRef)
    {
        Name = name;
        Phone = phone;
        Notes = notes;
        PhotoRef = photoRef;
    }

    [Required, MaxLength(FieldLimits.CustomerName)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(FieldLimits.Phone)]
    public string Phone { get; set; } = string.Empty;

    [MaxLength(FieldLimits.Notes)]
    public string? Notes { get; set; }

    [MaxLength(FieldLimits.PhotoRef)]
    public string? PhotoRef { get; set; }
}
