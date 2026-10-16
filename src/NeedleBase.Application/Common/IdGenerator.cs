using NUlid;

namespace NeedleBase.Application.Common;

/// <summary>
/// Creates ULID string IDs for domain entities and client sync keys (AssetTag pattern).
/// </summary>
public static class IdGenerator
{
    public static string NewId() => Ulid.NewUlid().ToString();
}
