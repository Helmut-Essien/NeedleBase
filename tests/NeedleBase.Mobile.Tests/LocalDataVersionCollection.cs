namespace NeedleBase.Mobile.Tests;

/// <summary>
/// <see cref="NeedleBase.Mobile.Data.Data.LocalDataVersion"/> is process-wide: every local save in ANY test bumps its
/// counter and a sync pull raises its ChangedBySync event. Tests that assert "no re-query" or count live reloads
/// therefore run alone (xUnit runs this collection after the parallel ones, with nothing beside it); otherwise a
/// parallel class's write lands between two steps on a slow CI runner and the count is off by one.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LocalDataVersionCollection
{
    public const string Name = "LocalDataVersion";
}
