using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace NeedleBase.Infrastructure.Health;

/// <summary>Readiness probe: verifies PostgreSQL is reachable (singleton-safe).</summary>
public sealed class DatabaseHealthCheck : IHealthCheck
{
    private readonly string _connectionString;

    public DatabaseHealthCheck(IConfiguration configuration)
    {
        var configured = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not configured.");
        }

        _connectionString = configured;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            command.CommandTimeout = 5;
            _ = await command.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy("Database is reachable.");
        }
        catch (Exception)
        {
            return HealthCheckResult.Unhealthy("Database unavailable.");
        }
    }
}
