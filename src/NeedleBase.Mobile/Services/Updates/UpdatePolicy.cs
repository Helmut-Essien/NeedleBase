namespace NeedleBase.Mobile.Services.Updates;

public enum UpdateKind
{
    /// <summary>Up to date, or the user chose "Later" for this version.</summary>
    None,
    /// <summary>Newer version; the user may postpone it.</summary>
    Optional,
    /// <summary>This version is below the manifest's minSupportedVersionCode: the app asks until it is updated.</summary>
    Required
}

/// <summary>Pure decisions (unit-tested): what to offer, and when to ask the network again.</summary>
public static class UpdatePolicy
{
    /// <summary>Automatic checks at most this often; "Check for updates" always checks.</summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    public static UpdateKind Evaluate(long currentVersionCode, UpdateManifest? manifest, int? skippedVersionCode, bool manual)
    {
        if (manifest is null || manifest.VersionCode <= currentVersionCode)
        {
            return UpdateKind.None;
        }

        if (currentVersionCode < manifest.MinSupportedVersionCode)
        {
            return UpdateKind.Required;
        }

        return !manual && skippedVersionCode == manifest.VersionCode ? UpdateKind.None : UpdateKind.Optional;
    }

    /// <summary>True when an automatic check is due. A last-check time in the future (clock changed) counts as due.</summary>
    public static bool IsCheckDue(DateTimeOffset? lastSuccessfulCheck, DateTimeOffset now, bool manual) =>
        manual || lastSuccessfulCheck is not { } last || last > now || now - last >= CheckInterval;
}
