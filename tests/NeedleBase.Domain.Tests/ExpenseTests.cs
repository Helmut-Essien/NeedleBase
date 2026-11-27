using NeedleBase.Domain.Common;
using NeedleBase.Domain.Entities;
using NeedleBase.Domain.Enums;
using NeedleBase.Domain.Exceptions;

namespace NeedleBase.Domain.Tests;

public class ExpenseTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 27);

    private static Expense Create(
        decimal amount = 120m,
        string category = ExpenseCategory.Materials,
        DateOnly? spentOn = null,
        string? note = null,
        string? orderId = null,
        string? recordedBy = "U1") =>
        Expense.Create("E1", "B1", amount, category, spentOn ?? Today, note, orderId, recordedBy, Today, Now);

    [Fact]
    public void Create_SetsFields()
    {
        var e = Create(note: "Makola lining", orderId: " O1 ");
        Assert.Equal("E1", e.Id);
        Assert.Equal("B1", e.BusinessId);
        Assert.Equal(120m, e.Amount);
        Assert.Equal(ExpenseCategory.Materials, e.Category);
        Assert.Equal(Today, e.SpentOn);
        Assert.Equal("Makola lining", e.Note);
        Assert.Equal("O1", e.OrderId);
        Assert.Equal("U1", e.RecordedByUserId);
        Assert.False(e.IsVoided);
        Assert.Null(e.VoidedAtUtc);
        Assert.Equal(Now, e.CreatedAtUtc);
        Assert.Equal(Now, e.DateModifiedUtc);
    }

    [Theory]
    [InlineData(0.01)]
    [InlineData(120)]
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
        Assert.Equal(DomainErrorCodes.ExpenseInvalid, ex.Code);
    }

    [Theory]
    [InlineData("Card")]
    [InlineData("")]
    [InlineData("materials")]
    public void Create_RejectsUnknownCategory(string category)
    {
        var ex = Assert.Throws<DomainRuleException>(() => Create(category: category));
        Assert.Equal(DomainErrorCodes.ExpenseInvalid, ex.Code);
    }

    [Fact]
    public void Create_TrimsCategory() => Assert.Equal(ExpenseCategory.StaffPay, Create(category: " StaffPay ").Category);

    [Fact]
    public void Create_DateRange_2020ToToday()
    {
        Assert.Equal(new DateOnly(2020, 1, 1), Create(spentOn: new DateOnly(2020, 1, 1)).SpentOn);
        Assert.Equal(Today, Create(spentOn: Today).SpentOn);
        var future = Assert.Throws<DomainRuleException>(() => Create(spentOn: Today.AddDays(1)));
        Assert.Equal("The date can't be in the future.", future.Message);
        var early = Assert.Throws<DomainRuleException>(() => Create(spentOn: new DateOnly(2019, 12, 31)));
        Assert.Equal(DomainErrorCodes.ExpenseInvalid, early.Code);
    }

    [Fact]
    public void CheckSpentOn_NullWhenValid()
    {
        Assert.Null(Expense.CheckSpentOn(Today, Today));
        Assert.NotNull(Expense.CheckSpentOn(Today.AddDays(1), Today));
        Assert.NotNull(Expense.CheckSpentOn(new DateOnly(2019, 12, 31), Today));
    }

    [Fact]
    public void Create_CleansNote()
    {
        Assert.Null(Create(note: "   ").Note);
        Assert.Null(Create(note: "\u0001\u0002").Note);
        Assert.Equal("Makola lining", Create(note: " Makola\u0007 lining ").Note);
        Assert.Equal("line 1\nline 2", Create(note: "line 1\nline 2").Note);
        Assert.Equal(2000, Create(note: new string('x', 2000)).Note!.Length);
        var ex = Assert.Throws<DomainRuleException>(() => Create(note: new string('x', 2001)));
        Assert.Equal(DomainErrorCodes.ExpenseInvalid, ex.Code);
    }

    [Fact]
    public void Create_OrderLinkOptional_AndBounded()
    {
        Assert.Null(Create(orderId: null).OrderId);
        Assert.Null(Create(orderId: "  ").OrderId);
        Assert.Throws<DomainRuleException>(() => Create(orderId: new string('O', 27)));
    }

    [Fact]
    public void Create_RecordedByOptional_AndBounded()
    {
        Assert.Null(Create(recordedBy: " ").RecordedByUserId);
        Assert.Throws<ArgumentException>(() => Create(recordedBy: new string('u', 451)));
    }

    [Fact]
    public void Create_RequiresIds()
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            Expense.Create(" ", "B1", 1m, ExpenseCategory.Other, Today, null, null, null, Today, Now));
        Assert.ThrowsAny<ArgumentException>(() =>
            Expense.Create("E1", "", 1m, ExpenseCategory.Other, Today, null, null, null, Today, Now));
    }

    [Fact]
    public void Void_IsOneWay_AndFirstReasonWins()
    {
        var e = Create();
        e.Void(" wrong amount ", "U2", Now.AddMinutes(1));
        e.Void("other", "U3", Now.AddMinutes(2));
        Assert.True(e.IsVoided);
        Assert.Equal("wrong amount", e.VoidReason);
        Assert.Equal("U2", e.VoidedByUserId);
        Assert.Equal(Now.AddMinutes(1), e.VoidedAtUtc);
        Assert.Equal(Now.AddMinutes(1), e.DateModifiedUtc);
    }

    [Fact]
    public void Void_AllowsBlankReason_OnTheDomain()
    {
        var e = Create();
        e.Void(null, null, Now);
        Assert.True(e.IsVoided);
        Assert.Null(e.VoidReason);
    }

    [Fact]
    public void DetachOrder_DropsLink_AndNotesIt_Once()
    {
        var e = Create(note: "lining", orderId: "O1");
        e.DetachOrder(Now.AddMinutes(1));
        Assert.Null(e.OrderId);
        Assert.Equal("lining [order removed]", e.Note);
        Assert.Equal(Now.AddMinutes(1), e.DateModifiedUtc);

        e.DetachOrder(Now.AddMinutes(2));
        Assert.Equal("lining [order removed]", e.Note);
        Assert.Equal(Now.AddMinutes(1), e.DateModifiedUtc);
    }

    [Fact]
    public void DetachOrder_WithoutNote_AndAtTheLimit()
    {
        var blank = Create(orderId: "O1");
        blank.DetachOrder(Now);
        Assert.Equal(Expense.OrderRemovedMarker, blank.Note);

        var full = Create(note: new string('x', 2000), orderId: "O1");
        full.DetachOrder(Now);
        Assert.Equal(2000, full.Note!.Length);
        Assert.EndsWith(" " + Expense.OrderRemovedMarker, full.Note);
    }

    [Fact]
    public void DetachOrder_NoLink_NoChange()
    {
        var e = Create(note: "rent");
        e.DetachOrder(Now.AddMinutes(5));
        Assert.Equal("rent", e.Note);
        Assert.Equal(Now, e.DateModifiedUtc);
    }

    [Fact]
    public void Categories_AreTheSixConstants_WithPlainLabels()
    {
        Assert.Equal(["Materials", "StaffPay", "RentAndBills", "Equipment", "Transport", "Other"], ExpenseCategory.All);
        Assert.Equal("Fabric and materials", ExpenseCategory.DisplayName(ExpenseCategory.Materials));
        Assert.Equal("Staff pay", ExpenseCategory.DisplayName(ExpenseCategory.StaffPay));
        Assert.Equal("Rent, light and water", ExpenseCategory.DisplayName(ExpenseCategory.RentAndBills));
        Assert.Equal("Machines and repairs", ExpenseCategory.DisplayName(ExpenseCategory.Equipment));
        Assert.Equal("Transport", ExpenseCategory.DisplayName(ExpenseCategory.Transport));
        Assert.Equal("Other", ExpenseCategory.DisplayName("Unknown"));
        Assert.True(ExpenseCategory.All.All(c => c.Length <= FieldLimits.ExpenseCategory));
        Assert.False(ExpenseCategory.IsValid(null));
    }

    [Theory]
    [InlineData(0, "Amount must be more than 0.")]
    [InlineData(10.005, "Amount can have at most 2 decimal places.")]
    [InlineData(50000000.01, "Amount can be at most GH₵ 50,000,000.")]
    public void MoneyRules_Messages(double amount, string expected) =>
        Assert.Equal(expected, MoneyRules.CheckAmount((decimal)amount));

    [Fact]
    public void MoneyRules_SharedWithPayments()
    {
        Assert.Null(MoneyRules.CheckAmount(0.01m));
        Assert.Equal(MoneyRules.AmountMax, Payment.AmountMax);
        Assert.Equal(MoneyRules.AmountMax, Expense.AmountMax);
        Assert.Equal(MoneyRules.CheckAmount(-5m), Payment.CheckAmount(-5m));
    }
}
