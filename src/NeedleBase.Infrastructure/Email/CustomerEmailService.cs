using Microsoft.EntityFrameworkCore;
using NeedleBase.Application.Abstractions;
using NeedleBase.Application.Audit;
using NeedleBase.Application.Contracts.Customers;
using NeedleBase.Domain.Entities;
using NeedleBase.Infrastructure.Persistence;

namespace NeedleBase.Infrastructure.Email;

public sealed class CustomerEmailService
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;
    private readonly IAuditLog _audit;

    public CustomerEmailService(ApplicationDbContext db, ICurrentUser user, IClock clock, IAuditLog audit)
    {
        _db = db;
        _user = user;
        _clock = clock;
        _audit = audit;
    }

    public async Task<(CustomerEmailConsentDto? Value, string? Error)> GetConsentAsync(
        string customerId, CancellationToken cancellationToken = default)
    {
        var customer = await _db.Customers.AsNoTracking()
            .Where(c => c.Id == customerId)
            .Select(c => new { c.Id, c.Email })
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (customer is null)
        {
            return (null, "NOT_FOUND");
        }

        var row = await _db.CustomerEmailConsents.AsNoTracking()
            .FirstOrDefaultAsync(c => c.CustomerId == customerId, cancellationToken).ConfigureAwait(false);
        return (new CustomerEmailConsentDto(
            customerId, row?.OptedIn == true, row?.Source, row?.ChangedAtUtc,
            !string.IsNullOrWhiteSpace(customer.Email)), null);
    }

    public async Task<(CustomerEmailConsentDto? Value, string? Error)> SetConsentAsync(
        string customerId,
        bool optedIn,
        DateTimeOffset? changedAtUtc,
        DateTimeOffset? deviceNowUtc,
        CancellationToken cancellationToken = default)
    {
        var customer = await _db.Customers.AsNoTracking()
            .Where(c => c.Id == customerId)
            .Select(c => new { c.Id, c.Email, c.Phone })
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (customer is null)
        {
            return (null, "NOT_FOUND");
        }

        var now = _clock.UtcNow;
        var at = WhatsApp.WhatsAppService.ChangeTime(changedAtUtc, deviceNowUtc, now);
        var row = await _db.CustomerEmailConsents
            .FirstOrDefaultAsync(c => c.CustomerId == customerId, cancellationToken).ConfigureAwait(false);
        if (row is not null && row.ChangedAtUtc > at)
        {
            return (new CustomerEmailConsentDto(
                customerId, row.OptedIn, row.Source, row.ChangedAtUtc,
                !string.IsNullOrWhiteSpace(customer.Email)), null);
        }

        var changed = row is null || row.OptedIn != optedIn;
        if (row is null)
        {
            row = CustomerEmailConsent.Create(_user.BusinessId!, customerId, at);
            _db.CustomerEmailConsents.Add(row);
        }

        if (changed)
        {
            row.Set(optedIn, "staff", _user.UserId, at);
            try
            {
                await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateException)
            {
                _db.ChangeTracker.Clear();
                row = await _db.CustomerEmailConsents.FirstAsync(c => c.CustomerId == customerId, cancellationToken)
                    .ConfigureAwait(false);
                if (row.ChangedAtUtc > at || row.OptedIn == optedIn)
                {
                    return (new CustomerEmailConsentDto(
                        customerId, row.OptedIn, row.Source, row.ChangedAtUtc,
                        !string.IsNullOrWhiteSpace(customer.Email)), null);
                }

                row.Set(optedIn, "staff", _user.UserId, at);
                await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            await _audit.WriteAsync(new AuditEntry(
                AuditActions.CustomerEmailConsentChanged,
                optedIn
                    ? $"Customer agreed to email updates ({AuditMask.Phone(customer.Phone)})"
                    : $"Customer stopped email updates ({AuditMask.Phone(customer.Phone)})",
                _user.BusinessId, _user.UserId, _user.Role, "customer", customerId),
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        return (new CustomerEmailConsentDto(
            customerId, row.OptedIn, row.Source, row.ChangedAtUtc,
            !string.IsNullOrWhiteSpace(customer.Email)), null);
    }
}
