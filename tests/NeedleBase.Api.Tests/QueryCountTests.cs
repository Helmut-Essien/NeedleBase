using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using NeedleBase.Infrastructure.Email;
using NeedleBase.Application.Contracts.Auth;
using NeedleBase.Application.Contracts.Errors;
using NeedleBase.Application.Contracts.Orders;
using NeedleBase.Application.Contracts.Staff;
using NeedleBase.Application.Contracts.Sync;
using NeedleBase.Domain.Enums;
using Xunit.Abstractions;

namespace NeedleBase.Api.Tests;

/// <summary>Counts SQL round trips per request (EF diagnostics, scoped to the calling test via AsyncLocal).</summary>
public static class QueryCounter
{
    private static readonly AsyncLocal<List<string>?> Current = new();
    private static int _subscribed;

    public static List<string> Start()
    {
        if (Interlocked.Exchange(ref _subscribed, 1) == 0)
        {
            DiagnosticListener.AllListeners.Subscribe(new Observer<DiagnosticListener>(l =>
            {
                if (l.Name == DbLoggerCategory.Name)
                {
                    l.Subscribe(new Observer<KeyValuePair<string, object?>>(e =>
                    {
                        if (e.Key == RelationalEventId.CommandExecuted.Name && e.Value is CommandExecutedEventData d)
                        {
                            var list = Current.Value;
                            if (list is not null)
                            {
                                lock (list)
                                {
                                    list.Add(d.Command.CommandText);
                                }
                            }
                        }
                    }));
                }
            }));
        }

        var fresh = new List<string>();
        Current.Value = fresh;
        return fresh;
    }

    public static void Stop() => Current.Value = null;

    private sealed class Observer<T>(Action<T> next) : IObserver<T>
    {
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void OnNext(T value) => next(value);
    }
}

