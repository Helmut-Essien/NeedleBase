namespace NeedleBase.Mobile.Data.Services;

/// <summary>Builds a "Save a copy" JSON payload for Free (phone only) shops — no cloud backup.</summary>
public interface IFreeCopyService
{
    /// <summary>JSON snapshot of this Free shop's records, or null on failure.</summary>
    Task<string?> BuildCopyJsonAsync(string businessId, string shopName, CancellationToken cancellationToken = default);
}
