using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NeedleBase.Application.Contracts.Customers;
using NeedleBase.Application.Contracts.Orders;
using NeedleBase.Application.Contracts.WhatsApp;
using NeedleBase.Domain.Entities;
using NeedleBase.Domain.Enums;
using NeedleBase.Infrastructure.Persistence;
using NeedleBase.Infrastructure.WhatsApp;
using Xunit;

namespace NeedleBase.Api.Tests;

/// <summary>Fake Meta client: records sends, never touches the network.</summary>
public sealed class FakeWhatsAppClient : IWhatsAppClient
{
    public List<(string To, string Template, IReadOnlyList<string> Parameters)> Sent { get; } = [];
    public Func<WhatsAppSendResult>? Next { get; set; }

    public Task<WhatsAppSendResult> SendTemplateAsync(string toDigits, string template, string language, IReadOnlyList<string> bodyParameters,
        CancellationToken cancellationToken)
    {
        lock (Sent)
        {
            Sent.Add((toDigits, template, bodyParameters));
            return Task.FromResult(Next?.Invoke() ?? WhatsAppSendResult.Sent("wamid." + Guid.NewGuid().ToString("N")));
        }
    }
}

public sealed partial class StaffApiTests
{
    private const string TestAppSecret = "test-app-secret-not-real";
    private const string TestVerifyToken = "test-verify-token";

    private (Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> Factory, FakeWhatsAppClient Client) WhatsAppFactory(bool configured = true)
    {
        var fake = new FakeWhatsAppClient();
        var factory = _factory.WithWebHostBuilder(b =>
        {
            if (configured)
            {
                b.UseSetting("WhatsApp:AccessToken", "fake-token-for-tests");
                b.UseSetting("WhatsApp:PhoneNumberId", "1234567890");
                b.UseSetting("WhatsApp:Templates:Ready", "order_ready_for_pickup");
                b.UseSetting("WhatsApp:Templates:Fitting", "order_ready_for_fitting");
                b.UseSetting("WhatsApp:AppSecret", TestAppSecret);
                b.UseSetting("WhatsApp:VerifyToken", TestVerifyToken);
                b.UseSetting("WhatsApp:DelaySeconds", "0");
            }

            b.ConfigureTestServices(s =>
            {
                s.AddSingleton<IWhatsAppClient>(fake);
                // Tests drive the sender themselves (deterministic).
                var hosted = s.Where(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(WhatsAppSender)).ToList();
                foreach (var d in hosted)
                {
                    s.Remove(d);
                }
            });
        });
        _client = factory.CreateClient();
        return (factory, fake);
    }

    private static async Task<int> RunSenderAsync(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory, TimeSpan? ahead = null)
    {
        var options = factory.Services.GetRequiredService<IOptionsMonitor<WhatsAppOptions>>().CurrentValue;
        return await WhatsAppSender.RunOnceAsync(factory.Services.GetRequiredService<IServiceScopeFactory>(), options,
            DateTimeOffset.UtcNow + (ahead ?? TimeSpan.FromSeconds(1)), CancellationToken.None);
    }

    private async Task<List<WhatsAppMessage>> MessagesForAsync(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory, string orderId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.WhatsAppMessages.IgnoreQueryFilters().AsNoTracking().Where(m => m.OrderId == orderId).OrderBy(m => m.Id).ToListAsync();
    }

