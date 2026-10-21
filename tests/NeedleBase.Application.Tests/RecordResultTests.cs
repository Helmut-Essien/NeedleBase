using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Errors;
using NeedleBase.Domain.Common;

namespace NeedleBase.Application.Tests;

public class RecordResultTests
{
    [Fact]
    public void EveryError_HasAStableDistinctCode_AndCopy()
    {
        var failures = Enum.GetValues<RecordError>().Where(e => e != RecordError.None).ToList();
        var codes = failures.Select(RecordErrorMessages.Code).ToList();
        Assert.Equal(codes.Count, codes.Distinct().Count());
        Assert.All(failures, e => Assert.False(string.IsNullOrWhiteSpace(RecordErrorMessages.For(e))));
        Assert.Equal("CUSTOMER_NOT_FOUND", RecordErrorMessages.Code(RecordError.CustomerNotFound));
        Assert.Equal("MEASUREMENT_NOT_FOUND", RecordErrorMessages.Code(RecordError.MeasurementNotFound));
        Assert.Equal("PHONE_INVALID", RecordErrorMessages.Code(RecordError.PhoneInvalid));
        Assert.Equal(StaleUpdate.Code, RecordErrorMessages.Code(RecordError.Stale));
    }

    [Fact]
    public void Result_SuccessFailureAndDeconstruct()
    {
        RecordResult<string> ok = "value";
        Assert.True(ok.IsSuccess);
        Assert.Null(ok.Code);
        var (v, msg) = ok;
        Assert.Equal("value", v);
        Assert.Null(msg);

        RecordResult<string> phone = RecordResult.Fail(RecordError.PhoneInvalid);
        Assert.False(phone.IsSuccess);
        Assert.Equal(PhoneNumbers.FormatMessage, phone.Message);

        RecordResult<string> detailed = RecordResult.Fail(RecordError.Validation, "Chest is too large.");
        Assert.Equal("Chest is too large.", detailed.Message);
        Assert.Equal(RecordErrorMessages.Validation, detailed.Code);

        RecordResult<string> missing = (string?)null;  // a vanished row is never reported as success
        Assert.False(missing.IsSuccess);
    }
}
