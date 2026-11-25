namespace NeedleBase.Mobile.Tests;

/// <summary>
/// Free tests share the process-wide <see cref="NeedleBase.Mobile.Data.Sync.LocalFreeModeGate"/>.
/// Run them one at a time so one test's Clear cannot un-gate another.
/// </summary>
[CollectionDefinition("LocalFreeModeGate", DisableParallelization = true)]
public sealed class LocalFreeModeGateCollection;
