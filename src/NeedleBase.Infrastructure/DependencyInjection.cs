using Microsoft.Extensions.DependencyInjection;
using NeedleBase.Application.Abstractions;
using NeedleBase.Infrastructure.Time;

namespace NeedleBase.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers Infrastructure services. EF Core SQL Server / Identity land in Slice 1.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IClock, SystemClock>();
        return services;
    }
}
