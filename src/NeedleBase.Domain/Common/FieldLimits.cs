namespace NeedleBase.Domain.Common;

/// <summary>
/// Shared max lengths — keep Domain, EF, DTOs, and MAUI validation aligned.
/// </summary>
public static class FieldLimits
{
    public const int Id = 26;
    public const int CustomerName = 200;
    public const int Phone = 40;
    public const int Notes = 2000;
    public const int PhotoRef = 500;
    public const int GarmentTemplate = 40;
    public const int MeasurementUnit = 20;
    public const int FieldsJson = 8000;
    public const int OrderStatus = 20;
    public const int OrderDisplayRef = 8;
    public const int AssignedToUserId = 450;
    public const int OrderLineQuantityMax = 99;
    public const int PaymentMethod = 16;
    public const int SyncEntityType = 20;
    public const int SyncOperation = 10;
    public const int SyncPushBatchMax = 200;
    public const int SyncPullLimitMax = 500;
}
