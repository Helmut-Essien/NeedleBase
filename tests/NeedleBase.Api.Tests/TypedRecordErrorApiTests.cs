using System.Net;
using System.Net.Http.Json;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Errors;
using NeedleBase.Application.Contracts.Measurements;
using NeedleBase.Domain.Enums;

namespace NeedleBase.Api.Tests;

/// <summary>
/// Customer/measurement services return typed <see cref="RecordError"/>; the API must keep the same status codes and
/// ProblemDetails <c>code</c> values older clients already switch on.
/// </summary>
public partial class OrderEndpointTests
{
    private static CreateMeasurementRequest Chest(decimal value) => new()
    {
        GarmentTemplate = GarmentTemplate.Shirt,
        Unit = MeasurementUnit.Centimetres,
        Fields = new Dictionary<string, decimal?> { ["chest"] = value }
    };

    [Fact]
    public async Task RecordErrors_KeepStatusAndCodes()
    {
        var (token, _) = await RegisterAsync("TypedErrors");
        var customer = await CreateCustomerAsync(token, "Akosua");

        async Task Expect(HttpRequestMessage request, HttpStatusCode status, string code)
        {
            using (request)
            using (var response = await _client.SendAsync(request))
            {
                Assert.Equal(status, response.StatusCode);
                Assert.Equal(code, await ReadCodeAsync(response));
            }
        }

        await Expect(Authorized(HttpMethod.Get, "/api/customers/missing-id/measurements", token),
            HttpStatusCode.NotFound, RecordErrorMessages.CustomerNotFound);
        await Expect(Authorized(HttpMethod.Post, "/api/customers/missing-id/measurements", token, Chest(90m)),
            HttpStatusCode.NotFound, RecordErrorMessages.CustomerNotFound);
        await Expect(Authorized(HttpMethod.Put, "/api/measurements/missing-id", token, new UpdateMeasurementRequest
            {
                GarmentTemplate = GarmentTemplate.Shirt, Unit = MeasurementUnit.Centimetres, Fields = new Dictionary<string, decimal?>()
            }),
            HttpStatusCode.NotFound, RecordErrorMessages.MeasurementNotFound);
        await Expect(Authorized(HttpMethod.Delete, "/api/measurements/missing-id", token),
            HttpStatusCode.NotFound, RecordErrorMessages.MeasurementNotFound);
        await Expect(Authorized(HttpMethod.Put, "/api/customers/missing-id", token,
                new NeedleBase.Application.Contracts.Customers.UpdateCustomerRequest("X", "024 555 0100", null, null)),
            HttpStatusCode.NotFound, RecordErrorMessages.CustomerNotFound);

        // Validation keeps the per-entity save-failed code (unchanged for older clients).
        await Expect(Authorized(HttpMethod.Post, $"/api/customers/{customer.Id}/measurements", token, Chest(99999m)),
            HttpStatusCode.BadRequest, "MEASUREMENT_SAVE_FAILED");

        using var create = Authorized(HttpMethod.Post, $"/api/customers/{customer.Id}/measurements", token, Chest(90m));
        using var created = await _client.SendAsync(create);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var m = (await created.Content.ReadFromJsonAsync<MeasurementDetailDto>(JsonOptions))!;

        await Expect(Authorized(HttpMethod.Put, $"/api/measurements/{m.Id}", token, new UpdateMeasurementRequest
            {
                GarmentTemplate = m.GarmentTemplate, Unit = m.Unit, Fields = new Dictionary<string, decimal?>(m.Fields),
                Notes = "late", BaseModifiedUtc = m.DateModifiedUtc.AddMinutes(-1)
            }),
            HttpStatusCode.Conflict, StaleUpdate.Code);
    }
}
