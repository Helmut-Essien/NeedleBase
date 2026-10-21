using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Errors;
using NeedleBase.Application.Orders;

namespace NeedleBase.Application.Tests;

public class OrderErrorMappingTests
{
    [Theory]
    [InlineData(OrderError.NotFound, RecordError.OrderNotFound, OrderErrorMessages.OrderNotFound)]
    [InlineData(OrderError.CustomerNotFound, RecordError.CustomerNotFound, OrderErrorMessages.CustomerNotFound)]
    [InlineData(OrderError.MeasurementNotFound, RecordError.MeasurementNotFound, OrderErrorMessages.MeasurementNotFound)]
    [InlineData(OrderError.IdConflict, RecordError.OrderConflict, OrderErrorMessages.OrderConflict)]
    [InlineData(OrderError.Locked, RecordError.OrderLocked, OrderErrorMessages.OrderLocked)]
    [InlineData(OrderError.StatusInvalid, RecordError.OrderStatusInvalid, OrderErrorMessages.OrderStatusInvalid)]
    [InlineData(OrderError.AssigneeInvalid, RecordError.AssigneeInvalid, OrderErrorMessages.AssigneeInvalid)]
    [InlineData(OrderError.TotalMismatch, RecordError.TotalMismatch, OrderErrorMessages.TotalMismatch)]
    [InlineData(OrderError.Invalid, RecordError.Validation, OrderErrorMessages.Validation)]
    [InlineData(OrderError.Stale, RecordError.Stale, StaleUpdate.Code)]
    public void OrderErrors_MapToSameCodesAsTheApi(OrderError order, RecordError record, string code)
    {
        Assert.Equal(record, OrderErrorMapping.ToRecord(order));
        Assert.Equal(code, OrderErrorMapping.Code(order));
    }

    [Fact]
    public void EveryOrderError_IsMapped()
    {
        Assert.All(Enum.GetValues<OrderError>().Where(e => e != OrderError.None),
            e => Assert.NotEqual(RecordError.None, OrderErrorMapping.ToRecord(e)));
    }

    [Fact]
    public void OrderRecordErrors_UseOrderCopy()
    {
        Assert.Equal(OrderErrorMessages.For(OrderErrorMessages.OrderLocked), RecordErrorMessages.For(RecordError.OrderLocked));
    }
}
