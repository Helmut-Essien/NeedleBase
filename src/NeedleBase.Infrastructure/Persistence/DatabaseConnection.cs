using Microsoft.Extensions.Configuration;
using Npgsql;

namespace NeedleBase.Infrastructure.Persistence;

/// <summary>
/// The one PostgreSQL connection string used by EF Core and the health check (same string = same Npgsql pool).
/// Shared hosting (e.g. a MonsterASP free database) caps connections per database, while Npgsql's own default pool
/// is 100: unless the connection string sets <c>Maximum Pool Size</c> itself, <c>Database:MaxPoolSize</c> (default
/// <see cref="DefaultMaxPoolSize"/>) applies. Keep it below the database's connection limit (docs/production-config.md).
/// </summary>
public static class DatabaseConnection
{
    public const int DefaultMaxPoolSize = 20;

    public static string Build(IConfiguration configuration)
    {
        var configured = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:DefaultConnection is not configured. " +
                "Set it via user secrets or environment (ConnectionStrings__DefaultConnection).");
        }

        NpgsqlConnectionStringBuilder builder;
        try
        {
            builder = new NpgsqlConnectionStringBuilder(configured);
        }
        catch (ArgumentException ex)
        {
            // Never echo the string itself: it holds the password.
            throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not a valid PostgreSQL connection string.", ex);
        }

        if (!HasExplicitMaxPoolSize(configured))
        {
            builder.MaxPoolSize = Math.Clamp(configuration.GetValue("Database:MaxPoolSize", DefaultMaxPoolSize), 1, 1000);
        }

        if (builder.MinPoolSize > builder.MaxPoolSize)
        {
            builder.MinPoolSize = builder.MaxPoolSize;
        }

        return builder.ConnectionString;
    }

    // Npgsql's builder reports every known keyword as present, so read the keys as written.
    private static bool HasExplicitMaxPoolSize(string connectionString)
    {
        var raw = new System.Data.Common.DbConnectionStringBuilder { ConnectionString = connectionString };
        foreach (string key in raw.Keys)
        {
            var k = key.Replace(" ", "", StringComparison.Ordinal);
            if (k.Equals("MaximumPoolSize", StringComparison.OrdinalIgnoreCase) || k.Equals("MaxPoolSize", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
