using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NeedleBase.Application.Abstractions;
using NeedleBase.Application.Audit;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Audit;
using NeedleBase.Application.Contracts.Staff;
using NeedleBase.Domain.Entities;
using NeedleBase.Domain.Enums;
using NeedleBase.Infrastructure.Persistence;

namespace NeedleBase.Infrastructure.Audit;

/// <summary>
/// Activity log reads. Owner: everything for the business. Manager: everything except security events (auth.*),
/// and no IP/user-agent. Tailor: nothing (the controller refuses first). Names come from this business's members
/// (former members included), never from other businesses.
/// </summary>
public sealed class AuditQueryService : IAuditQueryService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;

    public AuditQueryService(ApplicationDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<PaginatedResponse<AuditEventDto>> ListAsync(AuditQuery query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_currentUser.BusinessId))
        {
            throw new InvalidOperationException("Business context is required.");
        }

        var owner = _currentUser.Role == BusinessRole.Owner;
        var (page, pageSize) = PaginatedResponse<AuditEventDto>.Normalize(query.Page, query.PageSize);
        var rows = _db.AuditEvents.AsNoTracking(); // tenant-filtered (fail-closed)
        if (!owner)
        {
            rows = rows.Where(e => !e.Action.StartsWith("auth."));
        }

        if (Clean(query.UserId) is { } user)
        {
            rows = rows.Where(e => e.ActorUserId == user);
        }

        if (Clean(query.Action) is { } action)
        {
            rows = action.EndsWith('.') ? rows.Where(e => e.Action.StartsWith(action)) : rows.Where(e => e.Action == action);
        }

        if (Clean(query.EntityType) is { } type)
        {
            rows = rows.Where(e => e.EntityType == type);
        }

        if (Clean(query.EntityId) is { } entityId)
        {
            rows = rows.Where(e => e.EntityId == entityId);
        }

        if (Clean(query.OrderId) is { } orderId)
        {
            rows = rows.Where(e => e.OrderId == orderId);
        }

        if (Clean(query.CustomerId) is { } customerId)
        {
            rows = rows.Where(e => e.CustomerId == customerId);
        }

        if (Clean(query.Source) is { } source)
        {
            rows = rows.Where(e => e.Source == source);
        }

        if (query.From is { } from)
        {
            var start = BusinessTime.StartOfDayUtc(from);
            rows = rows.Where(e => e.OccurredAtUtc >= start);
        }

        if (query.To is { } to && to < DateOnly.MaxValue)
        {
            var end = BusinessTime.StartOfDayUtc(to.AddDays(1));
            rows = rows.Where(e => e.OccurredAtUtc < end);
        }

        if (Clean(query.Search) is { } search)
        {
            var pattern = "%" + search.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            rows = rows.Where(e => EF.Functions.ILike(e.Summary, pattern, "\\"));
        }

        var total = await rows.CountAsync(cancellationToken).ConfigureAwait(false);
        var items = await rows.OrderByDescending(e => e.OccurredAtUtc).ThenByDescending(e => e.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var changes = items.ToDictionary(e => e.Id, e => ParseChanges(e.ChangesJson));
        var userIds = items.Select(e => e.ActorUserId)
            .Concat(changes.Values.SelectMany(c => c).Where(c => c.Field == nameof(Order.AssignedToUserId)).SelectMany(c => new[] { c.Old, c.New }))
            .Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
        var names = userIds.Count == 0
            ? new Dictionary<string, string>()
            : await (from m in _db.StaffMemberships.AsNoTracking() // this business's members only, former included
                     join u in _db.Users.AsNoTracking() on m.UserId equals u.Id
                     where userIds.Contains(m.UserId)
                     select new { m.UserId, u.FirstName, u.Surname })
                .ToDictionaryAsync(x => x.UserId, x => StaffNameDto.Compose(x.FirstName, x.Surname), cancellationToken)
                .ConfigureAwait(false);

        var data = items.Select(e => new AuditEventDto(
            e.Id,
            e.OccurredAtUtc,
            e.ActorUserId,
            e.ActorUserId is null ? "System" : names.GetValueOrDefault(e.ActorUserId, "Former team member"),
            e.ActorRole,
            e.Action,
            e.EntityType,
            e.EntityId,
            e.OrderId,
            e.CustomerId,
            e.Summary,
            changes[e.Id].Select(c => c.Field == nameof(Order.AssignedToUserId)
                ? c with { Field = "Assignee", Old = Name(c.Old), New = Name(c.New) }
                : c).ToList(),
            e.Source,
            e.DeviceId,
            owner ? e.IpAddress : null,
            owner ? e.UserAgent : null)).ToList();
        return PaginatedResponse<AuditEventDto>.Create(data, total, page, pageSize);

        string? Name(string? id) => id is null ? null : names.GetValueOrDefault(id, "Former team member");
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static List<AuditChangeDto> ParseChanges(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<AuditChangeDto>>(json, Json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
