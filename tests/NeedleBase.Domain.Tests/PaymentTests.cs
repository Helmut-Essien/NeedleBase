using NeedleBase.Domain.Entities;
using NeedleBase.Domain.Enums;
using NeedleBase.Domain.Exceptions;

namespace NeedleBase.Domain.Tests;

public class PaymentTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 9, 0, 0, TimeSpan.Zero);

    private static Payment Create(decimal amount, string method = PaymentMethod.Cash, string? note = null) =>
        Payment.Create("P1", "B1", "O1", "C1", amount, method, note, Now, Now);

    [Theory]
    [InlineData(0.01)]
    [InlineData(200)]
    [InlineData(1250.5)]
    [InlineData(50000000)]
    public void Create_AcceptsCediAmounts(double amount) => Assert.Equal((decimal)amount, Create((decimal)amount).Amount);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(0.001)]
    [InlineData(10.005)]
    [InlineData(50000000.01)]
    public void Create_RejectsBadAmounts_NeverRounds(double amount)
    {
        var ex = Assert.Throws<DomainRuleException>(() => Create((decimal)amount));
        Assert.Equal(DomainErrorCodes.PaymentInvalid, ex.Code);
    }

    [Fact]
    public void Create_RejectsUnknownMethod_TrimsNote()
    {
        Assert.Throws<DomainRuleException>(() => Create(10m, "Card"));
        Assert.Null(Create(10m, note: "   ").Note);
        Assert.Equal("ref 1", Create(10m, note: " ref 1 ").Note);
        Assert.Throws<ArgumentException>(() => Create(10m, note: new string('x', 2001)));
    }

    [Fact]
    public void Void_IsOneWay_AndIdempotent()
    {
        var p = Create(100m);
        p.Void("mistake", Now.AddMinutes(1));
        p.Void("other", Now.AddMinutes(2));
        Assert.True(p.IsVoided);
        Assert.Equal("mistake", p.VoidReason);
        Assert.Equal(Now.AddMinutes(1), p.VoidedAtUtc);
        Assert.Equal(Now.AddMinutes(1), p.DateModifiedUtc);
    }

    [Fact]
    public void Methods_AreTheFourConstants_WithFriendlyNames()
    {
        Assert.Equal(["Cash", "MoMo", "Bank", "Other"], PaymentMethod.All);
        Assert.Equal("Mobile Money", PaymentMethod.DisplayName(PaymentMethod.MoMo));
        Assert.Equal("payment", SyncEntityType.Payment);
        Assert.Contains(SyncEntityType.Payment, SyncEntityType.All);
    }
}
