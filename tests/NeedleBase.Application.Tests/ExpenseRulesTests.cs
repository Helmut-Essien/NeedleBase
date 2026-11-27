using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Errors;
using NeedleBase.Application.Expenses;
using NeedleBase.Domain.Enums;

namespace NeedleBase.Application.Tests;

public class ExpenseRulesTests
{
    private static readonly DateOnly Today = new(2026, 11, 27);

    [Fact]
    public void Strict_AcceptsValidEntry_DefaultsDateToToday_TrimsNote()
    {
        var (error, detail, entry) = ExpenseRules.Check(120m, " Materials ", null, "  Makola lining ", Today, lenient: false);
        Assert.Equal(RecordError.None, error);
        Assert.Null(detail);
        Assert.Equal(new ExpenseEntry(120m, ExpenseCategory.Materials, Today, "Makola lining"), entry);
        Assert.Null(ExpenseRules.Check(5m, ExpenseCategory.Other, Today, "   ", Today, false).Entry!.Note);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BadAmount_IsRefused_InBothModes(bool lenient)
    {
        foreach (var amount in new[] { 0m, -1m, 10.005m, 50_000_000.01m })
        {
            var (error, detail, entry) = ExpenseRules.Check(amount, ExpenseCategory.Materials, Today, null, Today, lenient);
            Assert.Equal(RecordError.ExpenseInvalid, error);
            Assert.NotNull(detail);
            Assert.Null(entry);
        }
    }

    [Fact]
    public void UnknownCategory_StrictRefuses_LenientMapsToOther_KeepingLabel()
    {
        Assert.Equal(RecordError.ExpenseInvalid, ExpenseRules.Check(10m, "Fuel", Today, null, Today, false).Error);
        var lenient = ExpenseRules.Check(10m, "Fuel", Today, "Shell station", Today, true).Entry!;
        Assert.Equal(ExpenseCategory.Other, lenient.Category);
        Assert.Equal("Shell station [Fuel]", lenient.Note);
        Assert.Equal("[unknown]", ExpenseRules.Check(10m, "", Today, null, Today, true).Entry!.Note);
    }

    [Fact]
    public void NormalizeSyncCategory_IsStable_AndBounded()
    {
        Assert.Equal((ExpenseCategory.Transport, "trotro"), ExpenseRules.NormalizeSyncCategory("Transport", " trotro "));
        var (category, note) = ExpenseRules.NormalizeSyncCategory(new string('x', 40), new string('n', 2000));
        Assert.Equal(ExpenseCategory.Other, category);
        Assert.Equal(2000, note!.Length);
    }

    [Fact]
    public void FutureOrAncientDate_StrictRefuses_LenientClampsToToday()
    {
        var future = ExpenseRules.Check(10m, ExpenseCategory.Other, Today.AddDays(1), null, Today, false);
        Assert.Equal(RecordError.ExpenseInvalid, future.Error);
        Assert.Equal("The date can't be in the future.", future.Detail);
        Assert.Equal(RecordError.ExpenseInvalid, ExpenseRules.Check(10m, ExpenseCategory.Other, new DateOnly(2019, 12, 31), null, Today, false).Error);
        Assert.Equal(Today, ExpenseRules.Check(10m, ExpenseCategory.Other, Today.AddDays(3), null, Today, true).Entry!.SpentOn);
        Assert.Equal(Today, ExpenseRules.Check(10m, ExpenseCategory.Other, new DateOnly(2001, 1, 1), null, Today, true).Entry!.SpentOn);
        Assert.Equal(new DateOnly(2020, 1, 1), ExpenseRules.Check(10m, ExpenseCategory.Other, new DateOnly(2020, 1, 1), null, Today, false).Entry!.SpentOn);
    }

    [Fact]
    public void LongNote_StrictRefuses_LenientCuts()
    {
        var note = new string('x', 2001);
        Assert.Equal(RecordError.ExpenseInvalid, ExpenseRules.Check(10m, ExpenseCategory.Other, Today, note, Today, false).Error);
        Assert.Equal(2000, ExpenseRules.Check(10m, ExpenseCategory.Other, Today, note, Today, true).Entry!.Note!.Length);
    }

    [Fact]
    public void VoidReason_StrictNeedsThreeCharacters_LenientTakesAny()
    {
        Assert.Equal(ExpenseRules.VoidReasonMessage, ExpenseRules.CheckVoidReason(null, false));
        Assert.Equal(ExpenseRules.VoidReasonMessage, ExpenseRules.CheckVoidReason("  ab ", false));
        Assert.Null(ExpenseRules.CheckVoidReason("typo", false));
        Assert.Null(ExpenseRules.CheckVoidReason(null, true));
    }

    [Theory]
    [InlineData(BusinessRole.Owner, true)]
    [InlineData(BusinessRole.Manager, true)]
    [InlineData(BusinessRole.Tailor, false)]
    [InlineData(null, false)]
    public void OwnerAndManager_Manage(string? role, bool expected) => Assert.Equal(expected, ExpenseRules.CanManage(role));

    [Fact]
    public void ErrorCodes_AreStable()
    {
        Assert.Equal("EXPENSES_NOT_ALLOWED", RecordErrorMessages.Code(RecordError.ExpensesNotAllowed));
        Assert.Equal("EXPENSES_FORBIDDEN", RecordErrorMessages.Code(RecordError.ExpenseForbidden));
        Assert.Equal("EXPENSE_NOT_FOUND", RecordErrorMessages.Code(RecordError.ExpenseNotFound));
        Assert.Equal("EXPENSE_CONFLICT", RecordErrorMessages.Code(RecordError.ExpenseConflict));
        Assert.Equal("EXPENSE_INVALID", RecordErrorMessages.Code(RecordError.ExpenseInvalid));
        Assert.Equal("EXPENSE_ORDER_INVALID", RecordErrorMessages.Code(RecordError.ExpenseOrderInvalid));
        Assert.Equal("EXPENSE_FILTER_INVALID", RecordErrorMessages.Code(RecordError.ExpenseFilterInvalid));
        Assert.Equal("ORDER_HAS_EXPENSES", OrderErrorMessages.HasExpenses);
        Assert.Equal(ExpenseRules.NotAllowedMessage, RecordErrorMessages.For(RecordError.ExpensesNotAllowed));
    }
}
