using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NeedleBase.Application.Abstractions;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Auth;
using NeedleBase.Application.Contracts.Customers;
using NeedleBase.Application.Contracts.Orders;
using NeedleBase.Application.Contracts.Payments;
using NeedleBase.Domain.Entities;
using NeedleBase.Domain.Enums;
using NeedleBase.Infrastructure.Email;
using NeedleBase.Infrastructure.Persistence;

namespace NeedleBase.Api.Tests;

/// <summary>Customer transactional email channel: opt-in, status + receipt (PDF), SMTP-off honesty.</summary>
public class CustomerEmailChannelTests : IClassFixture<NeedleBaseApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly NeedleBaseApiFactory _factory;
    private HttpClient _client;

    public CustomerEmailChannelTests(NeedleBaseApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private CapturingTransport UseEmail()
    {
        var capture = new CapturingTransport();
        var factory = _factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Email:Host", "smtp.example.test");
            b.UseSetting("Email:Username", "shop@example.test");
            b.UseSetting("Email:Password", "not-a-real-password");
            b.UseSetting("Email:AppBaseUrl", "https://app.example.test");
            b.ConfigureTestServices(s => s.AddSingleton<IEmailTransport>(capture));
        });
        _client = factory.CreateClient();
        return capture;
    }

    [Fact]
    public async Task StatusAndReceipt_WhenOptedIn_Queues_WithPdfAttachment()
    {
        var mail = UseEmail();
        var (token, businessId) = await RegisterAsync("ce1");
        var customerId = await CreateCustomerAsync(token, "Efua Mensah", "+233245550201", "efua.ce1@example.com");
        await SetEmailConsentAsync(token, customerId, optedIn: true);

        var orderId = await CreateOrderAsync(token, customerId, 800m);
        using var statusReq = Authorized(
            HttpMethod.Patch,
            $"/api/orders/{orderId}/status",
            token,
            new UpdateOrderStatusRequest { Status = OrderStatus.Ready });
        using var statusRes = await _client.SendAsync(statusReq);
        Assert.Equal(HttpStatusCode.OK, statusRes.StatusCode);

        var statusMail = await mail.WaitForAsync("efua.ce1@example.com", "customer_order_status");
        Assert.Contains("Ready", statusMail.TextBody, StringComparison.OrdinalIgnoreCase);
        Assert.Null(statusMail.Attachments);

        Assert.Equal(HttpStatusCode.Created, (await RecordAsync(token, orderId, 300m, PaymentMethod.Cash)).Status);
        var receipt = await mail.WaitForAsync("efua.ce1@example.com", "customer_payment_receipt");
        Assert.Contains("Payment receipt", receipt.TextBody);
        Assert.Contains("A PDF copy is attached.", receipt.TextBody);
        Assert.NotNull(receipt.Attachments);
        Assert.Single(receipt.Attachments!);
        Assert.Equal("application/pdf", receipt.Attachments![0].ContentType);
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(receipt.Attachments[0].Content, 0, 4));

        await using var scope = _factory.Services.CreateAsyncScope();
        // UseEmail rebuilt the host — resolve DB from the same factory root is fine (shared TestDatabase).
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.CurrentBusinessId = businessId;
        var sent = await db.CustomerEmailSents.AsNoTracking()
            .Where(s => s.BusinessId == businessId)
            .ToListAsync();
        Assert.Contains(sent, s => s.Kind == CustomerEmailSent.KindStatus);
        Assert.Contains(sent, s => s.Kind == CustomerEmailSent.KindReceipt);
    }

    [Fact]
    public async Task SmtpOff_DoesNotMarkSent_WhenPaymentRecorded()
    {
        // Default factory: Email not configured.
        _client = _factory.CreateClient();
        var (token, businessId) = await RegisterAsync("ceoff");
        var customerId = await CreateCustomerAsync(token, "Kojo Mensah", "+233245550202", "kojo.ceoff@example.com");
        await SetEmailConsentAsync(token, customerId, optedIn: true);
        var orderId = await CreateOrderAsync(token, customerId, 400m);
        Assert.Equal(HttpStatusCode.Created, (await RecordAsync(token, orderId, 100m, PaymentMethod.Cash)).Status);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.CurrentBusinessId = businessId;
        Assert.Empty(await db.CustomerEmailSents.AsNoTracking()
            .Where(s => s.BusinessId == businessId)
            .ToListAsync());
    }

    [Fact]
    public async Task TwoPaymentsSameSave_PaidSoFarIncludesBoth()
    {
        var mail = UseEmail();
        var (token, businessId) = await RegisterAsync("ce2");
        var customerId = await CreateCustomerAsync(token, "Ama Boateng", "+233245550203", "ama.ce2@example.com");
        await SetEmailConsentAsync(token, customerId, optedIn: true);
        var orderId = await CreateOrderAsync(token, customerId, 500m);

        // Simulate a sync-style batch: two Added payments in one SaveChanges.
        await using (var scope = _factory.WithWebHostBuilder(b =>
                     {
                         b.UseSetting("Email:Host", "smtp.example.test");
                         b.UseSetting("Email:Username", "shop@example.test");
                         b.UseSetting("Email:Password", "not-a-real-password");
                         b.ConfigureTestServices(s => s.AddSingleton<IEmailTransport>(mail));
                     }).Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.CurrentBusinessId = businessId;
            var now = DateTimeOffset.UtcNow;
            db.Payments.Add(Payment.Create(
                IdGenerator.NewId(), businessId, orderId, customerId, 200m, PaymentMethod.Cash, null, now, now));
            db.Payments.Add(Payment.Create(
                IdGenerator.NewId(), businessId, orderId, customerId, 300m, PaymentMethod.MoMo, null, now, now));
            await db.SaveChangesAsync();
        }

        var receipts = new List<EmailMessage>();
        for (var i = 0; i < 40 && receipts.Count < 2; i++)
        {
            receipts = mail.Sent.Where(m => m.Kind == "customer_payment_receipt").ToList();
            if (receipts.Count < 2)
            {
                await Task.Delay(50);
            }
        }

        Assert.Equal(2, receipts.Count);
        Assert.All(receipts, r => Assert.Contains("Paid so far: GH₵ 500.00", r.TextBody));
        Assert.All(receipts, r => Assert.Contains("Paid in full", r.TextBody));
    }

    private async Task<(string AccessToken, string BusinessId)> RegisterAsync(string label)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var register = new RegisterBusinessRequest(
            $"owner{label}{suffix}@ama-stitches.test",
            "Passw0rd!",
            "Ama",
            "Owusu",
            $"Ama Stitches {label} {suffix}",
            "+233200000000");

        using var response = await _client.PostAsJsonAsync("/api/auth/register-business", register, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<AuthPayload>(JsonOptions);
        Assert.NotNull(payload?.AccessToken);
        Assert.NotNull(payload.User?.BusinessId);
        return (payload.AccessToken, payload.User.BusinessId);
    }

    private async Task<string> CreateCustomerAsync(string token, string name, string phone, string email)
    {
        using var create = Authorized(
            HttpMethod.Post,
            "/api/customers",
            token,
            new CreateCustomerRequest(name, phone, null, null, email: email));
        using var createResponse = await _client.SendAsync(create);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<CustomerDetailDto>(JsonOptions);
        Assert.NotNull(created);
        return created.Id;
    }

    private async Task SetEmailConsentAsync(string token, string customerId, bool optedIn)
    {
        using var put = Authorized(
            HttpMethod.Put,
            $"/api/customers/{customerId}/email-consent",
            token,
            new SetCustomerEmailConsentRequest { OptedIn = optedIn });
        using var response = await _client.SendAsync(put);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<string> CreateOrderAsync(string token, string customerId, decimal total)
    {
        using var create = Authorized(
            HttpMethod.Post,
            "/api/orders",
            token,
            new CreateOrderRequest
            {
                CustomerId = customerId,
                Lines =
                [
                    new CreateOrderLineRequest
                    {
                        GarmentTemplate = GarmentTemplate.Shirt,
                        Quantity = 1,
                        UnitPrice = total,
                        Unit = MeasurementUnit.Centimetres,
                        MeasurementFields = new Dictionary<string, decimal?>()
                    }
                ]
            });
        using var response = await _client.SendAsync(create);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var order = await response.Content.ReadFromJsonAsync<OrderDetailDto>(JsonOptions);
        Assert.NotNull(order);
        return order.Id;
    }

    private async Task<(HttpStatusCode Status, PaymentDto? Payment)> RecordAsync(
        string token, string orderId, decimal amount, string method)
    {
        using var request = Authorized(
            HttpMethod.Post,
            $"/api/orders/{orderId}/payments",
            token,
            new RecordPaymentRequest { Amount = amount, Method = method });
        using var response = await _client.SendAsync(request);
        var payment = response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<PaymentDto>(JsonOptions)
            : null;
        return (response.StatusCode, payment);
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        return request;
    }

    private sealed record AuthPayload(string AccessToken, AuthUserDto? User);
}
