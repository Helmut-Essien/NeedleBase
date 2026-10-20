using System.Net;
using System.Net.Http.Json;
using NeedleBase.Application.Contracts.Measurements;
using NeedleBase.Application.Contracts.Orders;
using NeedleBase.Domain.Enums;

namespace NeedleBase.Api.Tests;

/// <summary>Second-audit fixes on the REST API: amount rules and order-line snapshots.</summary>
public partial class OrderEndpointTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(10.005)]
    [InlineData(1_000_000.01)]
    public async Task CreateOrder_BadUnitPrice_Is400(double price)
    {
        var (token, _) = await RegisterAsync("Price");
        var customer = await CreateCustomerAsync(token, "Ato");
        using var request = Authorized(HttpMethod.Post, "/api/orders", token, OneLine(customer.Id, (decimal)price));
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateMeasurement_OutOfRange_Is400_InRangeIsCreated()
    {
        var (token, _) = await RegisterAsync("Range");
        var customer = await CreateCustomerAsync(token, "Akua");
        foreach (var (value, expected) in new[] { (301m, HttpStatusCode.BadRequest), (0m, HttpStatusCode.BadRequest), (300m, HttpStatusCode.Created) })
        {
            using var request = Authorized(HttpMethod.Post, $"/api/customers/{customer.Id}/measurements", token, new CreateMeasurementRequest
            {
                GarmentTemplate = GarmentTemplate.Shirt,
                Unit = MeasurementUnit.Centimetres,
                Fields = new Dictionary<string, decimal?> { ["chest"] = value }
            });
            using var response = await _client.SendAsync(request);
            Assert.Equal(expected, response.StatusCode);
        }
    }

    [Fact]
    public async Task CreateOrder_ProfileGoneButLineCarriesSnapshot_IsCreated()
    {
        var (token, _) = await RegisterAsync("Snapshot");
        var customer = await CreateCustomerAsync(token, "Kwabena");
        var body = OneLine(customer.Id, 120m);
        body.Lines[0].MeasurementProfileId = NeedleBase.Application.Common.IdGenerator.NewId(); // deleted elsewhere
        body.Lines[0].MeasurementFields = new Dictionary<string, decimal?> { ["chest"] = 98m };

        var created = await CreateOrderAsync(token, body);

        var line = Assert.Single(created.Lines);
        Assert.Null(line.MeasurementProfileId);
        Assert.Equal(98m, line.MeasurementFields["chest"]);

        body.Lines[0].MeasurementFields = new Dictionary<string, decimal?>();
        using var request = Authorized(HttpMethod.Post, "/api/orders", token, body);
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); // no snapshot to fall back on: MEASUREMENT_NOT_FOUND
    }
}
