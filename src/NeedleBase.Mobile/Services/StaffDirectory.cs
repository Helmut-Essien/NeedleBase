using System.Text.Json;
using NeedleBase.Application.Contracts.Staff;

namespace NeedleBase.Mobile.Services;

/// <summary>Active team members' names (any signed-in member may read them). Online only.</summary>
public interface IStaffNamesApi
{
    Task<StaffCallResult<IReadOnlyList<StaffNameDto>>> GetNamesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// The shop's active team, cached on the device so the "Assigned to" name and the assign picker work offline
/// (code-gaps G14). Refreshed whenever an order screen opens online; cleared on sign-out.
/// </summary>
public interface IStaffDirectory
{
    /// <summary>The last list saved for this shop (empty when none or another shop's).</summary>
    Task<IReadOnlyList<StaffNameDto>> GetCachedAsync(string businessId);

    /// <summary>Fetch and save the latest list; offline or on error, the cached list.</summary>
    Task<IReadOnlyList<StaffNameDto>> RefreshAsync(string businessId, CancellationToken cancellationToken = default);
}

public sealed class StaffDirectory : IStaffDirectory
{
    public const string StorageKey = "staff_names";

    private readonly IStaffNamesApi _api;
    private readonly ISecureStorageService _storage;

    public StaffDirectory(IStaffNamesApi api, ISecureStorageService storage)
    {
        _api = api;
        _storage = storage;
    }

    public async Task<IReadOnlyList<StaffNameDto>> GetCachedAsync(string businessId)
    {
        try
        {
            var json = await _storage.GetAsync(StorageKey);
            if (string.IsNullOrWhiteSpace(json))
            {
                return Array.Empty<StaffNameDto>();
            }

            var saved = JsonSerializer.Deserialize<Saved>(json);
            return saved is not null && saved.BusinessId == businessId && saved.Staff is not null
                ? saved.Staff
                : Array.Empty<StaffNameDto>();
        }
        catch (Exception)
        {
            return Array.Empty<StaffNameDto>(); // unreadable cache: treat as empty
        }
    }

    public async Task<IReadOnlyList<StaffNameDto>> RefreshAsync(string businessId, CancellationToken cancellationToken = default)
    {
        var result = await _api.GetNamesAsync(cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            return await GetCachedAsync(businessId);
        }

        try
        {
            await _storage.SetAsync(StorageKey, JsonSerializer.Serialize(new Saved(businessId, result.Value.ToList())));
        }
        catch (Exception)
        {
            // cache is best effort; the fresh list is still shown
        }

        return result.Value;
    }

    private sealed record Saved(string BusinessId, List<StaffNameDto>? Staff);
}
