using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Payments;
using NeedleBase.Application.Payments;
using NeedleBase.Domain.Entities;
using NeedleBase.Domain.Enums;
using NeedleBase.Domain.Exceptions;
using NeedleBase.Mobile.Data.Data;

namespace NeedleBase.Mobile.Data.Services;

public sealed class PaymentLocalService : IPaymentLocalService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public PaymentLocalService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<LocalOrderPayments?> GetForOrderAsync(string businessId, string orderId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(businessId);
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        return await LoadOrderPaymentsAsync(db, businessId, orderId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<LocalPaymentListItem>> ListAsync(string businessId, int take = 100, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(businessId);
        take = Math.Clamp(take, 1, 200);
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        var rows = await (
            from p in db.Payments.AsNoTracking().Where(p => p.BusinessId == businessId)
            join o in db.Orders.AsNoTracking() on p.OrderId equals o.Id
            join c in db.Customers.AsNoTracking() on p.CustomerId equals c.Id
            orderby p.Id descending // ULID: time-sortable, indexed
            select new
            {
                p.Id, p.OrderId, o.DisplayRef, p.CustomerId, CustomerName = c.Name, p.Amount, p.Method, p.RecordedAtUtc,
                p.IsVoided, p.DateModifiedUtc
            })
            .Take(take)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var pending = await PendingIdsAsync(db, businessId, rows.Select(r => r.Id).ToList(), cancellationToken).ConfigureAwait(false);
        return rows.Select(r => new LocalPaymentListItem(
            new PaymentListItemDto(r.Id, r.OrderId, r.DisplayRef, r.CustomerId, r.CustomerName, r.Amount, r.Method,
                r.RecordedAtUtc, r.IsVoided, r.DateModifiedUtc),
            pending.Contains(r.Id))).ToList();
    }

    public async Task<LocalPaymentReceipt?> GetReceiptAsync(string businessId, string paymentId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(businessId);
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        var payment = await db.Payments.AsNoTracking()
            .FirstOrDefaultAsync(p => p.BusinessId == businessId && p.Id == paymentId, cancellationToken).ConfigureAwait(false);
        if (payment is null)
        {
            return null;
        }

        var header = await (
            from o in db.Orders.AsNoTracking().Where(o => o.BusinessId == businessId && o.Id == payment.OrderId)
            join c in db.Customers.AsNoTracking() on o.CustomerId equals c.Id
            select new { o.DisplayRef, c.Name, c.Phone })
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var totals = await LoadOrderPaymentsAsync(db, businessId, payment.OrderId, cancellationToken).ConfigureAwait(false);
        if (header is null || totals is null)
        {
            return null;
        }

        var local = totals.Payments.FirstOrDefault(p => p.Id == paymentId)
            ?? new LocalPayment(PaymentService.ToDto(payment), false);
        return new LocalPaymentReceipt(local, payment.OrderId, header.DisplayRef, header.Name, header.Phone, totals.Summary);
    }

    public async Task<RecordResult<PaymentDto>> RecordAsync(
        string businessId, string orderId, RecordPaymentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(businessId);
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        var order = await db.Orders.AsNoTracking()
            .Where(o => o.BusinessId == businessId && o.Id == orderId)
            .Select(o => new { o.Id, o.CustomerId, o.Status, o.TotalAmount })
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (order is null)
        {
            return RecordResult.Fail(RecordError.OrderNotFound);
        }

        var id = string.IsNullOrWhiteSpace(request.Id) ? IdGenerator.NewId() : request.Id.Trim();
        var existing = await db.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            // Double tap / retry with the same id: same answer, no second payment.
            return existing.BusinessId == businessId && existing.OrderId == orderId && existing.Amount == request.Amount
                ? PaymentService.ToDto(existing)
                : RecordResult.Fail(RecordError.PaymentConflict);
        }

        var paid = await PaidAsync(db, businessId, orderId, cancellationToken).ConfigureAwait(false);
        var (error, detail) = PaymentRules.CheckEntry(order.Status, order.TotalAmount, paid, request.Amount, request.Method, lenient: false);
        if (error != RecordError.None)
        {
            return RecordResult.Fail(error, detail);
        }

        try
        {
            var now = DateTimeOffset.UtcNow;
            var entity = Payment.Create(id, businessId, order.Id, order.CustomerId, request.Amount, request.Method!.Trim(), request.Note,
                request.RecordedAtUtc ?? now, now);
            db.Payments.Add(entity);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false); // queues the sync item in the same save
            return PaymentService.ToDto(entity);
        }
        catch (Exception ex) when (LocalStorageErrors.IsStorageFull(ex))
        {
            return RecordResult.Fail(RecordError.StorageFull, LocalStorageErrors.StorageFullMessage);
        }
        catch (DomainRuleException ex)
        {
            return RecordResult.Fail(RecordError.PaymentInvalid, ex.Message);
        }
        catch (ArgumentException)
        {
            return RecordResult.Fail(RecordError.PaymentInvalid, $"Note can be at most {NeedleBase.Domain.Common.FieldLimits.Notes} characters.");
        }
    }

    public async Task<RecordResult<PaymentDto>> VoidAsync(string businessId, string paymentId, string? reason, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(businessId);
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        var entity = await db.Payments.FirstOrDefaultAsync(p => p.BusinessId == businessId && p.Id == paymentId, cancellationToken)
            .ConfigureAwait(false);
        if (entity is null)
        {
            return RecordResult.Fail(RecordError.PaymentNotFound);
        }

        if (entity.IsVoided)
        {
            return PaymentService.ToDto(entity);
        }

        try
        {
            entity.Void(reason, DateTimeOffset.UtcNow);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return PaymentService.ToDto(entity);
        }
        catch (Exception ex) when (LocalStorageErrors.IsStorageFull(ex))
        {
            return RecordResult.Fail(RecordError.StorageFull, LocalStorageErrors.StorageFullMessage);
        }
        catch (ArgumentException)
        {
            return RecordResult.Fail(RecordError.PaymentInvalid, $"Reason can be at most {NeedleBase.Domain.Common.FieldLimits.Notes} characters.");
        }
    }

    private static async Task<LocalOrderPayments?> LoadOrderPaymentsAsync(LocalDbContext db, string businessId, string orderId, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking()
            .Where(o => o.BusinessId == businessId && o.Id == orderId)
            .Select(o => new { o.Id, o.Status, o.TotalAmount })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (order is null)
        {
            return null;
        }

        var payments = (await db.Payments.AsNoTracking()
            .Where(p => p.BusinessId == businessId && p.OrderId == orderId)
            .ToListAsync(ct).ConfigureAwait(false))
            .OrderByDescending(p => p.RecordedAtUtc).ThenByDescending(p => p.Id)
            .Select(PaymentService.ToDto).ToList();
        var pending = await PendingIdsAsync(db, businessId, payments.Select(p => p.Id).ToList(), ct).ConfigureAwait(false);
        var summary = new OrderPaymentsDto(order.Id, order.Status, order.TotalAmount,
            PaymentRules.Paid(payments.Select(p => (p.Amount, p.IsVoided))), payments);
        return new LocalOrderPayments(summary, payments.Select(p => new LocalPayment(p, pending.Contains(p.Id))).ToList());
    }

    private static async Task<decimal> PaidAsync(LocalDbContext db, string businessId, string orderId, CancellationToken ct)
    {
        // SQLite stores decimals as TEXT: sum in memory (exact), not in SQL (REAL).
        var amounts = await db.Payments.AsNoTracking()
            .Where(p => p.BusinessId == businessId && p.OrderId == orderId && !p.IsVoided)
            .Select(p => p.Amount)
            .ToListAsync(ct).ConfigureAwait(false);
        return amounts.Sum();
    }

    private static async Task<HashSet<string>> PendingIdsAsync(LocalDbContext db, string businessId, List<string> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        return (await db.SyncQueue.AsNoTracking()
            .Where(q => q.BusinessId == businessId && q.EntityType == SyncEntityType.Payment && ids.Contains(q.EntityId))
            .Select(q => q.EntityId)
            .ToListAsync(ct).ConfigureAwait(false)).ToHashSet(StringComparer.Ordinal);
    }
}
