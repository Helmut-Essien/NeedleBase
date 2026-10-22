using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NeedleBase.Application.Contracts.Customers;
using NeedleBase.Mobile.Data.Data;
using NeedleBase.Mobile.Data.Services;

namespace NeedleBase.Mobile.Tests;

/// <summary>SQLite WAL + private cache: readers don't wait for writers; wipe leaves no side files.</summary>
public partial class SyncEdgeCaseTests
{
    private static async Task<string> PathOfAsync(IServiceProvider device)
    {
        await using var scope = device.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        return new SqliteConnectionStringBuilder(db.Database.GetConnectionString()).DataSource;
    }

    [Fact]
    public void ConnectionString_HasBusyTimeout_AndNoSharedCache()
    {
        var builder = new SqliteConnectionStringBuilder(LocalDbConnection.Build("/x/a.db3"));
        Assert.Equal(SqliteCacheMode.Default, builder.Cache);
        Assert.Equal(LocalDbConnection.BusyTimeoutSeconds, builder.DefaultTimeout);
    }

    [Fact]
    public async Task Migrator_TurnsOnWal()
    {
        await using var device = await DeviceAsync();
        await using var scope = device.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        var mode = await db.Database.SqlQueryRaw<string>("PRAGMA journal_mode").ToListAsync();
        Assert.Equal("wal", Assert.Single(mode));
    }

    [Fact]
    public async Task ListRead_DoesNotWait_ForAnOpenSyncWriteTransaction()
    {
        await using var device = await DeviceAsync();
        await device.GetRequiredService<ICustomerLocalService>().SaveAsync(Biz, new CreateCustomerRequest("Ama", "024 555 0100", null, null));
        var path = await PathOfAsync(device);

        await using var writer = new SqliteConnection($"Data Source={path};Pooling=False");
        await writer.OpenAsync();
        await using var tx = (SqliteTransaction)await writer.BeginTransactionAsync();
        await using (var cmd = writer.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE \"Customers\" SET \"Notes\" = 'syncing' ;";
            await cmd.ExecuteNonQueryAsync();          // write lock held, like a pull page being applied
        }

        var watch = Stopwatch.StartNew();
        var rows = await device.GetRequiredService<ICustomerLocalService>().SearchAsync(Biz, string.Empty);
        Assert.Single(rows);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"read waited {watch.Elapsed}");
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task Wipe_RemovesWalSideFiles_AndData()
    {
        await using var device = await DeviceAsync();
        await device.GetRequiredService<ICustomerLocalService>().SaveAsync(Biz, new CreateCustomerRequest("Kofi", "024 555 0101", null, null));
        var path = await PathOfAsync(device);
        // Side files left behind (e.g. a pooled handle or crash kept the WAL): wipe must remove them.
        foreach (var side in LocalDbConnection.SideFiles(path))
        {
            await File.WriteAllTextAsync(side, "STALE-Kofi");
        }

        var service = new LocalDatabaseService(device.GetRequiredService<IServiceScopeFactory>(), new LocalDbOptions { DbPath = path });
        Assert.True(await service.WipeAsync());

        Assert.Empty(await device.GetRequiredService<ICustomerLocalService>().SearchAsync(Biz, string.Empty));
        await using var scope = device.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        Assert.Equal("wal", Assert.Single(await db.Database.SqlQueryRaw<string>("PRAGMA journal_mode").ToListAsync()));
        // Any side file now belongs to the fresh database and carries none of the old data.
        foreach (var side in LocalDbConnection.SideFiles(path).Where(File.Exists))
        {
            Assert.DoesNotContain("Kofi", System.Text.Encoding.Latin1.GetString(await File.ReadAllBytesAsync(side)));
        }
    }
}
