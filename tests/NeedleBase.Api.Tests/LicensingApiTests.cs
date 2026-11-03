using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NeedleBase.Application.Contracts.Auth;
using NeedleBase.Application.Contracts.Customers;
using NeedleBase.Application.Contracts.Licensing;
using NeedleBase.Application.Contracts.Sync;
using NeedleBase.Application.Licensing;
using NeedleBase.Domain.Entities;
using NeedleBase.Infrastructure.Licensing;
using NeedleBase.Infrastructure.Persistence;
using Xunit;

namespace NeedleBase.Api.Tests;

/// <summary>Fake Platform: answers from <see cref="Answer"/>, records every call, never touches the network.</summary>
public sealed class FakeLicensePlatform : ILicensePlatform
{
    public List<LicenseValidateCall> Calls { get; } = [];
    public Func<LicenseValidateCall, LicensePlatformOutcome> Answer { get; set; } = _ => Valid();
    public bool IsConfigured { get; set; } = true;

    public Task<LicensePlatformOutcome> ValidateAsync(LicenseValidateCall call, CancellationToken cancellationToken = default)
    {
        lock (Calls)
        {
            Calls.Add(call);
        }

        return Task.FromResult(Answer(call));
    }

    public static LicensePlatformOutcome Valid(bool? emailMatches = true, bool trial = false) =>
        LicensePlatformOutcome.Judged(new LicenseCheck(true, "ok", "lic-1", "Standard", trial, false,
            DateTimeOffset.UtcNow.AddDays(200), DateTimeOffset.UtcNow.AddHours(12), DateTimeOffset.UtcNow.AddDays(3), "header.payload.sig"), emailMatches);

    public static LicensePlatformOutcome Invalid(string reason, bool? emailMatches = true) =>
        LicensePlatformOutcome.Judged(new LicenseCheck(false, reason), emailMatches);
}

