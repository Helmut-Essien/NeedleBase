namespace NeedleBase.Api.Tests;

/// <summary>
/// One sync test re-stamps every synced row in the background (epoch bump). Classes that page through sync
/// share this collection so they run one after another and a bump never lands in the middle of another paging test.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SyncEpochCollection
{
    public const string Name = "SyncEpoch";
}
