using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NeedleBase.Application.Abstractions;
using NeedleBase.Application.Audit;
using NeedleBase.Application.WhatsApp;
using NeedleBase.Domain.Entities;
using NeedleBase.Infrastructure.Persistence;

namespace NeedleBase.Infrastructure.WhatsApp;

/// <summary>
/// Background sender for the WhatsApp outbox. Never runs inside a request. Each message is claimed with a conditional
/// UPDATE (safe with several server instances), re-checked just before sending (status still the same, customer still
/// opted in, setting still on, not already sent), then sent once. Retries with backoff (1 m, 5 m, 30 m, 2 h; 5 tries).
/// </summary>
public sealed class WhatsAppSender : BackgroundService
{
    public const int BatchSize = 20;
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan StuckAfter = TimeSpan.FromMinutes(10);

    private readonly IServiceScopeFactory _scopes;
    private readonly IOptionsMonitor<WhatsAppOptions> _options;
    private readonly ILogger<WhatsAppSender> _logger;

    public WhatsAppSender(IServiceScopeFactory scopes, IOptionsMonitor<WhatsAppOptions> options, ILogger<WhatsAppSender> logger)
    {
        _scopes = scopes;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken).ConfigureAwait(false); // let startup/migrations finish
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_options.CurrentValue.IsConfigured)
                {
                    int sent;
                    do
                    {
                        sent = await RunOnceAsync(_scopes, _options.CurrentValue, DateTimeOffset.UtcNow, stoppingToken).ConfigureAwait(false);
                    }
                    while (sent >= BatchSize && !stoppingToken.IsCancellationRequested);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "WhatsApp sender pass failed; will retry");
            }

            try
            {
                await Task.Delay(Tick, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>One pass: processes up to <see cref="BatchSize"/> due messages; returns how many it handled.</summary>
    public static async Task<int> RunOnceAsync(IServiceScopeFactory scopes, WhatsAppOptions options, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!options.IsConfigured)
        {
            return 0;
        }

        List<string> due;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var stuck = now - StuckAfter;
            await db.WhatsAppMessages.IgnoreQueryFilters()
                .Where(m => m.State == WhatsAppMessageState.Sending && m.UpdatedAtUtc < stuck)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.State, WhatsAppMessageState.Pending), cancellationToken).ConfigureAwait(false);

            var settledBefore = now - TimeSpan.FromSeconds(Math.Max(0, options.DelaySeconds));
            due = await db.WhatsAppMessages.IgnoreQueryFilters().AsNoTracking()
                .Where(m => m.State == WhatsAppMessageState.Pending && m.NotBeforeUtc <= now && m.CreatedAtUtc <= settledBefore)
                .OrderBy(m => m.NotBeforeUtc).ThenBy(m => m.Id)
                .Select(m => m.Id)
                .Take(BatchSize)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
        }

        var handled = 0;
        foreach (var id in due)
        {
            await using var scope = scopes.CreateAsyncScope();
            if (await ProcessAsync(scope.ServiceProvider, options, id, now, cancellationToken).ConfigureAwait(false))
            {
                handled++;
            }
        }

        return handled;
    }

    private static async Task<bool> ProcessAsync(IServiceProvider sp, WhatsAppOptions options, string id, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var db = sp.GetRequiredService<ApplicationDbContext>();
        var claimed = await db.WhatsAppMessages.IgnoreQueryFilters()
            .Where(m => m.Id == id && m.State == WhatsAppMessageState.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.State, WhatsAppMessageState.Sending)
                .SetProperty(m => m.UpdatedAtUtc, now), cancellationToken).ConfigureAwait(false);
        if (claimed == 0)
        {
            return false; // another instance took it
        }

        var message = await db.WhatsAppMessages.IgnoreQueryFilters().SingleAsync(m => m.Id == id, cancellationToken).ConfigureAwait(false);
        var bid = message.BusinessId;
        var order = await db.Orders.IgnoreQueryFilters().AsNoTracking()
            .Where(o => o.Id == message.OrderId && o.BusinessId == bid)
            .Select(o => new { o.Status, o.DisplayRef, o.CustomerId })
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var customer = order is null
            ? null
            : await db.Customers.IgnoreQueryFilters().AsNoTracking()
                .Where(c => c.Id == order.CustomerId && c.BusinessId == bid)
                .Select(c => new { c.Id, c.Name, c.Phone })
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var settings = await db.WhatsAppSettings.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(s => s.BusinessId == bid, cancellationToken).ConfigureAwait(false);
        var optedIn = customer is not null && await db.WhatsAppConsents.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(c => c.CustomerId == customer.Id && c.BusinessId == bid && c.OptedIn, cancellationToken).ConfigureAwait(false);
        var alreadySent = await db.WhatsAppMessages.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(m => m.Id != id && m.OrderId == message.OrderId && m.OrderStatus == message.OrderStatus &&
                           (m.State == WhatsAppMessageState.Sent || m.State == WhatsAppMessageState.Delivered ||
                            m.State == WhatsAppMessageState.Read ||
                            (m.State == WhatsAppMessageState.Sending && string.Compare(m.Id, id) < 0)), cancellationToken)
            .ConfigureAwait(false);
        var template = options.TemplateFor(message.OrderStatus);
        var digits = WhatsAppLinks.ToWhatsAppDigits(customer?.Phone);

        var skip = order is null ? "Order deleted"
            : order.Status != message.OrderStatus ? $"Status changed to {order.Status} before sending"
            : customer is null ? "Customer deleted"
            : settings is null || !settings.Sends(message.OrderStatus) ? "Turned off for this status"
            : !optedIn ? "Customer has not agreed to WhatsApp messages"
            : alreadySent ? "Already sent"
            : template is null ? "No template set up for this status"
            : digits is null ? "No WhatsApp-ready phone number"
            : null;
        if (skip is not null)
        {
            message.Skip(skip, now);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }

        var lastHour = now - TimeSpan.FromHours(1);
        var sentLastHour = await db.WhatsAppMessages.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(m => m.BusinessId == bid && m.SentAtUtc != null && m.SentAtUtc >= lastHour, cancellationToken)
            .ConfigureAwait(false);
        if (sentLastHour >= Math.Max(1, options.MaxPerBusinessPerHour))
        {
            await db.WhatsAppMessages.IgnoreQueryFilters().Where(m => m.Id == id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.State, WhatsAppMessageState.Pending)
                    .SetProperty(m => m.NotBeforeUtc, now + TimeSpan.FromMinutes(10)), cancellationToken).ConfigureAwait(false);
            return true;
        }

        var businessName = await db.Businesses.AsNoTracking().Where(b => b.Id == bid).Select(b => b.Name)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var parameters = new[]
        {
            FirstName(customer!.Name),
            Param(businessName ?? "your tailor"),
            Param(order!.DisplayRef)
        };

        var client = sp.GetRequiredService<IWhatsAppClient>();
        var result = await client.SendTemplateAsync(digits!, template!, options.TemplateLanguage, parameters, cancellationToken)
            .ConfigureAwait(false);
        if (result.Ok)
        {
            message.MarkSent(result.ProviderMessageId!, now);
        }
        else
        {
            message.Fail(result.Error ?? "Send failed", result.Permanent, now);
        }

        // Not cancellable: once Meta accepted the message, losing this save (shutdown) would send it again later.
        await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        if (result.Ok || message.State == WhatsAppMessageState.Failed)
        {
            var audit = sp.GetRequiredService<IAuditLog>();
            var what = result.Ok ? AuditActions.WhatsAppSent : AuditActions.WhatsAppFailed;
            var summary = result.Ok
                ? $"WhatsApp \"{message.OrderStatus}\" message sent to {AuditMask.Phone("+" + digits)}"
                : $"WhatsApp \"{message.OrderStatus}\" message failed for {AuditMask.Phone("+" + digits)}";
            await audit.WriteAsync(new AuditEntry(what, summary, bid, null, null, "order", message.OrderId), cancellationToken: CancellationToken.None)
                .ConfigureAwait(false);
        }

        return true;
    }

    /// <summary>Template parameters can't contain new lines, tabs or 4+ spaces (Meta rejects them).</summary>
    internal static string Param(string? value)
    {
        var s = string.Join(' ', (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (s.Length == 0)
        {
            s = "-";
        }

        return NeedleBase.Domain.Common.TextCut.Max(s, 60);
    }

    internal static string FirstName(string? name)
    {
        var first = (name ?? string.Empty).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return Param(string.IsNullOrEmpty(first) ? "there" : first);
    }
}
