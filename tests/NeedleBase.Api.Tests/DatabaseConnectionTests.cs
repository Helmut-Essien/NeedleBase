using Microsoft.Extensions.Configuration;
using NeedleBase.Infrastructure.Persistence;
using Npgsql;

namespace NeedleBase.Api.Tests;

public class DatabaseConnectionTests
{
    private const string Base = "Host=db.example;Database=nb;Username=u;Password=p";

    private static IConfiguration Config(string? connection, string? maxPool = null)
    {
        var values = new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = connection };
        if (maxPool is not null)
        {
            values["Database:MaxPoolSize"] = maxPool;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Fact]
    public void Default_pool_is_capped()
    {
        var b = new NpgsqlConnectionStringBuilder(DatabaseConnection.Build(Config(Base)));
        Assert.Equal(DatabaseConnection.DefaultMaxPoolSize, b.MaxPoolSize);
        Assert.Equal("db.example", b.Host);
        Assert.Equal("p", b.Password);
    }

    [Fact]
    public void Configured_pool_size_applies()
    {
        var b = new NpgsqlConnectionStringBuilder(DatabaseConnection.Build(Config(Base, "8")));
        Assert.Equal(8, b.MaxPoolSize);
    }

    [Theory]
    [InlineData("Maximum Pool Size=7")]
    [InlineData("MaxPoolSize=7")]
    public void Explicit_pool_size_in_the_string_wins(string part)
    {
        var b = new NpgsqlConnectionStringBuilder(DatabaseConnection.Build(Config(Base + ";" + part, "50")));
        Assert.Equal(7, b.MaxPoolSize);
    }

    [Fact]
    public void Min_pool_never_exceeds_max()
    {
        var b = new NpgsqlConnectionStringBuilder(DatabaseConnection.Build(Config(Base + ";Minimum Pool Size=30", "5")));
        Assert.Equal(5, b.MaxPoolSize);
        Assert.Equal(5, b.MinPoolSize);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void Missing_string_is_refused(string? connection) =>
        Assert.Throws<InvalidOperationException>(() => DatabaseConnection.Build(Config(connection)));

    [Fact]
    public void Invalid_string_is_refused_without_echoing_the_password()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => DatabaseConnection.Build(Config("Host=x;Password=s3cret;NoSuchKey=1")));
        Assert.DoesNotContain("s3cret", ex.Message);
    }
}
