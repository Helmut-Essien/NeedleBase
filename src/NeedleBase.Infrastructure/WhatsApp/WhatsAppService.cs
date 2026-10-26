using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NeedleBase.Application.Abstractions;
using NeedleBase.Application.Audit;
using NeedleBase.Application.Contracts.WhatsApp;
using NeedleBase.Domain.Entities;
using NeedleBase.Domain.Enums;
using NeedleBase.Infrastructure.Persistence;

namespace NeedleBase.Infrastructure.WhatsApp;

public sealed record WhatsAppResult<T>(T? Value, string? ErrorCode)
{
    public bool Succeeded => ErrorCode is null;
    public static WhatsAppResult<T> Ok(T value) => new(value, null);
    public static WhatsAppResult<T> Fail(string code) => new(default, code);
}

/// <summary>Settings, consent, message log and tap-to-send audit for the caller's business (tenant-filtered).</summary>
public sealed class WhatsAppService
{
    public const int PageSize = 50;
    public const int MaxPage = 200;

    private readonly ApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;
    private readonly IAuditLog _audit;
    private readonly IOptionsMonitor<WhatsAppOptions> _options;

    public WhatsAppService(ApplicationDbContext db, ICurrentUser user, IClock clock, IAuditLog audit, IOptionsMonitor<WhatsAppOptions> options)
    {
        _db = db;
        _user = user;
        _clock = clock;
        _audit = audit;
        _options = options;
    }

    private IReadOnlyList<string> Available(WhatsAppOptions o) =>
        o.IsConfigured
            ? OrderStatus.All.Where(s => s != OrderStatus.New && o.TemplateFor(s) is not null).ToList()
            : [];

