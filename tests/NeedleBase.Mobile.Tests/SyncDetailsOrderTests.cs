using NeedleBase.Domain.Enums;
using NeedleBase.Mobile.Data.Data;
using NeedleBase.Application.Contracts.Sync;
using NeedleBase.Mobile.Data.Sync;
using NeedleBase.Mobile.ViewModels;
using Xunit;

namespace NeedleBase.Mobile.Tests;

public class SyncDetailsOrderTests
{
    private static SyncQueueEntry E(string id, string state) =>
        new(id, SyncEntityType.Order, id, "Changed", state, null, null, false);

    [Fact]
    public void NeedsAttentionRowsComeFirstAndPushOrderIsKept()
    {
        var ordered = SyncStatusViewModel.Ordered(new[]
        {
            E("w1", SyncQueueState.Pending), E("f1", SyncQueueState.Failed), E("w2", SyncQueueState.Pending),
            E("c1", SyncQueueState.Conflict), E("f2", SyncQueueState.Failed)
        });

        Assert.Equal(new[] { "c1", "f1", "f2", "w1", "w2" }, ordered.Select(e => e.Id));
    }

    [Fact]
    public void EmptyQueueStaysEmpty() => Assert.Empty(SyncStatusViewModel.Ordered(Array.Empty<SyncQueueEntry>()));

    [Fact]
    public void PaymentMismatchCopyReadsPlainly()
    {
        var row = new SyncItemRow(new SyncQueueEntry("p", SyncEntityType.Payment, "x", "New", SyncQueueState.Conflict, null, SyncErrorCodes.PaymentMismatch, false));
        Assert.Contains("Keep both keeps them as two payments", row.Subtitle);
    }
}
