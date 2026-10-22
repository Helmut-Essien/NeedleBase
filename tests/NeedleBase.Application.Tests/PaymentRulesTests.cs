using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Errors;
using NeedleBase.Application.Contracts.Payments;
using NeedleBase.Application.Payments;
using NeedleBase.Application.Sync;
using NeedleBase.Domain.Enums;

namespace NeedleBase.Application.Tests;

public class PaymentRulesTests
{
    [Fact]
    public void Paid_ExcludesVoided_AndIsExact()
    {
        var payments = Enumerable.Repeat((0.1m, false), 10).Append((500m, true));
        Assert.Equal(1.0m, PaymentRules.Paid(payments));
    }

    [Theory]
    [InlineData(OrderStatus.New, 800, 200, 600, RecordError.None)]
    [InlineData(OrderStatus.New, 800, 200, 600.01, RecordError.PaymentExceedsBalance)]
    [InlineData(OrderStatus.Collected, 800, 200, 100, RecordError.None)]
    [InlineData(OrderStatus.Collected, 800, 800, 1, RecordError.PaymentExceedsBalance)]
    [InlineData(OrderStatus.Cancelled, 800, 0, 100, RecordError.PaymentOrderLocked)]
    [InlineData(OrderStatus.New, 800, 0, 0, RecordError.PaymentInvalid)]
    [InlineData(OrderStatus.New, 800, 0, 1.234, RecordError.PaymentInvalid)]
    public void CheckEntry(string status, double total, double paid, double amount, RecordError expected) =>
        Assert.Equal(expected, PaymentRules.CheckEntry(status, (decimal)total, (decimal)paid, (decimal)amount, PaymentMethod.Cash, lenient: false).Error);

    [Fact]
    public void CheckEntry_Lenient_StoresRealOfflineMoney_ButStillValidatesAmountAndMethod()
    {
        Assert.Equal(RecordError.None, PaymentRules.CheckEntry(OrderStatus.Cancelled, 100m, 100m, 50m, PaymentMethod.MoMo, lenient: true).Error);
        Assert.Equal(RecordError.PaymentInvalid, PaymentRules.CheckEntry(OrderStatus.New, 100m, 0m, 1.001m, PaymentMethod.MoMo, lenient: true).Error);
        Assert.Equal(RecordError.PaymentInvalid, PaymentRules.CheckEntry(OrderStatus.New, 100m, 0m, 1m, "Card", lenient: true).Error);
    }

    [Theory]
    [InlineData(BusinessRole.Owner, true)]
    [InlineData(BusinessRole.Manager, true)]
    [InlineData(BusinessRole.Tailor, false)]
    [InlineData(null, false)]
    public void OnlyOwnerAndManager_Manage(string? role, bool expected) => Assert.Equal(expected, PaymentRules.CanManage(role));

    [Fact]
    public void OrderPayments_Copy_UsesGhanaCediFormat()
    {
        var dto = new OrderPaymentsDto("O1", OrderStatus.New, 1250m, 200m, []);
        Assert.Equal("GH₵ 200.00 of GH₵ 1,250.00 paid", dto.PaidSummary);
        Assert.Equal("GH₵ 1,050.00 left", dto.BalanceDisplay);
        Assert.Equal("Paid in full", (dto with { PaidAmount = 1250m }).BalanceDisplay);
        Assert.Equal("GH₵ 50.00 overpaid", (dto with { PaidAmount = 1300m }).BalanceDisplay);
    }

    [Theory]
    [InlineData(RecordError.PaymentNotFound, "PAYMENT_NOT_FOUND")]
    [InlineData(RecordError.PaymentConflict, "PAYMENT_CONFLICT")]
    [InlineData(RecordError.PaymentInvalid, "PAYMENT_INVALID")]
    [InlineData(RecordError.PaymentExceedsBalance, "PAYMENT_EXCEEDS_BALANCE")]
    [InlineData(RecordError.PaymentOrderLocked, "PAYMENT_ORDER_LOCKED")]
    [InlineData(RecordError.PaymentForbidden, "PAYMENT_FORBIDDEN")]
    public void Codes_AreStable_AndHaveCopy(RecordError error, string code)
    {
        Assert.Equal(code, RecordErrorMessages.Code(error));
        Assert.False(string.IsNullOrWhiteSpace(RecordErrorMessages.For(error)));
        Assert.DoesNotContain("Exception", RecordErrorMessages.For(error));
    }

    [Fact]
    public void Cursor_V2_AcceptsPaymentsPhase_AndCountsItsPosition()
    {
        var cursor = new SyncCursor { Phase = SyncCursor.PhasePayments, Cap = 10, IssuedTicks = 1, Payments = new SyncPosition(42, "P1") };
        Assert.True(SyncCursor.TryDecode(cursor.Encode(), out var decoded));
        Assert.Equal(SyncCursor.PhasePayments, decoded.Phase);
        Assert.Equal(42, decoded.HighestPosition);

        var beyond = new SyncCursor { Phase = SyncCursor.PhaseLast + 1, Cap = 10, IssuedTicks = 1 };
        Assert.False(SyncCursor.TryDecode(beyond.Encode(), out _));
    }
}
