using System.ComponentModel.DataAnnotations;
using NeedleBase.Application.Contracts.Errors;
using NeedleBase.Application.Contracts.Orders;
using NeedleBase.Application.Orders;
using NeedleBase.Domain.Common;
using NeedleBase.Domain.Enums;

namespace NeedleBase.Application.Tests;

/// <summary>Code-gaps G14: who may assign, the request shape, and the copy a refused assign shows.</summary>
public class OrderAssigneeRulesTests
{
    [Theory]
    [InlineData(BusinessRole.Owner, true)]
    [InlineData(BusinessRole.Manager, true)]
    [InlineData(BusinessRole.Tailor, false)]
    [InlineData(null, false)]
    public void Only_owner_or_manager_may_assign(string? role, bool allowed) =>
        Assert.Equal(allowed, OrderEditor.CanEdit(role));

    [Fact]
    public void Request_allows_unassign_and_caps_id_length()
    {
        Assert.True(Valid(new UpdateOrderAssigneeRequest { AssignedToUserId = null }));
        Assert.True(Valid(new UpdateOrderAssigneeRequest { AssignedToUserId = new string('a', FieldLimits.AssignedToUserId) }));
        Assert.False(Valid(new UpdateOrderAssigneeRequest { AssignedToUserId = new string('a', FieldLimits.AssignedToUserId + 1) }));
    }

    [Fact]
    public void Refusals_use_plain_copy()
    {
        Assert.Equal("That person isn't on your team. Pick someone else.", OrderErrorMessages.For(OrderErrorMessages.AssigneeInvalid));
        Assert.NotEqual(OrderErrorMessages.Fallback, OrderErrorMessages.For(OrderErrorMessages.EditForbidden));
    }

    private static bool Valid(object model) =>
        Validator.TryValidateObject(model, new ValidationContext(model), new List<ValidationResult>(), validateAllProperties: true);
}