    private async Task<(string OrderId, string CustomerId)> WhatsAppOrderAsync(string token, string phone)
    {
        using var customerResponse = await SendAsync(HttpMethod.Post, "/api/customers", token, new CreateCustomerRequest("Efua Mensah", phone, null, null));
        Assert.Equal(HttpStatusCode.Created, customerResponse.StatusCode);
        var customer = (await customerResponse.Content.ReadFromJsonAsync<CustomerDetailDto>(JsonOptions))!;
        using var response = await SendAsync(HttpMethod.Post, "/api/orders", token, new CreateOrderRequest
        {
            CustomerId = customer.Id,
            TotalAmount = 300,
            Lines =
            [
                new CreateOrderLineRequest
                {
                    GarmentTemplate = GarmentTemplate.Dress, Quantity = 1, UnitPrice = 300,
                    Unit = MeasurementUnit.Centimetres, MeasurementFields = new Dictionary<string, decimal?>()
                }
            ]
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return ((await response.Content.ReadFromJsonAsync<OrderDetailDto>(JsonOptions))!.Id, customer.Id);
    }

    private static string RandomGhanaPhone() => "024" + Random.Shared.Next(1_000_000, 9_999_999).ToString();

    [Fact]
    public async Task WhatsApp_Unconfigured_IsOff_CantBeTurnedOn_AndTailorCantChangeSettings()
    {
        WhatsAppFactory(configured: false);
        var owner = await RegisterAsync("wa0");
        using (var r = await SendAsync(HttpMethod.Get, "/api/whatsapp/settings", owner.Token))
        {
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            var dto = (await r.Content.ReadFromJsonAsync<WhatsAppSettingsDto>(JsonOptions))!;
            Assert.False(dto.Configured);
            Assert.False(dto.Enabled);
            Assert.Empty(dto.AvailableStatuses);
        }

        await AssertCode(WhatsAppErrorCodes.NotConfigured, HttpMethod.Put, "/api/whatsapp/settings", owner.Token,
            new UpdateWhatsAppSettingsRequest { Enabled = true, Statuses = ["Ready"] });

        var tailor = await AddMemberAsync(owner.Token, BusinessRole.Tailor);
        await AssertStatus(HttpStatusCode.Forbidden, HttpMethod.Put, "/api/whatsapp/settings", tailor.Tokens.AccessToken!,
            new UpdateWhatsAppSettingsRequest { Enabled = false });
        await AssertStatus(HttpStatusCode.Forbidden, HttpMethod.Get, "/api/whatsapp/messages", tailor.Tokens.AccessToken!);

        // Webhook is closed when not configured.
        using var hook = await _client.GetAsync("/api/whatsapp/webhook?hub.mode=subscribe&hub.verify_token=x&hub.challenge=1");
        Assert.Equal(HttpStatusCode.Forbidden, hook.StatusCode);
    }

    [Fact]
    public async Task WhatsApp_ReadyWithConsent_SendsOnce_NoDuplicates_OnFlipBack_AndLogIsMasked()
    {
        var (factory, fake) = WhatsAppFactory();
        var owner = await RegisterAsync("wa1");
        await AssertStatus(HttpStatusCode.OK, HttpMethod.Put, "/api/whatsapp/settings", owner.Token,
            new UpdateWhatsAppSettingsRequest { Enabled = true, Statuses = ["Ready"] });
        await AssertCode(WhatsAppErrorCodes.NoTemplate, HttpMethod.Put, "/api/whatsapp/settings", owner.Token,
            new UpdateWhatsAppSettingsRequest { Enabled = true, Statuses = ["Cutting"] });

        var phone = RandomGhanaPhone();
        var (orderId, customerId) = await WhatsAppOrderAsync(owner.Token, phone);

        // No consent yet: Ready queues nothing.
        Assert.Equal(HttpStatusCode.OK, await SetStatusAsync(owner.Token, orderId, OrderStatus.Ready));
        Assert.Empty(await MessagesForAsync(factory, orderId));
        Assert.Equal(HttpStatusCode.OK, await SetStatusAsync(owner.Token, orderId, OrderStatus.Sewing));

        await AssertStatus(HttpStatusCode.OK, HttpMethod.Put, $"/api/whatsapp/customers/{customerId}/consent", owner.Token,
            new SetWhatsAppConsentRequest { OptedIn = true });
        Assert.Equal(HttpStatusCode.OK, await SetStatusAsync(owner.Token, orderId, OrderStatus.Ready));
        Assert.Single(await MessagesForAsync(factory, orderId));

        // Flip away and back before sending: still one row; it sends once (status is Ready again).
        Assert.Equal(HttpStatusCode.OK, await SetStatusAsync(owner.Token, orderId, OrderStatus.Sewing));
        Assert.Equal(HttpStatusCode.OK, await SetStatusAsync(owner.Token, orderId, OrderStatus.Ready));
        Assert.Single(await MessagesForAsync(factory, orderId));

        await RunSenderAsync(factory);
        var sent = fake.Sent.Where(s => s.To == "233" + phone[1..]).ToList();
        var one = Assert.Single(sent);
        Assert.Equal("order_ready_for_pickup", one.Template);
        Assert.Equal("Efua", one.Parameters[0]);

        // After sending, flipping back to Ready again doesn't send a second message.
        Assert.Equal(HttpStatusCode.OK, await SetStatusAsync(owner.Token, orderId, OrderStatus.Sewing));
        Assert.Equal(HttpStatusCode.OK, await SetStatusAsync(owner.Token, orderId, OrderStatus.Ready));
        await RunSenderAsync(factory);
        Assert.Single(fake.Sent, s => s.To == "233" + phone[1..]);
        var rows = await MessagesForAsync(factory, orderId);
        Assert.Equal(WhatsAppMessageState.Sent, Assert.Single(rows).State);

        using var log = await SendAsync(HttpMethod.Get, $"/api/whatsapp/messages?orderId={orderId}", owner.Token);
        var page = (await log.Content.ReadFromJsonAsync<WhatsAppMessagePageDto>(JsonOptions))!;
        var item = Assert.Single(page.Items);
        Assert.Equal(WhatsAppMessageState.Sent, item.State);
        Assert.DoesNotContain(phone[1..], item.MaskedPhone);
        Assert.EndsWith(phone[^4..], item.MaskedPhone);

        // Another business can't see this log or consent.
        var other = await RegisterAsync("wa1b");
        using var foreign = await SendAsync(HttpMethod.Get, $"/api/whatsapp/messages?orderId={orderId}", other.Token);
        Assert.Empty((await foreign.Content.ReadFromJsonAsync<WhatsAppMessagePageDto>(JsonOptions))!.Items);
        await AssertStatus(HttpStatusCode.NotFound, HttpMethod.Get, $"/api/whatsapp/customers/{customerId}/consent", other.Token);
    }

    [Fact]
    public async Task WhatsApp_QuickFlip_IsSkipped_OptOut_IsRespected_AndFailuresRetryThenStop()
    {
        var (factory, fake) = WhatsAppFactory();
        var owner = await RegisterAsync("wa2");
        await AssertStatus(HttpStatusCode.OK, HttpMethod.Put, "/api/whatsapp/settings", owner.Token,
            new UpdateWhatsAppSettingsRequest { Enabled = true, Statuses = ["Ready", "Fitting"] });

        var (orderId, customerId) = await WhatsAppOrderAsync(owner.Token, RandomGhanaPhone());
        await AssertStatus(HttpStatusCode.OK, HttpMethod.Put, $"/api/whatsapp/customers/{customerId}/consent", owner.Token,
            new SetWhatsAppConsentRequest { OptedIn = true });

        // Ready, then Collected before the sender runs: the Ready message is skipped (no stale "ready" text).
        Assert.Equal(HttpStatusCode.OK, await SetStatusAsync(owner.Token, orderId, OrderStatus.Ready));
        Assert.Equal(HttpStatusCode.OK, await SetStatusAsync(owner.Token, orderId, OrderStatus.Collected));
        var before = fake.Sent.Count;
        await RunSenderAsync(factory);
        Assert.Equal(before, fake.Sent.Count);
        Assert.Equal(WhatsAppMessageState.Skipped, Assert.Single(await MessagesForAsync(factory, orderId)).State);

        // Opt-out: Fitting on a new order queues nothing.
        var (order2, customer2) = await WhatsAppOrderAsync(owner.Token, RandomGhanaPhone());
        await AssertStatus(HttpStatusCode.OK, HttpMethod.Put, $"/api/whatsapp/customers/{customer2}/consent", owner.Token,
            new SetWhatsAppConsentRequest { OptedIn = true });
        await AssertStatus(HttpStatusCode.OK, HttpMethod.Put, $"/api/whatsapp/customers/{customer2}/consent", owner.Token,
            new SetWhatsAppConsentRequest { OptedIn = false });
        Assert.Equal(HttpStatusCode.OK, await SetStatusAsync(owner.Token, order2, OrderStatus.Fitting));
        Assert.Empty(await MessagesForAsync(factory, order2));

        // Transient failure retries later; a permanent one stops.
        await AssertStatus(HttpStatusCode.OK, HttpMethod.Put, $"/api/whatsapp/customers/{customer2}/consent", owner.Token,
            new SetWhatsAppConsentRequest { OptedIn = true });
        Assert.Equal(HttpStatusCode.OK, await SetStatusAsync(owner.Token, order2, OrderStatus.Ready));
        fake.Next = () => WhatsAppSendResult.Failed("429: rate", permanent: false);
        await RunSenderAsync(factory);
        var row = Assert.Single(await MessagesForAsync(factory, order2));
        Assert.Equal(WhatsAppMessageState.Pending, row.State);
        Assert.Equal(1, row.Attempts);
        Assert.True(row.NotBeforeUtc > DateTimeOffset.UtcNow);

        fake.Next = () => WhatsAppSendResult.Failed("400 code 132001: template does not exist", permanent: true);
        await RunSenderAsync(factory, TimeSpan.FromMinutes(2));
        row = Assert.Single(await MessagesForAsync(factory, order2));
        Assert.Equal(WhatsAppMessageState.Failed, row.State);
        fake.Next = null;
    }

    [Fact]
    public async Task WhatsApp_Webhook_ChecksSignature_UpdatesDelivery_AndStopOptsOut()
    {
        var (factory, _) = WhatsAppFactory();
        var owner = await RegisterAsync("wa3");
        await AssertStatus(HttpStatusCode.OK, HttpMethod.Put, "/api/whatsapp/settings", owner.Token,
            new UpdateWhatsAppSettingsRequest { Enabled = true, Statuses = ["Ready"] });
        var phone = RandomGhanaPhone();
        var (orderId, customerId) = await WhatsAppOrderAsync(owner.Token, phone);
        await AssertStatus(HttpStatusCode.OK, HttpMethod.Put, $"/api/whatsapp/customers/{customerId}/consent", owner.Token,
            new SetWhatsAppConsentRequest { OptedIn = true });
        Assert.Equal(HttpStatusCode.OK, await SetStatusAsync(owner.Token, orderId, OrderStatus.Ready));
        await RunSenderAsync(factory);
        var wamid = Assert.Single(await MessagesForAsync(factory, orderId)).ProviderMessageId!;

        using (var verify = await _client.GetAsync($"/api/whatsapp/webhook?hub.mode=subscribe&hub.verify_token={TestVerifyToken}&hub.challenge=4242"))
        {
            Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
            Assert.Equal("4242", await verify.Content.ReadAsStringAsync());
        }

        using (var wrong = await _client.GetAsync("/api/whatsapp/webhook?hub.mode=subscribe&hub.verify_token=nope&hub.challenge=4242"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
        }

        var digits = "233" + phone[1..];
        var body = "{\"object\":\"whatsapp_business_account\",\"entry\":[{\"id\":\"1\",\"changes\":[{\"field\":\"messages\",\"value\":{" +
                   "\"statuses\":[{\"id\":\"" + wamid + "\",\"status\":\"delivered\",\"recipient_id\":\"" + digits + "\"}]," +
                   "\"messages\":[{\"from\":\"" + digits + "\",\"id\":\"wamid.in\",\"type\":\"text\",\"text\":{\"body\":\" stop \"}}]}}]}]}";

        using (var unsigned = await PostHookAsync(body, "sha256=00"))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, unsigned.StatusCode);
        }

        Assert.Equal(WhatsAppMessageState.Sent, Assert.Single(await MessagesForAsync(factory, orderId)).State);

        using (var signed = await PostHookAsync(body, Sign(body)))
        {
            Assert.Equal(HttpStatusCode.OK, signed.StatusCode);
        }

        Assert.Equal(WhatsAppMessageState.Delivered, Assert.Single(await MessagesForAsync(factory, orderId)).State);
        using (var consent = await SendAsync(HttpMethod.Get, $"/api/whatsapp/customers/{customerId}/consent", owner.Token))
        {
            var dto = (await consent.Content.ReadFromJsonAsync<WhatsAppConsentDto>(JsonOptions))!;
            Assert.False(dto.OptedIn);
            Assert.Equal("reply", dto.Source);
        }

        // A phone's offline opt-in made before the STOP reply arrives late: the STOP wins and is returned.
        using (var late0 = await SendAsync(HttpMethod.Put, $"/api/whatsapp/customers/{customerId}/consent", owner.Token,
                   new SetWhatsAppConsentRequest { OptedIn = true, ChangedAtUtc = DateTimeOffset.UtcNow.AddHours(-1) }))
        {
            Assert.Equal(HttpStatusCode.OK, late0.StatusCode);
            var dto = (await late0.Content.ReadFromJsonAsync<WhatsAppConsentDto>(JsonOptions))!;
            Assert.False(dto.OptedIn);
            Assert.Equal("reply", dto.Source);
        }

        // A device clock in the future is clamped to server time (it can't lock out later changes).
        using (var future = await SendAsync(HttpMethod.Put, $"/api/whatsapp/customers/{customerId}/consent", owner.Token,
                   new SetWhatsAppConsentRequest { OptedIn = true, ChangedAtUtc = DateTimeOffset.UtcNow.AddDays(30) }))
        {
            var dto = (await future.Content.ReadFromJsonAsync<WhatsAppConsentDto>(JsonOptions))!;
            Assert.True(dto.OptedIn);
            Assert.True(dto.ChangedAtUtc < DateTimeOffset.UtcNow.AddMinutes(1));
        }

        // Meta re-delivers the old STOP (with its original time, before the opt-in above): the newer opt-in stands.
        var oldStopAt = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds();
        var replay = "{\"entry\":[{\"changes\":[{\"value\":{\"messages\":[{\"from\":\"" + digits + "\",\"timestamp\":\"" + oldStopAt +
                     "\",\"text\":{\"body\":\"STOP\"}}]}}]}]}";
        using (var r = await PostHookAsync(replay, Sign(replay)))
        {
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        }

        using (var consent = await SendAsync(HttpMethod.Get, $"/api/whatsapp/customers/{customerId}/consent", owner.Token))
        {
            Assert.True((await consent.Content.ReadFromJsonAsync<WhatsAppConsentDto>(JsonOptions))!.OptedIn);
        }

        // A phone whose clock is 2 hours slow turns it off just now: corrected by its clock error, so it is newer and applies.
        var slowNow = DateTimeOffset.UtcNow.AddHours(-2);
        using (var slow = await SendAsync(HttpMethod.Put, $"/api/whatsapp/customers/{customerId}/consent", owner.Token,
                   new SetWhatsAppConsentRequest { OptedIn = false, ChangedAtUtc = slowNow, DeviceNowUtc = slowNow }))
        {
            var dto = (await slow.Content.ReadFromJsonAsync<WhatsAppConsentDto>(JsonOptions))!;
            Assert.False(dto.OptedIn);
            Assert.Equal("staff", dto.Source);
            Assert.True(dto.ChangedAtUtc > DateTimeOffset.UtcNow.AddMinutes(-1));
        }

        // A late "sent" after "delivered" doesn't move it back.
        var late = "{\"entry\":[{\"changes\":[{\"value\":{\"statuses\":[{\"id\":\"" + wamid + "\",\"status\":\"sent\"}]}}]}]}";
        using (var r = await PostHookAsync(late, Sign(late)))
        {
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        }

        Assert.Equal(WhatsAppMessageState.Delivered, Assert.Single(await MessagesForAsync(factory, orderId)).State);
    }

    [Fact]
    public async Task WhatsApp_Opened_IsAudited_ForAnyMember_AndUnknownOrderIs404()
    {
        WhatsAppFactory(configured: false);
        var owner = await RegisterAsync("wa4");
        var orderId = await CreateOrderAsync(owner.Token, null);
        var tailor = await AddMemberAsync(owner.Token, BusinessRole.Tailor);
        await AssertStatus(HttpStatusCode.NoContent, HttpMethod.Post, $"/api/whatsapp/orders/{orderId}/opened", tailor.Tokens.AccessToken!,
            new WhatsAppOpenedRequest { Status = OrderStatus.Ready });
        await AssertStatus(HttpStatusCode.NotFound, HttpMethod.Post, "/api/whatsapp/orders/01JUNKJUNKJUNKJUNKJUNKJUNK/opened", owner.Token,
            new WhatsAppOpenedRequest());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var audit = await db.AuditEvents.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.BusinessId == owner.BusinessId && a.Action == NeedleBase.Application.Audit.AuditActions.WhatsAppOpened).ToListAsync();
        Assert.Single(audit);
    }

