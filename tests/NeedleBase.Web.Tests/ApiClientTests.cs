using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using NeedleBase.Application.Contracts.Orders;
using NeedleBase.Web.Services;

namespace NeedleBase.Web.Tests;

public class ApiClientTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string?> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));
            return respond(request);
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public string? Name { get; private set; }
        public HttpClient CreateClient(string name) { Name = name; return new HttpClient(handler, false) { BaseAddress = new Uri("http://api.test/") }; }
    }

    private static (NeedleBaseApi Api, StubHandler Handler, Factory Factory) Make(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new StubHandler(respond);
        var factory = new Factory(handler);
        return (new NeedleBaseApi(factory, NullLogger<NeedleBaseApi>.Instance), handler, factory);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json, string type = "application/json") =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, type) };

    private const string EmptyPage = """{"data":[],"totalCount":0,"page":1,"pageSize":20,"totalPages":0}""";

    [Fact]
    public async Task Orders_query_is_clamped_trimmed_and_escaped()
    {
        var (api, handler, factory) = Make(_ => Json(HttpStatusCode.OK, EmptyPage));
        var result = await api.ListOrdersAsync(new OrderQuery(-3, 5000, "  Ama & Kofi?  ", "open", "due", "me"));

        Assert.True(result.IsSuccess);
        Assert.Equal(NeedleBaseApi.ClientName, factory.Name);
        var uri = handler.Requests.Single().RequestUri!.AbsoluteUri;
        Assert.Contains("page=1&pageSize=100", uri);
        Assert.Contains("search=Ama%20%26%20Kofi%3F", uri);
        Assert.Contains("&status=open&sort=due&assignedTo=me", uri);
    }

    [Fact]
    public async Task Blank_filters_are_not_sent()
    {
        var (api, handler, _) = Make(_ => Json(HttpStatusCode.OK, EmptyPage));
        await api.ListOrdersAsync(new OrderQuery(2, 0, "   ", null, ""));
        var uri = handler.Requests.Single().RequestUri!.AbsoluteUri;
        Assert.EndsWith("api/orders?page=2&pageSize=1", uri);
    }

    [Fact]
    public async Task Order_id_is_path_escaped()
    {
        var (api, handler, _) = Make(_ => Json(HttpStatusCode.NotFound, ""));
        await api.GetOrderAsync("a/../b");
        Assert.Contains("api/orders/a%2F..%2Fb", handler.Requests.Single().RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task Problem_details_become_typed_failure()
    {
        var (api, _, _) = Make(_ => Json(HttpStatusCode.Conflict,
            """{"status":409,"title":"Conflict","detail":"Changed elsewhere.","code":"STALE_UPDATE"}""", "application/problem+json"));
        var result = await api.UpdateOrderStatusAsync("o1", new UpdateOrderStatusRequest { Status = "Sewing" });

        Assert.False(result.IsSuccess);
        Assert.False(result.Unreachable);
        Assert.Equal(HttpStatusCode.Conflict, result.Status);
        Assert.Equal("STALE_UPDATE", result.Code);
        Assert.Equal("Changed elsewhere.", result.Message);
    }

    [Fact]
    public async Task Status_patch_sends_base_modified()
    {
        var (api, handler, _) = Make(_ => Json(HttpStatusCode.OK, "null"));
        var when = new DateTimeOffset(2026, 10, 22, 8, 0, 0, TimeSpan.Zero);
        var result = await api.UpdateOrderStatusAsync("o1", new UpdateOrderStatusRequest { Status = "Ready", BaseModifiedUtc = when });

        Assert.Equal(HttpMethod.Patch, handler.Requests.Single().Method);
        Assert.Contains("\"baseModifiedUtc\":\"2026-10-22T08:00:00+00:00\"", handler.Bodies.Single());
        Assert.False(result.IsSuccess); // a null body is never treated as success
    }

    [Fact]
    public async Task Validation_problem_uses_first_field_message()
    {
        var (api, _, _) = Make(_ => Json(HttpStatusCode.BadRequest, """{"status":400,"title":"One or more validation errors occurred.","errors":{"Status":["Status is not valid."]}}"""));
        var result = await api.UpdateOrderStatusAsync("o1", new UpdateOrderStatusRequest { Status = "x" });
        Assert.Equal("Status is not valid.", result.Message);
    }

    [Fact]
    public async Task Network_failure_is_offline_not_exception()
    {
        var (api, _, _) = Make(_ => throw new HttpRequestException("down"));
        var result = await api.GetDashboardAsync();
        Assert.True(result.Unreachable);
        Assert.Equal(ApiResult<object>.OfflineMessage, result.Message);
    }

    [Fact]
    public async Task Timeout_is_offline()
    {
        var (api, _, _) = Make(_ => throw new TaskCanceledException("timeout"));
        Assert.True((await api.GetStaffNamesAsync()).Unreachable);
    }

    [Fact]
    public async Task Garbled_success_body_is_offline_state()
    {
        var (api, _, _) = Make(_ => Json(HttpStatusCode.OK, "<html>proxy</html>"));
        Assert.True((await api.GetDashboardAsync()).Unreachable);
    }

    [Fact]
    public async Task Browser_hanging_up_is_handled_quietly_not_thrown()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var (api, _, _) = Make(_ => throw new TaskCanceledException("Operation canceled", new System.Net.Sockets.SocketException(125)));
        var result = await api.GetDashboardAsync(cts.Token); // no unhandled TaskCanceledException
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task Payments_and_customers_paging_clamped()
    {
        var (api, handler, _) = Make(_ => Json(HttpStatusCode.OK, EmptyPage));
        await api.ListPaymentsAsync(0, 999);
        await api.ListCustomersAsync(" 024 ", 0, -1);
        Assert.EndsWith("api/payments?page=1&pageSize=100", handler.Requests[0].RequestUri!.AbsoluteUri);
        Assert.EndsWith("api/customers?page=1&pageSize=1&search=024", handler.Requests[1].RequestUri!.AbsoluteUri);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task Bare_gateway_errors_are_offline(HttpStatusCode code)
    {
        var (api, _, _) = Make(_ => new HttpResponseMessage(code));
        var result = await api.GetDashboardAsync();
        Assert.True(result.Unreachable);
        Assert.Equal(ApiResult<object>.OfflineMessage, result.Message);
    }

    [Fact]
    public async Task Typed_503_keeps_its_message()
    {
        var (api, _, _) = Make(_ => Json(HttpStatusCode.ServiceUnavailable, """{"status":503,"code":"SYNC_MAINTENANCE","detail":"Down for a minute."}"""));
        var result = await api.GetDashboardAsync();
        Assert.False(result.Unreachable);
        Assert.Equal("Down for a minute.", result.Message);
    }

    [Fact]
    public void Message_falls_back_to_plain_copy()
    {
        Assert.Equal("Something went wrong. Try again.", Data.Problem<int>(HttpStatusCode.InternalServerError).Message);
    }

    [Fact]
    public async Task Activity_query_carries_filters_as_iso_dates()
    {
        var (api, handler, _) = Make(_ => Json(HttpStatusCode.OK, EmptyPage));
        await api.ListActivityAsync(new NeedleBase.Application.Contracts.Audit.AuditQuery
        {
            Page = 2, PageSize = 50, Action = "payment.", OrderId = "o 1", From = new DateOnly(2026, 10, 1), To = new DateOnly(2026, 10, 22), Search = "a&b"
        });
        var uri = handler.Requests.Single().RequestUri!.AbsoluteUri;
        Assert.Contains("api/audit?page=2&pageSize=50", uri);
        Assert.Contains("action=payment.", uri);
        Assert.Contains("orderId=o%201", uri);
        Assert.Contains("from=2026-10-01&to=2026-10-22", uri);
        Assert.Contains("search=a%26b", uri);
    }
}
