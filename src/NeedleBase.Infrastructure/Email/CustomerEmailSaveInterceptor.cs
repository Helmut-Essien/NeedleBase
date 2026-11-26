using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using NeedleBase.Application.Abstractions;
using NeedleBase.Infrastructure.Persistence;

namespace NeedleBase.Infrastructure.Email;

/// <summary>
/// After a successful SaveChanges, queues any customer emails collected by <see cref="CustomerEmailOutbox"/>.
/// Collect runs inside SaveChanges (before commit) so dedupe rows roll back with the business change.
/// </summary>
public sealed class CustomerEmailSaveInterceptor : SaveChangesInterceptor
{
    private readonly IEmailSender _email;
    private readonly IOptionsMonitor<EmailOptions> _options;

    public CustomerEmailSaveInterceptor(IEmailSender email, IOptionsMonitor<EmailOptions> options)
    {
        _email = email;
        _options = options;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is ApplicationDbContext db && _email.IsConfigured)
        {
            CustomerEmailAmbient.AppBaseUrl = _options.CurrentValue.AppBaseUrl;
            CustomerEmailOutbox.CollectAsync(db, CancellationToken.None).GetAwaiter().GetResult();
        }

        return base.SavingChanges(eventData, result);
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        // Skip when SMTP is off so we do not mark CustomerEmailSent without ever queuing mail.
        if (eventData.Context is ApplicationDbContext db && _email.IsConfigured)
        {
            CustomerEmailAmbient.AppBaseUrl = _options.CurrentValue.AppBaseUrl;
            await CustomerEmailOutbox.CollectAsync(db, cancellationToken).ConfigureAwait(false);
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        if (eventData.Context is ApplicationDbContext db)
        {
            CustomerEmailOutbox.Flush(db, _email);
            CustomerEmailAmbient.AppBaseUrl = null;
        }

        return base.SavedChanges(eventData, result);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is ApplicationDbContext db)
        {
            CustomerEmailOutbox.Flush(db, _email);
            CustomerEmailAmbient.AppBaseUrl = null;
        }

        return await base.SavedChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        if (eventData.Context is ApplicationDbContext db)
        {
            db.PendingCustomerEmails = null;
            CustomerEmailAmbient.AppBaseUrl = null;
        }

        base.SaveChangesFailed(eventData);
    }

    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is ApplicationDbContext db)
        {
            db.PendingCustomerEmails = null;
            CustomerEmailAmbient.AppBaseUrl = null;
        }

        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }
}
