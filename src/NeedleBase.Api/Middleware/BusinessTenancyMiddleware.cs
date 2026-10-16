using NeedleBase.Application.Abstractions;
using NeedleBase.Infrastructure.Persistence;

namespace NeedleBase.Api.Middleware;

/// <summary>
/// Sets <see cref="ApplicationDbContext.CurrentBusinessId"/> from JWT so global query filters apply.
/// </summary>
public sealed class BusinessTenancyMiddleware
{
    private readonly RequestDelegate _next;

    public BusinessTenancyMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ApplicationDbContext db, ICurrentUser currentUser)
    {
        if (!string.IsNullOrEmpty(currentUser.BusinessId))
        {
            db.CurrentBusinessId = currentUser.BusinessId;
        }

        await _next(context);
    }
}
