using NeedleBase.Domain.Common;

namespace NeedleBase.Domain.Entities;

/// <summary>Client of a Business workspace.</summary>
public class Customer : Entity, IBusinessScoped
{
    public string BusinessId { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Phone { get; private set; } = string.Empty;
    public string? Notes { get; private set; }
    public string? PhotoRef { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset DateModifiedUtc { get; private set; }

    private Customer()
    {
    }

    public static Customer Create(
        string id,
        string businessId,
        string name,
        string phone,
        string? notes,
        string? photoRef,
        DateTimeOffset utcNow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(businessId);

        var customer = new Customer
        {
            Id = id,
            BusinessId = businessId,
            CreatedAtUtc = utcNow,
            DateModifiedUtc = utcNow
        };
        customer.ApplyDetails(name, phone, notes, photoRef, utcNow);
        return customer;
    }

    public void Update(string name, string phone, string? notes, string? photoRef, DateTimeOffset utcNow)
    {
        ApplyDetails(name, phone, notes, photoRef, utcNow);
    }

    private void ApplyDetails(string name, string phone, string? notes, string? photoRef, DateTimeOffset utcNow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(phone);

        name = name.Trim();
        phone = PhoneNumbers.Normalize(phone);
        if (name.Length > FieldLimits.CustomerName)
        {
            throw new ArgumentException($"Name must be at most {FieldLimits.CustomerName} characters.", nameof(name));
        }

        if (phone.Length > FieldLimits.Phone)
        {
            throw new ArgumentException($"Phone must be at most {FieldLimits.Phone} characters.", nameof(phone));
        }

        notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        if (notes is { Length: > FieldLimits.Notes })
        {
            throw new ArgumentException($"Notes must be at most {FieldLimits.Notes} characters.", nameof(notes));
        }

        photoRef = string.IsNullOrWhiteSpace(photoRef) ? null : photoRef.Trim();
        if (photoRef is { Length: > FieldLimits.PhotoRef })
        {
            throw new ArgumentException($"Photo reference must be at most {FieldLimits.PhotoRef} characters.", nameof(photoRef));
        }

        Name = name;
        Phone = phone;
        Notes = notes;
        PhotoRef = photoRef;
        DateModifiedUtc = utcNow;
    }
}
