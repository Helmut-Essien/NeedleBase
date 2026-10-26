using NeedleBase.Domain.Enums;

namespace NeedleBase.Domain.Entities;

/// <summary>
/// An invite for someone to join a business as Manager or Tailor. Only a hash of the one-time code is stored; the
/// owner shares the code (WhatsApp/SMS/in person) because no email/SMS provider is configured yet.
/// </summary>
public class Invitation : Entity, IBusinessScoped
{
    public const int EmailMax = 256;
    public const int NameMax = 100;
    public const int CodeHashMax = 64;
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    public string BusinessId { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string? Name { get; private set; }
    public string Role { get; private set; } = BusinessRole.Tailor;
    public string CodeHash { get; private set; } = string.Empty;
    public string CreatedByUserId { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset? AcceptedAtUtc { get; private set; }
    public string? AcceptedUserId { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }

    private Invitation()
    {
    }

    public static Invitation Create(
        string id, string businessId, string email, string? name, string role, string codeHash, string createdByUserId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(businessId);
        ArgumentException.ThrowIfNullOrWhiteSpace(codeHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(createdByUserId);
        var e = email?.Trim() ?? string.Empty;
        if (e.Length is 0 or > EmailMax || !e.Contains('@'))
        {
            throw new ArgumentException("A valid email is required.", nameof(email));
        }

        if (role is not (BusinessRole.Manager or BusinessRole.Tailor))
        {
            throw new ArgumentException($"Role must be {BusinessRole.Manager} or {BusinessRole.Tailor}.", nameof(role));
        }

        var n = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        if (n is { Length: > NameMax })
        {
            throw new ArgumentException("Name is too long.", nameof(name));
        }

        return new Invitation
        {
            Id = id,
            BusinessId = businessId,
            Email = e,
            Name = n,
            Role = role,
            CodeHash = codeHash,
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = now,
            ExpiresAtUtc = now + Lifetime
        };
    }

    public string StatusAt(DateTimeOffset now) =>
        AcceptedAtUtc is not null ? InvitationStatus.Accepted
        : RevokedAtUtc is not null ? InvitationStatus.Revoked
        : now >= ExpiresAtUtc ? InvitationStatus.Expired
        : InvitationStatus.Pending;

    public bool IsPendingAt(DateTimeOffset now) => StatusAt(now) == InvitationStatus.Pending;

    public void Accept(string userId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        if (!IsPendingAt(now))
        {
            throw new InvalidOperationException("This invite can no longer be used.");
        }

        AcceptedAtUtc = now;
        AcceptedUserId = userId;
    }

    public void Revoke(DateTimeOffset now)
    {
        if (AcceptedAtUtc is not null)
        {
            throw new InvalidOperationException("An accepted invite can't be revoked.");
        }

        RevokedAtUtc ??= now;
    }
}
