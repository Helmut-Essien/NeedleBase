using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using NeedleBase.Application.Abstractions;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Auth;
using NeedleBase.Application.Contracts.Customers;
using NeedleBase.Application.Contracts.Orders;
using NeedleBase.Application.Contracts.Payments;
using NeedleBase.Application.Contracts.Sync;
using NeedleBase.Domain.Enums;
using NeedleBase.Infrastructure.Identity;

namespace NeedleBase.Api.Tests;

/// <summary>Slice 5: payments REST + sync (Journey 1 steps 6–7, Journey 4), rules, tenancy, roles, conflicts.</summary>
[Collection(SyncEpochCollection.Name)]
public partial class PaymentApiTests : IClassFixture<NeedleBaseApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly NeedleBaseApiFactory _factory;
    private readonly HttpClient _client;

    public PaymentApiTests(NeedleBaseApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Journey1_Deposit200Of800_LeavesBalance600()
    {
        var (token, _) = await RegisterAsync("J1");
        var orderId = await CreateOrderAsync(token, 800m);

        var (status, payment, _) = await RecordAsync(token, orderId, 200m, PaymentMethod.Cash);
        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal(200m, payment!.Amount);

        var money = await GetOrderPaymentsAsync(token, orderId);
        Assert.Equal(800m, money.TotalAmount);
        Assert.Equal(200m, money.PaidAmount);
        Assert.Equal(600m, money.Balance);
        Assert.Equal("GH₵ 200.00 of GH₵ 800.00 paid", money.PaidSummary);
        Assert.Equal("GH₵ 600.00 left", money.BalanceDisplay);
    }

    [Fact]
    public async Task Journey4_MoMo300_ThenFullBalance_IsPaidInFull()
    {
        var (token, _) = await RegisterAsync("J4");
        var orderId = await CreateOrderAsync(token, 500m);
        Assert.Equal(HttpStatusCode.Created, (await RecordAsync(token, orderId, 300m, PaymentMethod.MoMo, note: "ref 123")).Status);
        Assert.Equal(HttpStatusCode.Created, (await RecordAsync(token, orderId, 200m, PaymentMethod.Cash)).Status);

        var money = await GetOrderPaymentsAsync(token, orderId);
        Assert.True(money.IsPaidInFull);
        Assert.Equal("Paid in full", money.BalanceDisplay);
        Assert.Equal(2, money.Payments.Count);
        Assert.Contains(money.Payments, p => p.Method == PaymentMethod.MoMo && p.Note == "ref 123");
    }

    [Fact]
    public async Task EntryRules_Overpay_Decimals_Method_AreRejectedWithCodes()
    {
        var (token, _) = await RegisterAsync("Rules");
        var orderId = await CreateOrderAsync(token, 100m);

        var over = await RecordAsync(token, orderId, 100.01m, PaymentMethod.Cash);
        Assert.Equal(HttpStatusCode.Conflict, over.Status); // state conflict, like PAYMENT_ORDER_LOCKED
        Assert.Equal("PAYMENT_EXCEEDS_BALANCE", over.Code);
        using (var raw = await SendAsync(HttpMethod.Post, $"/api/orders/{orderId}/payments", token,
                   new RecordPaymentRequest { Amount = 100.01m, Method = PaymentMethod.Cash }))
        {
            Assert.Contains("Only GH₵ 100.00 is left", await raw.Content.ReadAsStringAsync()); // the useful detail survives
        }

        var cents = await RecordAsync(token, orderId, 10.005m, PaymentMethod.Cash);
        Assert.Equal(HttpStatusCode.BadRequest, cents.Status);
        Assert.Equal("PAYMENT_INVALID", cents.Code);

        var method = await RecordAsync(token, orderId, 10m, "Card");
        Assert.Equal(HttpStatusCode.BadRequest, method.Status);
        Assert.Equal("PAYMENT_INVALID", method.Code);

        var zero = await RecordAsync(token, orderId, 0m, PaymentMethod.Cash);
        Assert.Equal(HttpStatusCode.BadRequest, zero.Status);

        // Exactly the balance, with cents: fine, and nothing more after it.
        Assert.Equal(HttpStatusCode.Created, (await RecordAsync(token, orderId, 99.99m, PaymentMethod.Cash)).Status);
        Assert.Equal(HttpStatusCode.Created, (await RecordAsync(token, orderId, 0.01m, PaymentMethod.Cash)).Status);
        Assert.Equal("PAYMENT_EXCEEDS_BALANCE", (await RecordAsync(token, orderId, 0.01m, PaymentMethod.Cash)).Code);
        Assert.Equal(0m, (await GetOrderPaymentsAsync(token, orderId)).Balance);
    }

    [Fact]
    public async Task Record_SameId_IsIdempotent_DifferentContent_IsConflict()
    {
        var (token, _) = await RegisterAsync("Idem");
        var orderId = await CreateOrderAsync(token, 800m);
        var id = IdGenerator.NewId();

        Assert.Equal(HttpStatusCode.Created, (await RecordAsync(token, orderId, 200m, PaymentMethod.Cash, id)).Status);
        Assert.Equal(HttpStatusCode.Created, (await RecordAsync(token, orderId, 200m, PaymentMethod.Cash, id)).Status); // double tap
        var other = await RecordAsync(token, orderId, 250m, PaymentMethod.Cash, id);
        Assert.Equal(HttpStatusCode.Conflict, other.Status);
        Assert.Equal("PAYMENT_CONFLICT", other.Code);

        Assert.Equal(200m, (await GetOrderPaymentsAsync(token, orderId)).PaidAmount); // recorded once
    }

    [Fact]
    public async Task CancelledOrder_RefusesPayments_CollectedStillTakesBalance()
    {
        var (token, _) = await RegisterAsync("Lock");
        var cancelled = await CreateOrderAsync(token, 300m);
        await SetStatusAsync(token, cancelled, OrderStatus.Cancelled);
        var locked = await RecordAsync(token, cancelled, 50m, PaymentMethod.Cash);
        Assert.Equal(HttpStatusCode.Conflict, locked.Status);
        Assert.Equal("PAYMENT_ORDER_LOCKED", locked.Code);

        var collected = await CreateOrderAsync(token, 300m);
        await SetStatusAsync(token, collected, OrderStatus.Collected);
        Assert.Equal(HttpStatusCode.Created, (await RecordAsync(token, collected, 300m, PaymentMethod.Bank)).Status);
    }

    [Fact]
    public async Task Void_RestoresBalance_IsIdempotent_AndOrderWithPayments_CannotBeDeleted()
    {
        var (token, _) = await RegisterAsync("Void");
        var orderId = await CreateOrderAsync(token, 800m);
        var (_, payment, _) = await RecordAsync(token, orderId, 800m, PaymentMethod.MoMo);

        using (var del = Authorized(HttpMethod.Delete, $"/api/orders/{orderId}", token))
        using (var delResponse = await _client.SendAsync(del))
        {
            Assert.Equal(HttpStatusCode.Conflict, delResponse.StatusCode);
            Assert.Equal("ORDER_HAS_PAYMENTS", await CodeAsync(delResponse));
        }

        var voided = await VoidAsync(token, payment!.Id, "typed wrong amount");
        Assert.True(voided.IsVoided);
        var again = await VoidAsync(token, payment.Id, "second reason");
        Assert.Equal("typed wrong amount", again.VoidReason); // first void wins
        Assert.Equal(voided.VoidedAtUtc, again.VoidedAtUtc);

        var money = await GetOrderPaymentsAsync(token, orderId);
        Assert.Equal(0m, money.PaidAmount);
        Assert.Equal(800m, money.Balance);
        Assert.Single(money.Payments); // kept on record
    }

    [Fact]
    public async Task Tenancy_OtherBusiness_CannotSeeRecordOrVoid()
    {
        var (tokenA, _) = await RegisterAsync("TA");
        var (tokenB, _) = await RegisterAsync("TB");
        var orderA = await CreateOrderAsync(tokenA, 500m);
        var (_, paymentA, _) = await RecordAsync(tokenA, orderA, 100m, PaymentMethod.Cash);

        await AssertStatus(HttpStatusCode.NotFound, HttpMethod.Get, $"/api/orders/{orderA}/payments", tokenB);
        await AssertStatus(HttpStatusCode.NotFound, HttpMethod.Get, $"/api/payments/{paymentA!.Id}", tokenB);
        await AssertStatus(HttpStatusCode.NotFound, HttpMethod.Post, $"/api/payments/{paymentA.Id}/void", tokenB, new VoidPaymentRequest());
        Assert.Equal(HttpStatusCode.NotFound, (await RecordAsync(tokenB, orderA, 10m, PaymentMethod.Cash)).Status);

        using var list = Authorized(HttpMethod.Get, "/api/payments", tokenB);
        using var listResponse = await _client.SendAsync(list);
        var page = await listResponse.Content.ReadFromJsonAsync<PaginatedResponse<PaymentListItemDto>>(JsonOptions);
        Assert.DoesNotContain(page!.Data, p => p.Id == paymentA.Id);

        // A's payment is untouched.
        Assert.Equal(100m, (await GetOrderPaymentsAsync(tokenA, orderA)).PaidAmount);

        // Sync: B pushing A's payment id is refused generically; B's pull never contains it.
        var orderB = await CreateOrderAsync(tokenB, 500m);
        var push = await PushAsync(tokenB, PaymentOp(Payment(paymentA.Id, orderB, "x", 100m)));
        Assert.Equal(SyncOpStatus.Failed, push.Status);
        Assert.Equal(SyncErrorCodes.Rejected, push.Code);
        Assert.DoesNotContain(await PullPaymentsAsync(tokenB), p => p.Id == paymentA.Id);
    }

    [Fact]
    public async Task Tailor_GetsPaymentForbidden()
    {
        var (ownerToken, businessId) = await RegisterAsync("Role");
        var orderId = await CreateOrderAsync(ownerToken, 500m);
        var tailorToken = await TokenWithRoleAsync(ownerToken, businessId, BusinessRole.Tailor);

        var record = await RecordAsync(tailorToken, orderId, 10m, PaymentMethod.Cash);
        Assert.Equal(HttpStatusCode.Forbidden, record.Status);
        Assert.Equal("PAYMENT_FORBIDDEN", record.Code);
        await AssertStatus(HttpStatusCode.Forbidden, HttpMethod.Get, $"/api/orders/{orderId}/payments", tailorToken);
        await AssertStatus(HttpStatusCode.Forbidden, HttpMethod.Get, "/api/payments", tailorToken);

        var managerToken = await TokenWithRoleAsync(ownerToken, businessId, BusinessRole.Manager);
        Assert.Equal(HttpStatusCode.Created, (await RecordAsync(managerToken, orderId, 10m, PaymentMethod.Cash)).Status);
    }

    [Fact]
    public async Task ConcurrentRecords_NeverOverpay_AndSameIdRace_StoresOnce()
    {
        var (token, _) = await RegisterAsync("Race");
        var orderId = await CreateOrderAsync(token, 300m);

        // Eight taps/devices at once, each for GH₵ 100 on a GH₵ 300 order: exactly three fit.
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => RecordAsync(token, orderId, 100m, PaymentMethod.Cash)));
        Assert.Equal(3, results.Count(r => r.Status == HttpStatusCode.Created));
        Assert.All(results.Where(r => r.Status != HttpStatusCode.Created), r => Assert.Equal("PAYMENT_EXCEEDS_BALANCE", r.Code));
        Assert.Equal(0m, (await GetOrderPaymentsAsync(token, orderId)).Balance);

        // The same payment id sent five times at once (double tap + retries): one payment.
        var other = await CreateOrderAsync(token, 300m);
        var id = IdGenerator.NewId();
        var same = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => RecordAsync(token, other, 50m, PaymentMethod.MoMo, id)));
        Assert.All(same, r => Assert.Equal(id, r.Payment?.Id));
        var money = await GetOrderPaymentsAsync(token, other);
        Assert.Single(money.Payments);
        Assert.Equal(250m, money.Balance);
    }

    [Fact]
    public async Task DeleteRacingRecord_NeverFailsWith500_AndNeverOrphansMoney()
    {
        var (token, _) = await RegisterAsync("DelRace");
        for (var i = 0; i < 5; i++)
        {
            var orderId = await CreateOrderAsync(token, 100m);
            var record = RecordAsync(token, orderId, 50m, PaymentMethod.Cash);
            var delete = SendAsync(HttpMethod.Delete, $"/api/orders/{orderId}", token);
            await Task.WhenAll(record, delete);
            using var deleted = await delete;
            var recorded = await record;
            Assert.NotEqual(HttpStatusCode.InternalServerError, deleted.StatusCode);
            Assert.NotEqual(HttpStatusCode.InternalServerError, recorded.Status);
            // Exactly one wins: a payment and a kept order, or a deleted order and no payment.
            Assert.True(recorded.Status == HttpStatusCode.Created ^ deleted.IsSuccessStatusCode,
                $"record {recorded.Status}, delete {deleted.StatusCode}");
        }
    }

    [Fact]
    public async Task FutureDate_IsRefusedOnRest_AndDatedNowOnSync()
    {
        var (token, _) = await RegisterAsync("Dates");
        var orderId = await CreateOrderAsync(token, 500m);
        using (var future = await SendAsync(HttpMethod.Post, $"/api/orders/{orderId}/payments", token,
                   new RecordPaymentRequest { Amount = 10m, Method = PaymentMethod.Cash, RecordedAtUtc = DateTimeOffset.UtcNow.AddDays(3) }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, future.StatusCode);
            Assert.Equal("PAYMENT_INVALID", await CodeAsync(future));
        }

        var detail = await GetOrderAsync(token, orderId);
        var skewed = Payment(IdGenerator.NewId(), orderId, detail.CustomerId, 20m) with { RecordedAtUtc = DateTimeOffset.UtcNow.AddYears(1) };
        var applied = await PushAsync(token, PaymentOp(skewed));
        Assert.Equal(SyncOpStatus.Applied, applied.Status);
        Assert.True(applied.Payment!.RecordedAtUtc <= DateTimeOffset.UtcNow.AddMinutes(1));
        Assert.Equal(SyncOpStatus.Applied, (await PushAsync(token, PaymentOp(skewed))).Status); // replay still matches
    }

    [Fact]
    public async Task SyncPush_UnknownMethodFromNewerApp_IsKeptAsOther_AndReplayMatches()
    {
        var (token, _) = await RegisterAsync("NewMethod");
        var orderId = await CreateOrderAsync(token, 500m);
        var detail = await GetOrderAsync(token, orderId);
        var card = Payment(IdGenerator.NewId(), orderId, detail.CustomerId, 30m) with { Method = "Card", Note = "terminal 4" };
        var applied = await PushAsync(token, PaymentOp(card));
        Assert.Equal(SyncOpStatus.Applied, applied.Status);
        Assert.Equal(PaymentMethod.Other, applied.Payment!.Method);
        Assert.Equal("terminal 4 [Card]", applied.Payment.Note);
        Assert.Equal(SyncOpStatus.Applied, (await PushAsync(token, PaymentOp(card))).Status);
    }

    [Fact]
    public async Task ConcurrentSyncPush_SameIdDifferentAmount_OneApplied_OtherIsMismatchNotRejected()
    {
        var (token, _) = await RegisterAsync("PushRace");
        var orderId = await CreateOrderAsync(token, 500m);
        var detail = await GetOrderAsync(token, orderId);
        var id = IdGenerator.NewId();
        var results = await Task.WhenAll(
            PushAsync(token, PaymentOp(Payment(id, orderId, detail.CustomerId, 10m))),
            PushAsync(token, PaymentOp(Payment(id, orderId, detail.CustomerId, 20m))));
        Assert.Single(results, r => r.Status == SyncOpStatus.Applied);
        var other = Assert.Single(results, r => r.Status != SyncOpStatus.Applied);
        Assert.Equal(SyncOpStatus.Conflict, other.Status);
        Assert.Equal(SyncErrorCodes.PaymentMismatch, other.Code);
    }

    [Fact]
    public async Task ConcurrentVoids_FirstReasonWins_AndStaysOne()
    {
        var (token, _) = await RegisterAsync("VoidRace");
        var orderId = await CreateOrderAsync(token, 500m);
        var paymentId = (await RecordAsync(token, orderId, 100m, PaymentMethod.Cash)).Payment!.Id;
        var voids = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => VoidAsync(token, paymentId, $"reason {i}")));
        Assert.Single(voids.Select(v => v.VoidReason).Distinct());
        Assert.Single(voids.Select(v => v.VoidedAtUtc).Distinct());
        Assert.Equal(500m, (await GetOrderPaymentsAsync(token, orderId)).Balance);
    }

    [Fact]
    public async Task CancelledOrder_WithPayments_ShowsRecordedNotLeft()
    {
        var (token, _) = await RegisterAsync("CancelCopy");
        var orderId = await CreateOrderAsync(token, 800m);
        Assert.Equal(HttpStatusCode.Created, (await RecordAsync(token, orderId, 200m, PaymentMethod.Cash)).Status);
        await SetStatusAsync(token, orderId, OrderStatus.Cancelled);
        var money = await GetOrderPaymentsAsync(token, orderId);
        Assert.Equal("Order cancelled · GH₵ 200.00 recorded", money.BalanceDisplay);
        Assert.Equal(600m, money.Balance); // the number stays honest; refunds are out of scope
    }

    [Fact]
    public async Task SyncPull_Tailor_GetsNoPayments_AndAPromotedCursorPullsThemAll()
    {
        var (ownerToken, businessId) = await RegisterAsync("PullRole");
        var orderId = await CreateOrderAsync(ownerToken, 500m);
        Assert.Equal(HttpStatusCode.Created, (await RecordAsync(ownerToken, orderId, 120m, PaymentMethod.Cash)).Status);
        var tailorToken = await TokenWithRoleAsync(ownerToken, businessId, BusinessRole.Tailor);

        string? cursor = null;
        SyncPullResponse page;
        var tailorSaw = 0;
        do
        {
            page = await PullAsync(tailorToken, cursor, 200);
            tailorSaw += page.Payments?.Count ?? 0;
            Assert.Contains(page.Orders, o => o.Id == orderId);
            cursor = page.Cursor;
        }
        while (page.HasMore);
        Assert.Equal(0, tailorSaw);

        // Same phone, now a manager: continuing from the tailor's cursor still delivers the older payment.
        var managerToken = await TokenWithRoleAsync(ownerToken, businessId, BusinessRole.Manager);
        var next = await PullAsync(managerToken, cursor, 200);
        Assert.Single(next.Payments ?? [], p => p.OrderId == orderId);
    }

    // ------------------------------------------------------------------ sync

    [Fact]
    public async Task SyncPush_Create_Replay_Pull_AndOfflineOverpayIsKept()
    {
        var (token, _) = await RegisterAsync("SyncPay");
        var orderId = await CreateOrderAsync(token, 300m);
        var customerId = (await GetOrderPaymentsAsync(token, orderId)).OrderId; // order exists
        var detail = await GetOrderAsync(token, orderId);

        var first = Payment(IdGenerator.NewId(), orderId, detail.CustomerId, 300m);
        var applied = await PushAsync(token, PaymentOp(first));
        Assert.Equal(SyncOpStatus.Applied, applied.Status);
        Assert.Equal(300m, applied.Payment!.Amount);
        Assert.Equal(SyncOpStatus.Applied, (await PushAsync(token, PaymentOp(first))).Status); // lost ack replay

        // A second phone recorded the same balance offline: the money was really taken, so it is stored (overpaid).
        var second = Payment(IdGenerator.NewId(), orderId, detail.CustomerId, 300m);
        Assert.Equal(SyncOpStatus.Applied, (await PushAsync(token, PaymentOp(second))).Status);
        var money = await GetOrderPaymentsAsync(token, orderId);
        Assert.Equal(-300m, money.Balance);
        Assert.True(money.IsOverpaid);
        Assert.Equal("GH₵ 300.00 overpaid", money.BalanceDisplay);

        var pulled = await PullPaymentsAsync(token);
        Assert.Single(pulled, p => p.Id == first.Id);
        Assert.Single(pulled, p => p.Id == second.Id);
        Assert.NotNull(customerId);
    }

    [Fact]
    public async Task SyncPush_SameIdDifferentContent_IsConflict_WithServerCopy_NeverOverwritten()
    {
        var (token, _) = await RegisterAsync("SyncMis");
        var orderId = await CreateOrderAsync(token, 800m);
        var detail = await GetOrderAsync(token, orderId);
        var payment = Payment(IdGenerator.NewId(), orderId, detail.CustomerId, 200m);
        Assert.Equal(SyncOpStatus.Applied, (await PushAsync(token, PaymentOp(payment))).Status);

        var conflict = await PushAsync(token, PaymentOp(payment with { Amount = 250m }));
        Assert.Equal(SyncOpStatus.Conflict, conflict.Status);
        Assert.Equal(SyncErrorCodes.PaymentMismatch, conflict.Code);
        Assert.Equal(200m, conflict.Payment!.Amount);
        Assert.Equal(200m, (await GetOrderPaymentsAsync(token, orderId)).PaidAmount);
    }

    [Fact]
    public async Task SyncPush_Void_IsMonotonic_AndDeleteIsRefused()
    {
        var (token, _) = await RegisterAsync("SyncVoid");
        var orderId = await CreateOrderAsync(token, 800m);
        var detail = await GetOrderAsync(token, orderId);
        var payment = Payment(IdGenerator.NewId(), orderId, detail.CustomerId, 200m);
        await PushAsync(token, PaymentOp(payment));

        var now = DateTimeOffset.UtcNow;
        var voided = await PushAsync(token, PaymentOp(payment with { IsVoided = true, VoidedAtUtc = now, VoidReason = "mistake" }));
        Assert.Equal(SyncOpStatus.Applied, voided.Status);
        Assert.True(voided.Payment!.IsVoided);

        // An older device copy (not voided) can't un-void: applied, server copy returned.
        var stale = await PushAsync(token, PaymentOp(payment));
        Assert.Equal(SyncOpStatus.Applied, stale.Status);
        Assert.True(stale.Payment!.IsVoided);

        var delete = await PushAsync(token, new SyncPushOperation
        {
            OpId = IdGenerator.NewId(),
            EntityType = SyncEntityType.Payment,
            EntityId = payment.Id,
            Operation = SyncOperation.Delete
        });
        Assert.Equal(SyncOpStatus.Failed, delete.Status);
        Assert.Contains(await PullPaymentsAsync(token), p => p.Id == payment.Id && p.IsVoided);
    }

    [Fact]
    public async Task SyncPush_PaymentForMissingOrder_IsParentDeleted_AndOrderDeleteWithPayments_IsRefused()
    {
        var (token, _) = await RegisterAsync("SyncParent");
        var orphan = await PushAsync(token, PaymentOp(Payment(IdGenerator.NewId(), IdGenerator.NewId(), IdGenerator.NewId(), 10m)));
        Assert.Equal(SyncOpStatus.Conflict, orphan.Status);
        Assert.Equal(SyncErrorCodes.ParentDeleted, orphan.Code);

        var orderId = await CreateOrderAsync(token, 800m);
        var detail = await GetOrderAsync(token, orderId);
        await PushAsync(token, PaymentOp(Payment(IdGenerator.NewId(), orderId, detail.CustomerId, 100m)));
        var delete = await PushAsync(token, new SyncPushOperation
        {
            OpId = IdGenerator.NewId(),
            EntityType = SyncEntityType.Order,
            EntityId = orderId,
            Operation = SyncOperation.Delete,
            BaseModifiedUtc = detail.DateModifiedUtc
        });
        Assert.Equal(SyncOpStatus.Conflict, delete.Status);
        Assert.Equal(SyncErrorCodes.DeleteRefused, delete.Code);
        Assert.Equal(orderId, delete.Order!.Id);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, $"/api/orders/{orderId}", token)).StatusCode);
    }

    [Fact]
    public async Task SyncPull_PaymentsPhase_PagesWithoutGaps_AndContinuesFromStoredCursor()
    {
        var (token, _) = await RegisterAsync("SyncPage");
        var orderId = await CreateOrderAsync(token, 1000m);
        var detail = await GetOrderAsync(token, orderId);
        var ids = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            var p = Payment(IdGenerator.NewId(), orderId, detail.CustomerId, 10m);
            ids.Add(p.Id);
            await PushAsync(token, PaymentOp(p));
        }

        // Tiny pages: payments span several pages after customers/orders; nothing is skipped or duplicated.
        var seen = new List<string>();
        string? cursor = null;
        SyncPullResponse page;
        do
        {
            page = await PullAsync(token, cursor, 2);
            seen.AddRange((page.Payments ?? []).Select(p => p.Id));
            cursor = page.Cursor;
        }
        while (page.HasMore);

        Assert.Equal(ids.OrderBy(x => x), seen.OrderBy(x => x));

        // Incremental: a new payment after a completed sync arrives alone on the stored cursor.
        var later = Payment(IdGenerator.NewId(), orderId, detail.CustomerId, 10m);
        await PushAsync(token, PaymentOp(later));
        var next = await PullAsync(token, cursor, 200);
        Assert.Equal([later.Id], (next.Payments ?? []).Select(p => p.Id).ToArray());
    }

    // ------------------------------------------------------------------ helpers

    private static SyncPaymentDto Payment(string id, string orderId, string customerId, decimal amount)
    {
        var now = DateTimeOffset.UtcNow;
        return new SyncPaymentDto(id, orderId, customerId, amount, PaymentMethod.Cash, null, now, false, null, null, now, now);
    }

    private static SyncPushOperation PaymentOp(SyncPaymentDto payment) => new()
    {
        OpId = IdGenerator.NewId(),
        EntityType = SyncEntityType.Payment,
        EntityId = payment.Id,
        Payment = payment
    };

    private async Task<SyncPushResult> PushAsync(string token, SyncPushOperation op)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = Authorized(HttpMethod.Post, "/api/sync/push", token, new SyncPushRequest { Operations = [op] });
            using var response = await _client.SendAsync(request);
            if (response.StatusCode == HttpStatusCode.ServiceUnavailable && attempt < 5 && await CodeAsync(response) == SyncErrorCodes.Maintenance)
            {
                // Another test bumped the sync epoch (SYNC_MAINTENANCE): the real client retries too.
                await NeedleBase.Infrastructure.Persistence.ApplicationDbContext.WaitForEpochBumpAsync().WaitAsync(TimeSpan.FromSeconds(60));
                await Task.Delay(50);
                continue;
            }

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<SyncPushResponse>(JsonOptions);
            return Assert.Single(body!.Results);
        }
    }

    private async Task<SyncPullResponse> PullAsync(string token, string? cursor, int limit)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = Authorized(HttpMethod.Post, "/api/sync/pull", token, new SyncPullRequest { Cursor = cursor, Limit = limit });
            using var response = await _client.SendAsync(request);
            if (response.StatusCode == HttpStatusCode.ServiceUnavailable && attempt < 5 && await CodeAsync(response) == SyncErrorCodes.Maintenance)
            {
                await NeedleBase.Infrastructure.Persistence.ApplicationDbContext.WaitForEpochBumpAsync().WaitAsync(TimeSpan.FromSeconds(60));
                await Task.Delay(50);
                continue;
            }

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<SyncPullResponse>(JsonOptions))!;
        }
    }

    private async Task<List<SyncPaymentDto>> PullPaymentsAsync(string token)
    {
        var all = new List<SyncPaymentDto>();
        string? cursor = null;
        SyncPullResponse page;
        do
        {
            page = await PullAsync(token, cursor, 200);
            all.AddRange(page.Payments ?? []);
            cursor = page.Cursor;
        }
        while (page.HasMore);

        return all;
    }

    private async Task<(HttpStatusCode Status, PaymentDto? Payment, string? Code)> RecordAsync(
        string token, string orderId, decimal amount, string method, string? id = null, string? note = null)
    {
        using var request = Authorized(HttpMethod.Post, $"/api/orders/{orderId}/payments", token,
            new RecordPaymentRequest { Id = id, Amount = amount, Method = method, Note = note });
        using var response = await _client.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.Created)
        {
            return (response.StatusCode, await response.Content.ReadFromJsonAsync<PaymentDto>(JsonOptions), null);
        }

        return (response.StatusCode, null, await CodeAsync(response));
    }

    private async Task<PaymentDto> VoidAsync(string token, string paymentId, string reason)
    {
        using var request = Authorized(HttpMethod.Post, $"/api/payments/{paymentId}/void", token, new VoidPaymentRequest { Reason = reason });
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PaymentDto>(JsonOptions))!;
    }

    private async Task<OrderPaymentsDto> GetOrderPaymentsAsync(string token, string orderId)
    {
        using var response = await SendAsync(HttpMethod.Get, $"/api/orders/{orderId}/payments", token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<OrderPaymentsDto>(JsonOptions))!;
    }

    private async Task<OrderDetailDto> GetOrderAsync(string token, string orderId)
    {
        using var response = await SendAsync(HttpMethod.Get, $"/api/orders/{orderId}", token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<OrderDetailDto>(JsonOptions))!;
    }

    private async Task SetStatusAsync(string token, string orderId, string status)
    {
        using var response = await SendAsync(HttpMethod.Patch, $"/api/orders/{orderId}/status", token, new UpdateOrderStatusRequest { Status = status });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task AssertStatus(HttpStatusCode expected, HttpMethod method, string url, string token, object? body = null)
    {
        using var response = await SendAsync(method, url, token, body);
        Assert.True(response.StatusCode == expected, $"{method} {url} returned {(int)response.StatusCode}, expected {(int)expected}.");
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null)
    {
        using var request = Authorized(method, url, token, body);
        return await _client.SendAsync(request);
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private async Task<string> CreateOrderAsync(string token, decimal total)
    {
        using var customerRequest = Authorized(HttpMethod.Post, "/api/customers", token,
            new CreateCustomerRequest("Efua Mensah", "+233245550123", null, null));
        using var customerResponse = await _client.SendAsync(customerRequest);
        Assert.Equal(HttpStatusCode.Created, customerResponse.StatusCode);
        var customer = (await customerResponse.Content.ReadFromJsonAsync<CustomerDetailDto>(JsonOptions))!;

        using var request = Authorized(HttpMethod.Post, "/api/orders", token, new CreateOrderRequest
        {
            CustomerId = customer.Id,
            TotalAmount = total,
            Lines =
            [
                new CreateOrderLineRequest
                {
                    GarmentTemplate = GarmentTemplate.Dress,
                    Quantity = 1,
                    UnitPrice = total,
                    Unit = MeasurementUnit.Centimetres,
                    MeasurementFields = new Dictionary<string, decimal?>()
                }
            ]
        });
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<OrderDetailDto>(JsonOptions))!.Id;
    }

    /// <summary>A token for the same (active) member with another role claim, as a staff invite would issue.</summary>
    private async Task<string> TokenWithRoleAsync(string ownerToken, string businessId, string role)
    {
        using var me = await SendAsync(HttpMethod.Get, "/api/me", ownerToken);
        using var doc = JsonDocument.Parse(await me.Content.ReadAsStringAsync());
        var userId = doc.RootElement.GetProperty("userId").GetString()!;
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await users.FindByIdAsync(userId))!;
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        return tokens.CreateAccessToken(user.Id, user.Email!, user.UserName!, user.SecurityStamp ?? string.Empty, true, businessId, role);
    }

    private async Task<(string AccessToken, string BusinessId)> RegisterAsync(string label)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var register = new RegisterBusinessRequest(
            $"pay{label}{suffix}@ama-stitches.test", "Passw0rd!", "Ama", "Owusu", $"Pay {label} {suffix}", "+233200000000");
        using var response = await _client.PostAsJsonAsync("/api/auth/register-business", register, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<AuthPayload>(JsonOptions);
        return (payload!.AccessToken!, payload.User!.BusinessId!);
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

    private sealed record AuthPayload(string? AccessToken, string? RefreshToken, AuthUserDto? User);
}