    public async Task<WhatsAppSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        var o = _options.CurrentValue;
        var row = await _db.WhatsAppSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var available = Available(o);
        return new WhatsAppSettingsDto(
            o.IsConfigured,
            o.IsConfigured && row?.Enabled == true,
            row?.StatusList.Where(available.Contains).ToList() ?? [],
            available,
            (int)Math.Ceiling(Math.Max(0, o.DelaySeconds) / 60.0),
            row?.UpdatedAtUtc);
    }

    public async Task<WhatsAppResult<WhatsAppSettingsDto>> UpdateSettingsAsync(UpdateWhatsAppSettingsRequest request, CancellationToken cancellationToken = default)
    {
        var o = _options.CurrentValue;
        var statuses = (request.Statuses ?? []).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).Distinct().ToList();
        if (request.Enabled && !o.IsConfigured)
        {
            return WhatsAppResult<WhatsAppSettingsDto>.Fail(WhatsAppErrorCodes.NotConfigured);
        }

        var available = Available(o);
        if (statuses.Count > OrderStatus.All.Count || statuses.Any(s => !available.Contains(s)))
        {
            return WhatsAppResult<WhatsAppSettingsDto>.Fail(WhatsAppErrorCodes.NoTemplate);
        }

        var businessId = _user.BusinessId!;
        var now = _clock.UtcNow;
        var row = await _db.WhatsAppSettings.FirstOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            row = WhatsAppSettings.Create(businessId, now);
            _db.WhatsAppSettings.Add(row);
        }

        var before = $"{(row.Enabled ? "on" : "off")} [{row.Statuses}]";
        row.Update(request.Enabled, statuses, _user.UserId, now);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Two first-time saves at once: the other one created the row; apply ours on top.
            _db.ChangeTracker.Clear();
            row = await _db.WhatsAppSettings.FirstAsync(cancellationToken);
            row.Update(request.Enabled, statuses, _user.UserId, now);
            await _db.SaveChangesAsync(cancellationToken);
        }

        var after = $"{(row.Enabled ? "on" : "off")} [{row.Statuses}]";
        if (before != after)
        {
            await _audit.WriteAsync(new AuditEntry(AuditActions.WhatsAppSettingsChanged,
                $"Automatic WhatsApp messages: {after}", businessId, _user.UserId, _user.Role, "whatsapp_settings", businessId),
                cancellationToken: cancellationToken);
        }

        return WhatsAppResult<WhatsAppSettingsDto>.Ok(await GetSettingsAsync(cancellationToken));
    }

    public async Task<WhatsAppResult<WhatsAppConsentDto>> GetConsentAsync(string customerId, CancellationToken cancellationToken = default)
    {
        if (!await _db.Customers.AsNoTracking().AnyAsync(c => c.Id == customerId, cancellationToken))
        {
            return WhatsAppResult<WhatsAppConsentDto>.Fail(WhatsAppErrorCodes.NotFound);
        }

        var row = await _db.WhatsAppConsents.AsNoTracking().FirstOrDefaultAsync(c => c.CustomerId == customerId, cancellationToken);
        return WhatsAppResult<WhatsAppConsentDto>.Ok(new WhatsAppConsentDto(customerId, row?.OptedIn == true, row?.Source, row?.ChangedAtUtc));
    }

    public Task<WhatsAppResult<WhatsAppConsentDto>> SetConsentAsync(string customerId, bool optedIn, CancellationToken cancellationToken = default) =>
        SetConsentAsync(customerId, optedIn, null, null, cancellationToken);

    public async Task<WhatsAppResult<WhatsAppConsentDto>> SetConsentAsync(string customerId, bool optedIn, DateTimeOffset? changedAtUtc,
        DateTimeOffset? deviceNowUtc, CancellationToken cancellationToken = default)
    {
        var customer = await _db.Customers.AsNoTracking().Where(c => c.Id == customerId).Select(c => new { c.Id, c.Phone })
            .FirstOrDefaultAsync(cancellationToken);
        if (customer is null)
        {
            return WhatsAppResult<WhatsAppConsentDto>.Fail(WhatsAppErrorCodes.NotFound);
        }

        var now = _clock.UtcNow;
        var at = ChangeTime(changedAtUtc, deviceNowUtc, now);
        var row = await _db.WhatsAppConsents.FirstOrDefaultAsync(c => c.CustomerId == customerId, cancellationToken);
        if (row is not null && row.ChangedAtUtc > at)
        {
            // An offline change that arrives late never overrides a newer one (e.g. the customer replied STOP since).
            return WhatsAppResult<WhatsAppConsentDto>.Ok(new WhatsAppConsentDto(customerId, row.OptedIn, row.Source, row.ChangedAtUtc));
        }

        var changed = row is null || row.OptedIn != optedIn;
        if (row is null)
        {
            row = WhatsAppConsent.Create(_user.BusinessId!, customerId, at);
            _db.WhatsAppConsents.Add(row);
        }

        if (changed)
        {
            row.Set(optedIn, "staff", _user.UserId, at);
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                _db.ChangeTracker.Clear();
                row = await _db.WhatsAppConsents.FirstAsync(c => c.CustomerId == customerId, cancellationToken);
                if (row.ChangedAtUtc > at || row.OptedIn == optedIn)
                {
                    return WhatsAppResult<WhatsAppConsentDto>.Ok(new WhatsAppConsentDto(customerId, row.OptedIn, row.Source, row.ChangedAtUtc));
                }

                row.Set(optedIn, "staff", _user.UserId, at);
                await _db.SaveChangesAsync(cancellationToken);
            }

            await _audit.WriteAsync(new AuditEntry(AuditActions.WhatsAppConsentChanged,
                optedIn
                    ? $"Customer agreed to WhatsApp messages ({AuditMask.Phone(customer.Phone)}){Offline(at, now)}"
                    : $"Customer stopped WhatsApp messages ({AuditMask.Phone(customer.Phone)}){Offline(at, now)}",
                _user.BusinessId, _user.UserId, _user.Role, "customer", customerId), cancellationToken: cancellationToken);
        }

        return WhatsAppResult<WhatsAppConsentDto>.Ok(new WhatsAppConsentDto(customerId, row.OptedIn, row.Source, row.ChangedAtUtc));
    }

    /// <summary>
    /// When the choice was made, on the server's clock: the phone's time corrected by the phone's clock error
    /// (device now vs server now; days offline is fine), never in the future. Decides which change is newer.
    /// </summary>
    internal static DateTimeOffset ChangeTime(DateTimeOffset? changedAtUtc, DateTimeOffset? deviceNowUtc, DateTimeOffset now)
    {
        if (changedAtUtc is not { } changed)
        {
            return now;
        }

        if (deviceNowUtc is { } deviceNow && deviceNow >= changed)
        {
            changed = now - (deviceNow - changed); // same age as the phone saw it, on the server's clock
        }

        return changed < now ? changed : now;
    }

    /// <summary>Audit metadata for a change made earlier on a phone (the audit row itself has the server time).</summary>
    private static string Offline(DateTimeOffset at, DateTimeOffset now) =>
        now - at > TimeSpan.FromMinutes(2) ? $" — chosen on a phone at {at:yyyy-MM-dd HH:mm} UTC, saved when it synced" : string.Empty;

    public async Task<WhatsAppMessagePageDto> GetMessagesAsync(string? orderId, int page, CancellationToken cancellationToken = default)
    {
        page = Math.Clamp(page, 1, MaxPage);
        var query = _db.WhatsAppMessages.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(orderId))
        {
            query = query.Where(m => m.OrderId == orderId);
        }

        var rows = await (
                from m in query
                join o in _db.Orders.AsNoTracking() on m.OrderId equals o.Id into os
                from o in os.DefaultIfEmpty()
                join c in _db.Customers.AsNoTracking() on m.CustomerId equals c.Id into cs
                from c in cs.DefaultIfEmpty()
                orderby m.CreatedAtUtc descending, m.Id descending
                select new
                {
                    m.Id, m.OrderId, Ref = o == null ? null : o.DisplayRef, Name = c == null ? null : c.Name,
                    Phone = c == null ? null : c.Phone, m.OrderStatus, m.State, m.Attempts, m.CreatedAtUtc, m.UpdatedAtUtc, m.Error
                })
            .Skip((page - 1) * PageSize)
            .Take(PageSize + 1)
            .ToListAsync(cancellationToken);
        var items = rows.Take(PageSize)
            .Select(r => new WhatsAppMessageDto(r.Id, r.OrderId, r.Ref, r.Name, AuditMask.Phone(r.Phone), r.OrderStatus, r.State,
                r.Attempts, r.CreatedAtUtc, r.UpdatedAtUtc, r.Error))
            .ToList();
        return new WhatsAppMessagePageDto(items, page, PageSize, rows.Count > PageSize);
    }

    /// <summary>Audit: someone opened WhatsApp with a prefilled message (we can't know whether they pressed send).</summary>
    public async Task<bool> RecordOpenedAsync(string orderId, string? status, CancellationToken cancellationToken = default)
    {
        var order = await _db.Orders.AsNoTracking().Where(o => o.Id == orderId)
            .Select(o => new { o.Id, o.Status, o.CustomerId }).FirstOrDefaultAsync(cancellationToken);
        if (order is null)
        {
            return false;
        }

        var shown = OrderStatus.IsValid(status) ? status! : order.Status;
        await _audit.WriteAsync(new AuditEntry(AuditActions.WhatsAppOpened,
                $"Opened WhatsApp to message the customer (\"{OrderStatus.DisplayName(shown)}\" message)",
                _user.BusinessId, _user.UserId, _user.Role, "order", order.Id),
            throttleKey: $"wa-open:{_user.UserId}:{order.Id}", window: TimeSpan.FromSeconds(30), cancellationToken: cancellationToken);
        return true;
    }
}