    [Fact]
    public async Task WhatsApp_PhoneStatusChange_ViaSync_QueuesOnce_EvenWhenPushedAgain()
    {
        var (factory, fake) = WhatsAppFactory();
        var owner = await RegisterAsync("wa5");
        await AssertStatus(HttpStatusCode.OK, HttpMethod.Put, "/api/whatsapp/settings", owner.Token,
            new UpdateWhatsAppSettingsRequest { Enabled = true, Statuses = ["Ready"] });
        var phone = RandomGhanaPhone();
        var (orderId, customerId) = await WhatsAppOrderAsync(owner.Token, phone);
        await AssertStatus(HttpStatusCode.OK, HttpMethod.Put, $"/api/whatsapp/customers/{customerId}/consent", owner.Token,
            new SetWhatsAppConsentRequest { OptedIn = true });

        var pulled = await PullOrderAsync(owner.Token, orderId);
        var first = await PushOrderAsync(owner.Token, pulled with { Status = OrderStatus.Ready }, pulled.DateModifiedUtc);
        Assert.Equal(NeedleBase.Application.Contracts.Sync.SyncOpStatus.Applied, first.Status);
        // The phone didn't hear back and pushes the same change again (new op, same status).
        await PushOrderAsync(owner.Token, first.Order! with { Status = OrderStatus.Ready }, first.Order!.DateModifiedUtc);
        Assert.Single(await MessagesForAsync(factory, orderId));
        await RunSenderAsync(factory);
        Assert.Single(fake.Sent, s => s.To == "233" + phone[1..]);
    }

    private async Task<HttpResponseMessage> PostHookAsync(string body, string signature)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/whatsapp/webhook")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Hub-Signature-256", signature);
        return await _client.SendAsync(request);
    }

    private static string Sign(string body) =>
        "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(TestAppSecret), Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
}
