using Microsoft.EntityFrameworkCore;
using NeedleBase.Application.Abstractions;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Payments;
using NeedleBase.Application.Email;
using NeedleBase.Domain.Entities;
using NeedleBase.Domain.Enums;
using NeedleBase.Infrastructure.Persistence;

namespace NeedleBase.Infrastructure.Email;

/// <summary>
/// Queues transactional customer emails in the same SaveChanges as the order/payment change (REST, web, sync).
/// Dedupe via <see cref="CustomerEmailSent"/>. Flush to <see cref="IEmailSender"/> after a successful commit.
/// </summary>
internal static class CustomerEmailOutbox
{
    private static readonly string[] DefaultStatuses = [OrderStatus.Ready, OrderStatus.Collected];

    public static async Task CollectAsync(ApplicationDbContext db, CancellationToken cancellationToken)
    {
        db.PendingCustomerEmails?.Clear();
        List<EmailMessage>? pending = null;

        var statusChanges = new List<(Order Order, string Status)>();
        foreach (var entry in db.ChangeTracker.Entries<Order>())
        {
            var status = entry.Entity.Status;
            var isChange = entry.State switch
            {
                EntityState.Added => status != OrderStatus.New,
                EntityState.Modified => entry.Property(o => o.Status).IsModified &&
                                        !string.Equals(entry.Property(o => o.Status).OriginalValue, status, StringComparison.Ordinal),
                _ => false
            };
            if (isChange)
            {
                statusChanges.Add((entry.Entity, status));
            }
        }

        var newPayments = db.ChangeTracker.Entries<Payment>()
            .Where(e => e.State == EntityState.Added && !e.Entity.IsVoided)
            .Select(e => e.Entity)
            .ToList();

        if (statusChanges.Count == 0 && newPayments.Count == 0)
        {
            return;
        }

        var businessIds = statusChanges.Select(c => c.Order.BusinessId)
            .Concat(newPayments.Select(p => p.BusinessId))
            .Distinct()
            .ToList();
        var customerIds = statusChanges.Select(c => c.Order.CustomerId)
            .Concat(newPayments.Select(p => p.CustomerId))
            .Distinct()
            .ToList();

        var consents = await db.CustomerEmailConsents.IgnoreQueryFilters().AsNoTracking()
            .Where(c => customerIds.Contains(c.CustomerId) && c.OptedIn)
            .Select(c => c.CustomerId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (consents.Count == 0)
        {
            return;
        }

        var customers = await db.Customers.IgnoreQueryFilters().AsNoTracking()
            .Where(c => customerIds.Contains(c.Id) && c.Email != null && c.Email != "")
            .Select(c => new { c.Id, c.Name, c.Email, c.BusinessId })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var byCustomer = customers.Where(c => consents.Contains(c.Id)).ToDictionary(c => c.Id);
        if (byCustomer.Count == 0)
        {
            return;
        }

        var businesses = await db.Businesses.IgnoreQueryFilters().AsNoTracking()
            .Where(b => businessIds.Contains(b.Id))
            .Select(b => new { b.Id, b.Name, b.Phone })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var byBiz = businesses.ToDictionary(b => b.Id);

        var waSettings = await db.WhatsAppSettings.IgnoreQueryFilters().AsNoTracking()
            .Where(s => businessIds.Contains(s.BusinessId))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        var appBase = ResolveAppBase(db);

        if (statusChanges.Count > 0)
        {
            var orderIds = statusChanges.Select(c => c.Order.Id).Distinct().ToList();
            var already = await db.CustomerEmailSents.IgnoreQueryFilters().AsNoTracking()
                .Where(s => s.Kind == CustomerEmailSent.KindStatus && orderIds.Contains(s.RefId))
                .Select(s => new { s.RefId, s.StatusKey })
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            foreach (var (order, status) in statusChanges.DistinctBy(c => (c.Order.Id, c.Status)))
            {
                if (!byCustomer.TryGetValue(order.CustomerId, out var cust) || cust.Email is null)
                {
                    continue;
                }

                if (!ShouldEmailStatus(waSettings.FirstOrDefault(s => s.BusinessId == order.BusinessId), status))
                {
                    continue;
                }

                if (already.Any(a => a.RefId == order.Id && a.StatusKey == status))
                {
                    continue;
                }

                db.CustomerEmailSents.Add(CustomerEmailSent.ForStatus(IdGenerator.NewId(), order.BusinessId, order.Id, status, now));
                byBiz.TryGetValue(order.BusinessId, out var biz);
                var msg = EmailTemplates.CustomerOrderStatus(
                    cust.Email, cust.Name, biz?.Name ?? "your shop", biz?.Phone, order.DisplayRef,
                    OrderStatus.DisplayName(status), appBase);
                (pending ??= []).Add(msg);
            }
        }

        if (newPayments.Count > 0)
        {
            var paymentIds = newPayments.Select(p => p.Id).ToList();
            var alreadyPay = await db.CustomerEmailSents.IgnoreQueryFilters().AsNoTracking()
                .Where(s => s.Kind == CustomerEmailSent.KindReceipt && paymentIds.Contains(s.RefId))
                .Select(s => s.RefId)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            var orderIds = newPayments.Select(p => p.OrderId).Distinct().ToList();
            var orders = await db.Orders.IgnoreQueryFilters().AsNoTracking()
                .Where(o => orderIds.Contains(o.Id))
                .Select(o => new { o.Id, o.DisplayRef, o.TotalAmount })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            var orderMap = orders.ToDictionary(o => o.Id);

            // DB sum excludes Added rows still in the tracker; include every new payment on the order so
            // multi-payment SaveChanges (sync batch) shows the correct paid-so-far on each receipt.
            var paidInDb = await db.Payments.IgnoreQueryFilters().AsNoTracking()
                .Where(p => orderIds.Contains(p.OrderId) && !p.IsVoided)
                .GroupBy(p => p.OrderId)
                .Select(g => new { OrderId = g.Key, Sum = g.Sum(p => p.Amount) })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            var paidDbMap = paidInDb.ToDictionary(x => x.OrderId, x => x.Sum);
            var paidNewMap = newPayments
                .GroupBy(p => p.OrderId)
                .ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));

            foreach (var pay in newPayments)
            {
                if (alreadyPay.Contains(pay.Id))
                {
                    continue;
                }

                if (!byCustomer.TryGetValue(pay.CustomerId, out var cust) || cust.Email is null)
                {
                    continue;
                }

                if (!orderMap.TryGetValue(pay.OrderId, out var ord))
                {
                    continue;
                }

                var paid = paidDbMap.GetValueOrDefault(pay.OrderId) + paidNewMap.GetValueOrDefault(pay.OrderId);
                var balance = ord.TotalAmount - paid;
                var balanceLine = balance > 0
                    ? $"Balance: {Money.Format(balance)}"
                    : balance < 0 ? $"Overpaid: {Money.Format(-balance)}" : "Paid in full";

                db.CustomerEmailSents.Add(CustomerEmailSent.ForReceipt(IdGenerator.NewId(), pay.BusinessId, pay.Id, now));
                byBiz.TryGetValue(pay.BusinessId, out var biz);
                var msg = EmailTemplates.CustomerPaymentReceipt(
                    cust.Email, cust.Name, biz?.Name ?? "your shop", biz?.Phone, ord.DisplayRef,
                    Money.Date(pay.RecordedAtUtc), Money.Format(pay.Amount), PaymentMethod.DisplayName(pay.Method),
                    Money.Format(ord.TotalAmount), Money.Format(paid), balanceLine);
                (pending ??= []).Add(msg);
            }
        }

        db.PendingCustomerEmails = pending;
    }

    public static void Flush(ApplicationDbContext db, IEmailSender email)
    {
        var list = db.PendingCustomerEmails;
        db.PendingCustomerEmails = null;
        if (list is null || list.Count == 0 || !email.IsConfigured)
        {
            return;
        }

        foreach (var message in list)
        {
            email.TryQueue(message);
        }
    }

    private static bool ShouldEmailStatus(WhatsAppSettings? wa, string status)
    {
        if (wa is { Enabled: true })
        {
            return wa.Sends(status);
        }

        return DefaultStatuses.Contains(status, StringComparer.Ordinal);
    }

    private static string? ResolveAppBase(ApplicationDbContext db)
    {
        // Interceptor supplies AppBaseUrl via Ambient when available; templates tolerate null.
        return CustomerEmailAmbient.AppBaseUrl;
    }
}

/// <summary>Request-scoped ambient values for customer email templates (set by the interceptor).</summary>
internal static class CustomerEmailAmbient
{
    private static readonly AsyncLocal<string?> AppBase = new();
    public static string? AppBaseUrl
    {
        get => AppBase.Value;
        set => AppBase.Value = value;
    }
}