/// <summary>Platform licensing end to end with a fake Platform (registration, attach, read-only, sync, re-check).</summary>
[Collection(SyncEpochCollection.Name)]
public sealed class LicensingApiTests : IClassFixture<NeedleBaseApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly NeedleBaseApiFactory _factory;

    public LicensingApiTests(NeedleBaseApiFactory factory)
    {
        _factory = factory;
    }

    private (WebApplicationFactory<Program> Factory, HttpClient Client, FakeLicensePlatform Platform) Licensed(bool requireKey = true)
    {
        var fake = new FakeLicensePlatform();
        var factory = _factory.WithWebHostBuilder(b =>
        {
            // Added after the shared factory's in-memory settings, so these win.
            b.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Platform:RequireLicenseKey"] = requireKey ? "true" : "false",
                ["Platform:LockGraceDays"] = "7"
            }));
            b.ConfigureTestServices(s =>
            {
                s.AddSingleton<ILicensePlatform>(fake);
                foreach (var d in s.Where(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(LicenseRecheckWorker)).ToList())
                {
                    s.Remove(d);
                }
            });
        });
        return (factory, factory.CreateClient(), fake);
    }

    private static RegisterBusinessRequest Request(string email, string? key) =>
        new(email, "Passw0rd!", "Ama", "Owusu", "Licensed Stitches", "+233200000000", key);

    private static string NewEmail(string tag) => $"lic.{tag}.{Guid.NewGuid():N}@example.com";

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("code", out var c) ? c.GetString() : null;
    }

    private static async Task<(string Token, string BusinessId)> RegisterOkAsync(HttpClient client, string email, string? key)
    {
        using var response = await client.PostAsJsonAsync("/api/auth/register-business", Request(email, key), Json);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (doc.RootElement.GetProperty("accessToken").GetString()!,
            doc.RootElement.GetProperty("user").GetProperty("businessId").GetString()!);
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: Json);
        }

        return request;
    }

    private static async Task<bool> UserExistsAsync(WebApplicationFactory<Program> factory, string email)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Users.AnyAsync(u => u.NormalizedEmail == email.ToUpperInvariant());
    }

    private static async Task MakeInvalidAsync(WebApplicationFactory<Program> factory, string businessId, DateTimeOffset since, DateTimeOffset? graceUntil = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var business = await db.Businesses.SingleAsync(b => b.Id == businessId);
        if (graceUntil is not null)
        {
            business.RecordLicenseCheck(new LicenseCheck(true, "ok", "lic-1", GraceUntilUtc: graceUntil), since.AddDays(-1));
        }

        business.RecordLicenseCheck(new LicenseCheck(false, "expired"), since);
        await db.SaveChangesAsync();
        // The invalid clock keeps its first date on purpose; tests move it explicitly.
        await db.Businesses.Where(b => b.Id == businessId)
            .ExecuteUpdateAsync(u => u.SetProperty(b => b.LicenseInvalidSinceUtc, since)
                .SetProperty(b => b.LicenseGraceUntilUtc, graceUntil ?? since));
        scope.ServiceProvider.GetRequiredService<LicenseService>().Invalidate(businessId);
    }

    [Fact]
    public async Task Register_WithoutKey_WhenRequired_IsRefused_BeforePlatform()
    {
        var (factory, client, platform) = Licensed();
        var email = NewEmail("nokey");
        using var response = await client.PostAsJsonAsync("/api/auth/register-business", Request(email, "  "), Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(LicenseErrors.KeyRequired, await CodeAsync(response));
        Assert.Empty(platform.Calls);
        Assert.False(await UserExistsAsync(factory, email));
    }

    [Fact]
    public async Task Register_ValidKeyAndEmail_CreatesShop_StoresKeyEncrypted_ShopIdIsDeviceId()
    {
        var (factory, client, platform) = Licensed();
        var email = NewEmail("ok");
        const string key = "NB-TEST-KEY-0001-ABCD";
        var (token, businessId) = await RegisterOkAsync(client, email, " " + key + " ");

        var call = Assert.Single(platform.Calls);
        Assert.Equal(key, call.LicenseKey);
        Assert.Equal(businessId, call.DeviceId);
        Assert.Equal(email, call.OwnerEmail);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var business = await db.Businesses.AsNoTracking().SingleAsync(b => b.Id == businessId);
            Assert.NotNull(business.LicenseKeyProtected);
            Assert.DoesNotContain(key, business.LicenseKeyProtected);
            Assert.Equal("…ABCD", business.LicenseKeyHint);
            Assert.True(business.LicenseIsValid);
            Assert.Equal(key, scope.ServiceProvider.GetRequiredService<LicenseService>().TryUnprotect(business.LicenseKeyProtected));
            // The key never reaches the activity log.
            var audit = await db.AuditEvents.IgnoreQueryFilters().AsNoTracking().Where(a => a.BusinessId == businessId).ToListAsync();
            Assert.NotEmpty(audit);
            Assert.DoesNotContain(audit, a => (a.ChangesJson ?? "").Contains("License") || a.Summary.Contains(key));
        }

        using var status = await client.SendAsync(Authorized(HttpMethod.Get, "/api/subscription", token));
        var dto = await status.Content.ReadFromJsonAsync<SubscriptionStatusDto>(Json);
        Assert.Equal(SubscriptionStates.Active, dto!.State);
        Assert.False(dto.IsReadOnly);
        Assert.Equal("…ABCD", dto.KeyHint);
    }

    [Theory]
    [InlineData("invalid_key", LicenseErrors.KeyInvalid)]
    [InlineData("device_limit", LicenseErrors.KeyInUse)]
    [InlineData("expired", LicenseErrors.Expired)]
    [InlineData("suspended", LicenseErrors.Suspended)]
    [InlineData("revoked", LicenseErrors.Revoked)]
    [InlineData("not_active", LicenseErrors.NotActive)]
    public async Task Register_KeyProblems_AreRefused_WithTheirOwnCode(string reason, string code)
    {
        var (factory, client, platform) = Licensed();
        platform.Answer = _ => FakeLicensePlatform.Invalid(reason);
        var email = NewEmail(reason);
        using var response = await client.PostAsJsonAsync("/api/auth/register-business", Request(email, "NB-KEY"), Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, await CodeAsync(response));
        Assert.False(await UserExistsAsync(factory, email));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(null)] // Platform didn't answer the email question: refuse rather than guess.
    public async Task Register_EmailMismatchOrMissing_IsRefused_AsEmailProblem(bool? matches)
    {
        var (factory, client, platform) = Licensed();
        platform.Answer = _ => FakeLicensePlatform.Valid(emailMatches: matches);
        var email = NewEmail("mismatch");
        using var response = await client.PostAsJsonAsync("/api/auth/register-business", Request(email, "NB-KEY"), Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(LicenseErrors.EmailMismatch, await CodeAsync(response));
        Assert.False(await UserExistsAsync(factory, email));
    }

    [Fact]
    public async Task Register_PlatformUnavailable_Is503WithRetryAfter_AndRetryReusesShopId()
    {
        var (factory, client, platform) = Licensed();
        platform.Answer = _ => LicensePlatformOutcome.NotAvailable("service_unavailable", TimeSpan.FromSeconds(42));
        var email = NewEmail("down");
        using (var response = await client.PostAsJsonAsync("/api/auth/register-business", Request(email, "NB-KEY-DOWN"), Json))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal(LicenseErrors.CheckUnavailable, await CodeAsync(response));
            Assert.Equal(TimeSpan.FromSeconds(42), response.Headers.RetryAfter?.Delta);
        }

        Assert.False(await UserExistsAsync(factory, email));
        platform.Answer = _ => FakeLicensePlatform.Valid();
        var (_, businessId) = await RegisterOkAsync(client, email, "NB-KEY-DOWN");
        Assert.Equal(2, platform.Calls.Count);
        Assert.All(platform.Calls, c => Assert.Equal(businessId, c.DeviceId));
    }

    [Fact]
    public async Task Register_ExistingEmail_OrWeakPassword_NeverCallsPlatform()
    {
        var (_, client, platform) = Licensed();
        var email = NewEmail("dupe");
        await RegisterOkAsync(client, email, "NB-KEY-DUPE");
        platform.Calls.Clear();

        using (var again = await client.PostAsJsonAsync("/api/auth/register-business", Request(email, "NB-KEY-OTHER"), Json))
        {
            Assert.Equal("REGISTER_FAILED", await CodeAsync(again));
        }

        var weak = Request(NewEmail("weak"), "NB-KEY-WEAK");
        weak.Password = "abcdef"; // passes the DTO length check, fails Identity's rules
        using (var response = await client.PostAsJsonAsync("/api/auth/register-business", weak, Json))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        Assert.Empty(platform.Calls);
    }

    [Fact]
    public async Task Register_PlatformNotConfigured_IsRefused_NotGuessed()
    {
        var (factory, client, platform) = Licensed();
        platform.IsConfigured = false;
        var email = NewEmail("unconfigured");
        using var response = await client.PostAsJsonAsync("/api/auth/register-business", Request(email, "NB-KEY"), Json);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(LicenseErrors.NotConfigured, await CodeAsync(response));
        Assert.Empty(platform.Calls);
        Assert.False(await UserExistsAsync(factory, email));
    }

    [Fact]
    public async Task ReadOnly_BlocksNewWork_ButReadsSyncAndStatusWork()
    {
        var (factory, client, _) = Licensed();
        var (token, businessId) = await RegisterOkAsync(client, NewEmail("ro"), "NB-KEY-RO");

        // Not valid for 3 days: still in our 7-day grace -> banner, full use.
        await MakeInvalidAsync(factory, businessId, DateTimeOffset.UtcNow.AddDays(-3));
        using (var create = await client.SendAsync(Authorized(HttpMethod.Post, "/api/customers", token,
                   new CreateCustomerRequest("Grace Customer", "+233245550101", null, null))))
        {
            Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        }

        await MakeInvalidAsync(factory, businessId, DateTimeOffset.UtcNow.AddDays(-30));
        using (var create = await client.SendAsync(Authorized(HttpMethod.Post, "/api/customers", token,
                   new CreateCustomerRequest("Locked Customer", "+233245550102", null, null))))
        {
            Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
            Assert.Equal(LicenseErrors.ReadOnly, await CodeAsync(create));
        }

        using (var list = await client.SendAsync(Authorized(HttpMethod.Get, "/api/customers", token)))
        {
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        }

        using (var pull = await client.SendAsync(Authorized(HttpMethod.Post, "/api/sync/pull", token, new SyncPullRequest { Limit = 50 })))
        {
            Assert.Equal(HttpStatusCode.OK, pull.StatusCode);
            var page = await pull.Content.ReadFromJsonAsync<SyncPullResponse>(Json);
            Assert.NotNull(page!.Subscription);
            Assert.True(page.Subscription!.IsReadOnly);
            Assert.Equal(SubscriptionStates.ReadOnly, page.Subscription.State);
        }

        using (var push = await client.SendAsync(Authorized(HttpMethod.Post, "/api/sync/push", token, new SyncPushRequest())))
        {
            Assert.NotEqual(HttpStatusCode.Forbidden, push.StatusCode);
        }

        // Stopping WhatsApp messages / opt-outs still work while paused (unknown customer: past the guard -> 404).
        using (var consent = await client.SendAsync(Authorized(HttpMethod.Put, "/api/whatsapp/customers/01JNOSUCHCUSTOMER0000000001/consent", token,
                   new NeedleBase.Application.Contracts.WhatsApp.SetWhatsAppConsentRequest { OptedIn = false })))
        {
            Assert.NotEqual(HttpStatusCode.Forbidden, consent.StatusCode);
        }
    }

    [Fact]
    public async Task ReadOnly_WaitsForTokenGrace_WhenLongerThanOurs()
    {
        var (factory, client, _) = Licensed();
        var (token, businessId) = await RegisterOkAsync(client, NewEmail("grace"), "NB-KEY-GRACE");
        await MakeInvalidAsync(factory, businessId, DateTimeOffset.UtcNow.AddDays(-30), graceUntil: DateTimeOffset.UtcNow.AddDays(2));

        using var status = await client.SendAsync(Authorized(HttpMethod.Get, "/api/subscription", token));
        var dto = await status.Content.ReadFromJsonAsync<SubscriptionStatusDto>(Json);
        Assert.Equal(SubscriptionStates.Problem, dto!.State);
        Assert.False(dto.IsReadOnly);
    }

    [Fact]
    public async Task Attach_ExistingShop_UsesOwnersEmail_AndUnlicensedShopIsNeverLocked()
    {
        var (factory, client, platform) = Licensed(requireKey: false);
        var email = NewEmail("attach");
        var (token, businessId) = await RegisterOkAsync(client, email, null);
        Assert.Empty(platform.Calls);

        using (var status = await client.SendAsync(Authorized(HttpMethod.Get, "/api/subscription", token)))
        {
            var dto = await status.Content.ReadFromJsonAsync<SubscriptionStatusDto>(Json);
            Assert.Equal(SubscriptionStates.NotAttached, dto!.State);
            Assert.False(dto.IsReadOnly);
            Assert.True(dto.OwnerOnly);
        }

        platform.Answer = _ => FakeLicensePlatform.Valid(emailMatches: false);
        using (var refused = await client.SendAsync(Authorized(HttpMethod.Post, "/api/subscription/license", token, new AttachLicenseRequest { LicenseKey = "NB-ATTACH-1" })))
        {
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Equal(LicenseErrors.EmailMismatch, await CodeAsync(refused));
        }

        platform.Answer = _ => FakeLicensePlatform.Valid(trial: true);
        using (var ok = await client.SendAsync(Authorized(HttpMethod.Post, "/api/subscription/license", token, new AttachLicenseRequest { LicenseKey = "NB-ATTACH-1" })))
        {
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            var dto = await ok.Content.ReadFromJsonAsync<SubscriptionStatusDto>(Json);
            Assert.Equal(SubscriptionStates.Trial, dto!.State);
        }

        Assert.All(platform.Calls, c =>
        {
            Assert.Equal(businessId, c.DeviceId);
            Assert.Equal(email, c.OwnerEmail);
        });
    }

    [Fact]
    public async Task Recheck_NetworkTrouble_NeverChangesLicense_ButDefiniteAnswersDo()
    {
        var (factory, client, platform) = Licensed();
        var (_, businessId) = await RegisterOkAsync(client, NewEmail("recheck"), "NB-KEY-RECHECK");

        async Task<Business> RunAsync(Func<LicenseValidateCall, LicensePlatformOutcome> answer)
        {
            platform.Answer = answer;
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            // Only this shop is due (other tests' shops would otherwise go first and a "busy" answer stops the pass).
            await db.Businesses.Where(b => b.LicenseKeyProtected != null && b.Id != businessId)
                .ExecuteUpdateAsync(u => u.SetProperty(b => b.LicenseNextCheckUtc, DateTimeOffset.UtcNow.AddDays(1)));
            await db.Businesses.Where(b => b.Id == businessId)
                .ExecuteUpdateAsync(u => u.SetProperty(b => b.LicenseNextCheckUtc, DateTimeOffset.UtcNow.AddMinutes(-1)));
            await scope.ServiceProvider.GetRequiredService<LicenseService>().RecheckDueAsync(500, CancellationToken.None);
            return await db.Businesses.AsNoTracking().SingleAsync(b => b.Id == businessId);
        }

        var b1 = await RunAsync(_ => LicensePlatformOutcome.NotAvailable("rate_limited", TimeSpan.FromMinutes(90)));
        Assert.True(b1.LicenseIsValid);
        Assert.Null(b1.LicenseInvalidSinceUtc);
        Assert.True(b1.LicenseNextCheckUtc > DateTimeOffset.UtcNow.AddMinutes(80));

        var b2 = await RunAsync(_ => FakeLicensePlatform.Invalid("suspended", emailMatches: null));
        Assert.False(b2.LicenseIsValid);
        Assert.NotNull(b2.LicenseInvalidSinceUtc);
        Assert.Equal("suspended", b2.LicenseReason);

        var b3 = await RunAsync(_ => FakeLicensePlatform.Valid(emailMatches: null)); // re-checks send no email
        Assert.True(b3.LicenseIsValid);
        Assert.Null(b3.LicenseInvalidSinceUtc);
        Assert.Contains(platform.Calls, c => c.DeviceId == businessId && c.OwnerEmail is null);
    }

    private static async Task IsolateDueAsync(WebApplicationFactory<Program> factory, string businessId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Businesses.Where(b => b.LicenseKeyProtected != null && b.Id != businessId)
            .ExecuteUpdateAsync(u => u.SetProperty(b => b.LicenseNextCheckUtc, DateTimeOffset.UtcNow.AddDays(1)));
        await db.Businesses.Where(b => b.Id == businessId)
            .ExecuteUpdateAsync(u => u.SetProperty(b => b.LicenseNextCheckUtc, DateTimeOffset.UtcNow.AddMinutes(-1)));
    }

    private static async Task<int> RecheckAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<LicenseService>().RecheckDueAsync(500, CancellationToken.None);
    }

    [Fact]
    public async Task Recheck_OverlappingPasses_AskPlatformOnlyOncePerShop()
    {
        var (factory, client, platform) = Licensed();
        var (_, businessId) = await RegisterOkAsync(client, NewEmail("overlap"), "NB-KEY-OVERLAP");
        await IsolateDueAsync(factory, businessId);
        platform.Answer = _ =>
        {
            Thread.Sleep(300);
            return FakeLicensePlatform.Valid(emailMatches: null);
        };

        await Task.WhenAll(Task.Run(() => RecheckAsync(factory)), Task.Run(() => RecheckAsync(factory)));

        Assert.Single(platform.Calls, c => c.DeviceId == businessId && c.OwnerEmail is null);
    }

    [Fact]
    public async Task Recheck_KeyReplacedWhilePlatformAnswers_KeepsTheNewKeysAnswer()
    {
        var (factory, client, platform) = Licensed();
        var (_, businessId) = await RegisterOkAsync(client, NewEmail("replaced"), "NB-KEY-REPLACED");
        await IsolateDueAsync(factory, businessId);
        platform.Answer = _ =>
        {
            // The Owner attaches another key (stand-in: the stored key changes) before the old key's "revoked" lands.
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var protector = scope.ServiceProvider.GetRequiredService<LicenseService>();
            var newKey = protector.Protect("NB-KEY-REPLACED-2");
            db.Businesses.Where(b => b.Id == businessId)
                .ExecuteUpdate(u => u.SetProperty(b => b.LicenseKeyProtected, newKey));
            return FakeLicensePlatform.Invalid("revoked", emailMatches: null);
        };

        await RecheckAsync(factory);

        using var check = factory.Services.CreateScope();
        var business = await check.ServiceProvider.GetRequiredService<ApplicationDbContext>().Businesses.AsNoTracking()
            .SingleAsync(b => b.Id == businessId);
        Assert.True(business.LicenseIsValid);
        Assert.Null(business.LicenseInvalidSinceUtc);
    }

    [Theory]
    [InlineData("NB\u0001KEY")]
    [InlineData("  ")]
    public async Task CheckKey_RefusesBadKeysWithoutCallingPlatform(string raw)
    {
        var key = System.Text.RegularExpressions.Regex.Unescape(raw);
        var (factory, _, platform) = Licensed();
        using var scope = factory.Services.CreateScope();
        var (check, code, _) = await scope.ServiceProvider.GetRequiredService<LicenseService>()
            .CheckKeyAsync(key, "01JSHOPBADKEY00000000000001", "Shop", "owner@example.com", CancellationToken.None);

        Assert.Null(check);
        Assert.Equal(key.Trim().Length == 0 ? LicenseErrors.KeyRequired : LicenseErrors.KeyInvalid, code);
        Assert.Empty(platform.Calls);
    }

    [Fact]
    public async Task Register_SameEmailTwiceAtOnce_OneWinsAndTheOtherGetsAPlainRefusal()
    {
        var (_, client, _) = Licensed();
        var email = NewEmail("race");
        var responses = await Task.WhenAll(
            client.PostAsJsonAsync("/api/auth/register-business", Request(email, "NB-KEY-RACE"), Json),
            client.PostAsJsonAsync("/api/auth/register-business", Request(email, "NB-KEY-RACE"), Json));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.BadRequest);
        foreach (var r in responses)
        {
            r.Dispose();
        }
    }
}
