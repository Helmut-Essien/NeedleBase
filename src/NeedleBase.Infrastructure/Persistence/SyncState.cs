namespace NeedleBase.Infrastructure.Persistence;

/// <summary>
/// Single row (Id = 1) guarding commit-ordered sync against a database moved to a new cluster
/// (pg_dump restore, host move): transaction ids restart lower there, so device cursors would sit ahead of
/// every new row. <see cref="HighWaterTx"/> is the highest watermark ever issued; seeing a lower one means
/// the cluster changed, so <see cref="Epoch"/> is bumped, rows are re-stamped and old cursors are refused.
/// </summary>
public sealed class SyncState
{
    public int Id { get; set; }
    public long Epoch { get; set; }
    public long HighWaterTx { get; set; }
}
