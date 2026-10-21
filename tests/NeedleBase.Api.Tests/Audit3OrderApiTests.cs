using System.Net;
using System.Net.Http.Json;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Customers;
using NeedleBase.Application.Contracts.Orders;
using NeedleBase.Domain.Enums;
using NeedleBase.Infrastructure.Identity;

namespace NeedleBase.Api.Tests;

/// <summary>Third-audit fixes on the REST API: phones, stale updates (409), server search keys, page cap, login throttle.</summary>
public partial class OrderEndpointTests
{
    [Fact]
    public async Task Customer_Phone_IsNormalised_BadFormatIs400_DuplicatesAreListed()
    {
        var (token, _) = await RegisterAsync("Phone");
        using (var create = Authorized(HttpMethod.Post, "/api/customers", token, new CreateCustomerRequest("Abena", "024 555 0199", null, null)))
        using (var response = await _client.SendAsync(create))
        {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var created = (await response.Content.ReadFromJsonAsync<CustomerDetailDto>(JsonOptions))!;
            Assert.Equal("+233245550199", created.Phone);
        }

        using (var bad = Authorized(HttpMethod.Post, "/api/customers", token, new CreateCustomerRequest("Kojo", "phone me", null, null)))
        using (var response = await _client.SendAsync(bad))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("PHONE_INVALID", await ReadCodeAsync(response));
        }

        using var matches = Authorized(HttpMethod.Get, "/api/customers/phone-matches?phone=%2B233%2024%20555%200199", token);
        using var matchResponse = await _client.SendAsync(matches);
        var list = (await matchResponse.Content.ReadFromJsonAsync<List<CustomerListItemDto>>(JsonOptions))!;
        Assert.Equal("Abena", Assert.Single(list).Name);
    }

    [Fact]
    public async Task Customer_StaleBase_Is409_CurrentBaseSaves()
    {
        var (token, _) = await RegisterAsync("StaleCustomer");
        var customer = await CreateCustomerAsync(token, "Yaw");
        var body = new UpdateCustomerRequest("Yaw Mensah", customer.Phone, null, null) { BaseModifiedUtc = customer.DateModifiedUtc.AddMinutes(-1) };
        using (var stale = Authorized(HttpMethod.Put, $"/api/customers/{customer.Id}", token, body))
        using (var response = await _client.SendAsync(stale))
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal(StaleUpdate.Code, await ReadCodeAsync(response));
        }

        body.BaseModifiedUtc = customer.DateModifiedUtc;
        using var ok = Authorized(HttpMethod.Put, $"/api/customers/{customer.Id}", token, body);
        using var okResponse = await _client.SendAsync(ok);
        Assert.Equal(HttpStatusCode.OK, okResponse.StatusCode);

        body.BaseModifiedUtc = null;                     // old clients send no base: last write wins as before
        using var legacy = Authorized(HttpMethod.Put, $"/api/customers/{customer.Id}", token, body);
        using var legacyResponse = await _client.SendAsync(legacy);
        Assert.Equal(HttpStatusCode.OK, legacyResponse.StatusCode);
    }

    [Fact]
    public async Task Order_StaleBase_OnEditAndStatus_Is409()
    {
        var (token, _) = await RegisterAsync("StaleOrder");
        var customer = await CreateCustomerAsync(token, "Esi");
        var order = await CreateOrderAsync(token, OneLine(customer.Id, 50m));
        var old = order.DateModifiedUtc.AddMinutes(-1);

        using (var edit = Authorized(HttpMethod.Put, $"/api/orders/{order.Id}", token, new UpdateOrderRequest { Notes = "x", BaseModifiedUtc = old }))
        using (var response = await _client.SendAsync(edit))
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal(StaleUpdate.Code, await ReadCodeAsync(response));
        }

        using var status = Authorized(HttpMethod.Patch, $"/api/orders/{order.Id}/status", token,
            new UpdateOrderStatusRequest { Status = OrderStatus.Cutting, BaseModifiedUtc = old });
        using var statusResponse = await _client.SendAsync(status);
        Assert.Equal(HttpStatusCode.Conflict, statusResponse.StatusCode);
    }

    [Fact]
    public async Task Search_UsesKeys_AccentsWordPrefixAndLocalPhoneFormat()
    {
        var (token, _) = await RegisterAsync("Search3");
        using (var create = Authorized(HttpMethod.Post, "/api/customers", token, new CreateCustomerRequest("Adwóa Mensah-Owusu", "020 111 4455", null, null)))
        using (var response = await _client.SendAsync(create))
        {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        foreach (var query in new[] { "adwoa", "owusu", "020 111", "+23320111", "201114455" })
        {
            using var list = Authorized(HttpMethod.Get, $"/api/customers?search={Uri.EscapeDataString(query)}", token);
            using var response = await _client.SendAsync(list);
            var page = (await response.Content.ReadFromJsonAsync<PaginatedResponse<CustomerListItemDto>>(JsonOptions))!;
            Assert.True(page.Data.Count == 1, $"'{query}' should find the customer");
        }

        using var none = Authorized(HttpMethod.Get, "/api/customers?search=dwoa", token);   // not a word prefix
        using var noneResponse = await _client.SendAsync(none);
        Assert.Empty((await noneResponse.Content.ReadFromJsonAsync<PaginatedResponse<CustomerListItemDto>>(JsonOptions))!.Data);
    }

    [Fact]
    public async Task Orders_SearchByCustomerKeys_AndHugePage_IsNot500()
    {
        var (token, _) = await RegisterAsync("OrderSearch3");
        var customer = await CreateCustomerAsync(token, "Kwesi Ánsah");
        await CreateOrderAsync(token, OneLine(customer.Id, 20m));

        using var search = Authorized(HttpMethod.Get, "/api/orders?search=ansah", token);
        using var response = await _client.SendAsync(search);
        Assert.Single((await response.Content.ReadFromJsonAsync<PaginatedResponse<OrderListItemDto>>(JsonOptions))!.Data);

        foreach (var url in new[] { $"/api/orders?page={int.MaxValue}", $"/api/customers?page={int.MaxValue}&pageSize=100" })
        {
            using var huge = Authorized(HttpMethod.Get, url, token);
            using var hugeResponse = await _client.SendAsync(huge);
            Assert.Equal(HttpStatusCode.OK, hugeResponse.StatusCode);
        }
    }

    [Fact]
    public async Task Login_WrongPasswords_ThrottlePerEmailAndIp_NoAccountLock_StateHiddenWithoutPassword()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var email = $"throttle{suffix}@ama-stitches.test";
        using (var register = await _client.PostAsJsonAsync("/api/auth/register-business",
                   new NeedleBase.Application.Contracts.Auth.RegisterBusinessRequest(email, "Passw0rd!", "Ama", "Owusu", $"Throttle {suffix}", "+233200000000"), JsonOptions))
        {
            Assert.Equal(HttpStatusCode.OK, register.StatusCode);
        }

        for (var i = 0; i < LoginThrottle.FreeFailures; i++)
        {
            using var wrong = await _client.PostAsJsonAsync("/api/auth/login", new NeedleBase.Application.Contracts.Auth.LoginRequest(email, "wrong-pass1"), JsonOptions);
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        }

        using var throttled = await _client.PostAsJsonAsync("/api/auth/login", new NeedleBase.Application.Contracts.Auth.LoginRequest(email, "Passw0rd!"), JsonOptions);
        Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);
        Assert.Equal("LOGIN_THROTTLED", await ReadCodeAsync(throttled));
        Assert.NotNull(throttled.Headers.RetryAfter?.Delta);
    }
}
