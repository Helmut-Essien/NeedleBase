using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NeedleBase.Application.Contracts.WhatsApp;
using NeedleBase.Mobile.Data.Data;

namespace NeedleBase.Mobile.Data.Services;

/// <summary>What the phone shows for one customer.</summary>
public sealed record WhatsAppConsentView(bool OptedIn, bool Pending, bool RepliedStop, DateTimeOffset? ChangedAtUtc);

public enum ConsentPushOutcome
{
    Saved,
    NotFound,
    Forbidden,
    Offline,
    Failed
}

public sealed record ConsentPushResult(ConsentPushOutcome Outcome, WhatsAppConsentDto? Value = null);

/// <summary>Online consent calls (implemented by the app's HTTP client; faked in tests).</summary>
public interface IWhatsAppConsentApi
{
    Task<ConsentPushResult> GetConsentAsync(string customerId, CancellationToken cancellationToken = default);
    Task<ConsentPushResult> SetConsentAsync(string customerId, bool optedIn, DateTimeOffset changedAtUtc, CancellationToken cancellationToken = default);
}

/// <summary>
/// Customer WhatsApp consent on the phone: works offline (pending choice saved locally), pushed after each sync,
/// and the server's answer always wins (a newer change on another device or a STOP reply is kept).
/// The server writes the audit entry when the change arrives.
/// </summary>
public sealed class WhatsAppConsentSync
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SemaphoreSlim _pushGate = new(1, 1);

    public WhatsAppConsentSync(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public static WhatsAppConsentView ToView(LocalWhatsAppConsent? row) => row is null
        ? new WhatsAppConsentView(false, false, false, null)
        : new WhatsAppConsentView(row.PendingOptedIn ?? row.OptedIn, row.PendingOptedIn is not null,
            row.PendingOptedIn is null && !row.OptedIn && row.Source == "reply", row.PendingAtUtc ?? row.ChangedAtUtc);

    public async Task<WhatsAppConsentView> GetAsync(string businessId, string customerId, CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        var row = await db.WhatsAppConsents.AsNoTracking()
            .FirstOrDefaultAsync(c => c.CustomerId == customerId && c.BusinessId == businessId, cancellationToken).ConfigureAwait(false);
        return ToView(row);
    }

    /// <summary>Saves the choice on this phone (offline-safe). Choosing the server's current value clears the pending change.</summary>
    public async Task<WhatsAppConsentView> SetAsync(string businessId, string customerId, bool optedIn, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(businessId);
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        var row = await db.WhatsAppConsents.FirstOrDefaultAsync(c => c.CustomerId == customerId, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            row = new LocalWhatsAppConsent { CustomerId = customerId, BusinessId = businessId };
            db.WhatsAppConsents.Add(row);
        }
        else if (row.BusinessId != businessId)
        {
            throw new InvalidOperationException("Customer belongs to another business.");
        }

        if (row.Source is not null && row.OptedIn == optedIn)
        {
            row.PendingOptedIn = null; // back to what the server already has: nothing to send
            row.PendingAtUtc = null;
        }
        else
        {
            row.PendingOptedIn = optedIn;
            row.PendingAtUtc = now;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
        return ToView(row);
    }

    /// <summary>Raised after a choice is saved on this device (the app starts a sync so it goes out and the badge updates).</summary>
    public event EventHandler? Changed;

    /// <summary>Stores the server's state. A pending choice is kept unless the server already reflects it or is newer.</summary>
    public async Task<WhatsAppConsentView> ApplyServerAsync(string businessId, WhatsAppConsentDto dto, CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        var row = await db.WhatsAppConsents.FirstOrDefaultAsync(c => c.CustomerId == dto.CustomerId, cancellationToken).ConfigureAwait(false);
        if (row is not null && row.BusinessId != businessId)
        {
            return ToView(null);
        }

        if (row is null)
        {
            row = new LocalWhatsAppConsent { CustomerId = dto.CustomerId, BusinessId = businessId };
            db.WhatsAppConsents.Add(row);
        }

        Apply(row, dto);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToView(row);
    }

    private static void Apply(LocalWhatsAppConsent row, WhatsAppConsentDto dto)
    {
        row.OptedIn = dto.OptedIn;
        row.Source = dto.Source ?? "none";
        row.ChangedAtUtc = dto.ChangedAtUtc;
        if (row.PendingOptedIn is { } pending &&
            (pending == dto.OptedIn || (dto.ChangedAtUtc is { } server && row.PendingAtUtc is { } mine && server > mine)))
        {
            row.PendingOptedIn = null;
            row.PendingAtUtc = null;
        }
    }

    public async Task<int> PendingCountAsync(string businessId, CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        return await db.WhatsAppConsents.CountAsync(c => c.BusinessId == businessId && c.PendingOptedIn != null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends pending choices (after the record sync, so new customers exist on the server). Stops at the first
    /// offline/failed call; a customer the server doesn't know yet stays pending unless it's gone from this phone too;
    /// a refused change (role no longer allowed) is dropped and the server state kept. Returns how many were sent.
    /// </summary>
    public async Task<int> PushAsync(string businessId, IWhatsAppConsentApi api, CancellationToken cancellationToken = default)
    {
        // Waits for a push already running (then usually finds nothing left), so the caller sees the final state.
        await _pushGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
            var pending = await db.WhatsAppConsents
                .Where(c => c.BusinessId == businessId && c.PendingOptedIn != null)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            var sent = 0;
            foreach (var row in pending)
            {
                var sentValue = row.PendingOptedIn!.Value;
                var sentAt = row.PendingAtUtc;
                var result = await api.SetConsentAsync(row.CustomerId, sentValue, sentAt ?? DateTimeOffset.UtcNow, cancellationToken)
                    .ConfigureAwait(false);
                if (result.Outcome is ConsentPushOutcome.Saved or ConsentPushOutcome.Forbidden)
                {
                    // Someone may have changed it again on this phone during the call: re-read so that newer choice isn't lost.
                    await db.Entry(row).ReloadAsync(cancellationToken).ConfigureAwait(false);
                }

                var unchanged = row.PendingOptedIn == sentValue && row.PendingAtUtc == sentAt;
                switch (result.Outcome)
                {
                    case ConsentPushOutcome.Saved when result.Value is not null:
                        Apply(row, result.Value);
                        if (unchanged || row.PendingOptedIn == row.OptedIn)
                        {
                            row.PendingOptedIn = null;
                            row.PendingAtUtc = null;
                        }

                        sent++;
                        break;
                    case ConsentPushOutcome.NotFound:
                        if (!await db.Customers.AnyAsync(c => c.BusinessId == businessId && c.Id == row.CustomerId, cancellationToken).ConfigureAwait(false))
                        {
                            db.WhatsAppConsents.Remove(row); // customer deleted: nothing left to record against
                        }

                        break;
                    case ConsentPushOutcome.Forbidden:
                        row.PendingOptedIn = null; // role can't change it any more: keep the server's state
                        row.PendingAtUtc = null;
                        break;
                    case ConsentPushOutcome.Offline:
                    case ConsentPushOutcome.Failed:
                        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                        return sent;
                }

                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false); // keep progress if the app is closed mid-way
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return sent;
        }
        finally
        {
            _pushGate.Release();
        }
    }
}