public sealed partial class StaffApiTests
{
    [Fact]
    public async Task QueryCounts_OnHotStaffPaths_StayFlat()
    {
        var log = _output;
        var mail = new CapturingTransport();
        var factory = _factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Email:Host", "smtp.example.test");
            b.UseSetting("Email:Username", "shop@example.test");
            b.UseSetting("Email:Password", "not-a-real-password");
            b.UseTestServer(o => o.PreserveExecutionContext = true);
            b.ConfigureTestServices(s => s.AddSingleton<IEmailTransport>(mail));
        });
        _client = factory.CreateClient();
        var owner = await RegisterAsync("qc");
        var results = new Dictionary<string, int>();

        async Task<T> Count<T>(string name, Func<Task<T>> call)
        {
            var list = QueryCounter.Start();
            try
            {
                return await call();
            }
            finally
            {
                QueryCounter.Stop();
                lock (list)
                {
                    results[name] = list.Count;
                }
            }
        }

        await Count("auth only: GET /api/staff/names", async () => { using var r = await SendAsync(HttpMethod.Get, "/api/staff/names", owner.Token); return r.StatusCode; });
        var (kofiId, kofi) = await AddMemberAsync(owner.Token, BusinessRole.Tailor);
        await AddMemberAsync(owner.Token, BusinessRole.Manager);
        await Count("staff list (3 members)", async () => { using var r = await SendAsync(HttpMethod.Get, "/api/staff", owner.Token); return r.StatusCode; });
        for (var i = 0; i < 5; i++)
        {
            await AddMemberAsync(owner.Token, BusinessRole.Tailor);
        }

        await Count("staff list (8 members)", async () => { using var r = await SendAsync(HttpMethod.Get, "/api/staff", owner.Token); return r.StatusCode; });

        var invite = await InviteAsync(owner.Token, $"qc{Guid.NewGuid():N}"[..14] + "@ama-stitches.test", BusinessRole.Tailor, null);
        await Count("accept invite", () => AcceptAsync(invite.Code, "Esi", "Ofori", "Kofi1234"));

        await Count("change password", async () =>
        {
            using var r = await SendAsync(HttpMethod.Post, "/api/auth/change-password", kofi.AccessToken!,
                new ChangePasswordRequest { CurrentPassword = "Kofi1234", NewPassword = "Kofi5678" });
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            return r.StatusCode;
        });

        await Count("forgot password", async () =>
        {
            using var r = await _client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest { Email = owner.Email }, JsonOptions);
            return r.StatusCode;
        });
        var m = await mail.WaitForAsync(owner.Email, "password_reset");
        var code = Regex.Match(m.TextBody, @"\b(\d{4} \d{4})\b").Groups[1].Value;
        await Count("reset password", async () => { Assert.Null(await ResetAsync(owner.Email, code, "Fresh1234")); return 0; });
        using (var login = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(owner.Email, "Fresh1234"), JsonOptions))
        {
            owner = owner with { Token = (await login.Content.ReadFromJsonAsync<AuthPayload>(JsonOptions))!.AccessToken! };
        }

        // Tailor status change through sync push (tailor rules).
        using (var relogin = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(kofi.User!.Email, "Kofi5678"), JsonOptions))
        {
            kofi = (await relogin.Content.ReadFromJsonAsync<AuthPayload>(JsonOptions))!;
        }

        var mine = await CreateOrderAsync(owner.Token, kofiId);
        var pulled = await PullOrderAsync(kofi.AccessToken!, mine);
        await Count("sync push: tailor status change", async () =>
        {
            var result = await PushOrderAsync(kofi.AccessToken!, pulled with { Status = OrderStatus.Cutting }, pulled.DateModifiedUtc);
            Assert.Equal(SyncOpStatus.Applied, result.Status);
            return 0;
        });

        // Reassign: query count must not grow with the number of orders (no N+1).
        for (var i = 0; i < 2; i++)
        {
            await CreateOrderAsync(owner.Token, kofiId);
        }

        var (amaId, _) = await AddMemberAsync(owner.Token, BusinessRole.Manager);
        await Count("reassign 3 orders", async () =>
        {
            using var r = await SendAsync(HttpMethod.Post, $"/api/staff/{kofiId}/reassign-orders", owner.Token, new ReassignOrdersRequest { ToUserId = amaId });
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            return 0;
        });
        for (var i = 0; i < 9; i++)
        {
            await CreateOrderAsync(owner.Token, amaId);
        }

        await Count("reassign 12 orders", async () =>
        {
            using var r = await SendAsync(HttpMethod.Post, $"/api/staff/{amaId}/reassign-orders", owner.Token, new ReassignOrdersRequest { ToUserId = kofiId });
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            return 0;
        });

        foreach (var (name, n) in results)
        {
            log.WriteLine($"QUERYCOUNT {name}: {n}");
        }

        Assert.Equal(results["staff list (3 members)"], results["staff list (8 members)"]);
        Assert.Equal(results["reassign 3 orders"], results["reassign 12 orders"]);
    }

    [Fact]
    public async Task TailorOfflineCreate_PastNew_ForSomeoneElse_IsSavedAsNew_AndReportedAsConflict()
    {
        var owner = await RegisterAsync("tcreate");
        var (_, kofi) = await AddMemberAsync(owner.Token, BusinessRole.Tailor);
        var template = await PullOrderAsync(owner.Token, await CreateOrderAsync(owner.Token, null));
        var ownerId = (await OverviewAsync(owner.Token)).Members.Single(x => x.IsYou).UserId;
        var id = Guid.NewGuid().ToString("N")[..26];
        var created = template with
        {
            Id = id,
            Status = OrderStatus.Sewing,
            AssignedToUserId = ownerId,
            Lines = template.Lines.Select(l => l with { Id = Guid.NewGuid().ToString("N")[..26] }).ToList()
        };

        var result = await PushOrderAsync(kofi.AccessToken!, created, null);
        Assert.Equal(SyncOpStatus.Conflict, result.Status);
        Assert.Equal(OrderErrorMessages.NotAssigned, result.Code);
        Assert.NotNull(result.Order);
        Assert.Equal(OrderStatus.New, result.Order!.Status); // saved, status as the shop has it

        // "Use the shop's copy" or a re-push of the same op: no stuck failure, same order.
        var again = await PushOrderAsync(kofi.AccessToken!, result.Order with { }, result.Order.DateModifiedUtc);
        Assert.True(again.Status == SyncOpStatus.Applied, again.Status + " " + again.Code + " " + again.Message);
    }

    [Fact]
    public async Task ForgotPassword_TurnedOffMember_GetsNoEmail_SameReply()
    {
        var mail = UseEmail();
        var owner = await RegisterAsync("offreset");
        var (kofiId, kofi) = await AddMemberAsync(owner.Token, BusinessRole.Tailor);
        using (var off = await SendAsync(HttpMethod.Post, $"/api/staff/{kofiId}/deactivate", owner.Token))
        {
            Assert.True(off.IsSuccessStatusCode);
        }

        using var r = await _client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest { Email = kofi.User!.Email }, JsonOptions);
        Assert.Equal(HttpStatusCode.Accepted, r.StatusCode);
        await Task.Delay(300);
        Assert.DoesNotContain(mail.Sent, x => x.ToAddress == kofi.User.Email && x.Kind == "password_reset");
    }

    /// <summary>Another test's epoch bump answers 503 for a moment; wait for it and retry.</summary>
    private async Task<HttpResponseMessage> SyncCallAsync(string url, string token, object body)
    {
        for (var attempt = 0; ; attempt++)
        {
            var response = await SendAsync(HttpMethod.Post, url, token, body);
            if (response.StatusCode != HttpStatusCode.ServiceUnavailable || attempt >= 20)
            {
                return response;
            }

            response.Dispose();
            await NeedleBase.Infrastructure.Persistence.ApplicationDbContext.WaitForEpochBumpAsync().WaitAsync(TimeSpan.FromSeconds(60));
            await Task.Delay(50);
        }
    }

    private async Task<SyncOrderDto> PullOrderAsync(string token, string orderId)
    {
        string? cursor = null;
        while (true)
        {
            using var response = await SyncCallAsync("/api/sync/pull", token, new SyncPullRequest { Cursor = cursor });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var page = (await response.Content.ReadFromJsonAsync<SyncPullResponse>(JsonOptions))!;
            if (page.Orders.FirstOrDefault(o => o.Id == orderId) is { } hit)
            {
                return hit;
            }

            Assert.True(page.HasMore, "order not in pull");
            cursor = page.Cursor;
        }
    }

    private async Task<SyncPushResult> PushOrderAsync(string token, SyncOrderDto order, DateTimeOffset? baseModified)
    {
        using var response = await SyncCallAsync("/api/sync/push", token, new SyncPushRequest
        {
            Operations =
            [
                new SyncPushOperation
                {
                    OpId = Guid.NewGuid().ToString("N")[..26], EntityType = SyncEntityType.Order, EntityId = order.Id,
                    Operation = SyncOperation.Upsert, BaseModifiedUtc = baseModified, Order = order
                }
            ]
        });
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = (await response.Content.ReadFromJsonAsync<SyncPushResponse>(JsonOptions))!;
        return body.Results.Single();
    }
}
