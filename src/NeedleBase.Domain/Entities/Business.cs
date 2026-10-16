namespace NeedleBase.Domain.Entities;

/// <summary>Workspace / shop. Tenant root — other entities reference <see cref="Id"/> as BusinessId.</summary>
public class Business : Entity
{
    public string Name { get; private set; } = string.Empty;
    public string? Phone { get; private set; }
    public string CurrencyCode { get; private set; } = "GHS";
    public string MeasurementUnit { get; private set; } = "cm";
    public DateTimeOffset CreatedAtUtc { get; private set; }

    private Business()
    {
    }

    public static Business Create(string id, string name, string? phone, DateTimeOffset createdAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new Business
        {
            Id = id,
            Name = name.Trim(),
            Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim(),
            CurrencyCode = "GHS",
            MeasurementUnit = "cm",
            CreatedAtUtc = createdAtUtc
        };
    }
}
