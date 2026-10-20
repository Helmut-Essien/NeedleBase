using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NeedleBase.Application.Contracts.Customers;
using NeedleBase.Application.Contracts.Orders;
using NeedleBase.Domain.Enums;
using NeedleBase.Mobile.Data.Data;
using NeedleBase.Mobile.Data.Services;
using NeedleBase.Mobile.Helpers;

namespace NeedleBase.Mobile.Tests;

/// <summary>Search-as-you-type: one matching rule for Customers tab, Orders list and New order picker.</summary>
public class CustomerSearchTests
{
    private const string Biz = "biz-search";

    [Theory]
    [InlineData("efua")]
    [InlineData("EFUA")]
    [InlineData("men")]          // any word prefix
    [InlineData("efua men")]
    [InlineData("024")]          // local 0 prefix
    [InlineData("24 555")]       // spaces
    [InlineData("+23324")]       // country code
    [InlineData("0245550")]
    public async Task Search_MatchesNamePrefixAndPhoneVariants(string term)
    {
        await using var provider = await BuildAsync();
        var customers = provider.GetRequiredService<ICustomerLocalService>();
        var efua = await AddAsync(customers, "Efua Mensah", "+233 24 555 0123");
        await AddAsync(customers, "Kwame Boateng", "0201112222");

        var results = await customers.SearchAsync(Biz, term);

        Assert.Single(results);
        Assert.Equal(efua, results[0].Id);
    }

    [Theory]
    [InlineData("fua")]     // middle of a word is not a word prefix
    [InlineData("555")]     // middle of a number
    [InlineData("%")]       // LIKE wildcard is ignored, never matches everything
    public async Task Search_DoesNotMatchInsideWords(string term)
    {
        await using var provider = await BuildAsync();
        var customers = provider.GetRequiredService<ICustomerLocalService>();
        await AddAsync(customers, "Efua Mensah", "+233 24 555 0123");

        var results = await customers.SearchAsync(Biz, term);

        if (term == "%")
        {
            // Wildcard-only input has no letters/digits: treated as "no filter", not as a pattern.
            Assert.Single(results);
            return;
        }

        Assert.Empty(results);
    }

    [Fact]
    public async Task Search_RespectsLimit_AndRanksNameStartFirst()
    {
        await using var provider = await BuildAsync();
        var customers = provider.GetRequiredService<ICustomerLocalService>();
        for (var i = 0; i < 40; i++)
        {
            await AddAsync(customers, $"Ama Customer {i:D2}", $"02000000{i:D2}");
        }

        await AddAsync(customers, "Zed Ama", "0209999999");

        var limited = await customers.SearchAsync(Biz, "ama", take: 30);
        Assert.Equal(30, limited.Count);

        var ranked = await customers.SearchAsync(Biz, "ama", take: 100);
        Assert.Equal("Zed Ama", ranked[^1].Name);
    }

    [Fact]
    public async Task Recent_PrefersCustomersWithNewestOrders()
    {
        await using var provider = await BuildAsync();
        var customers = provider.GetRequiredService<ICustomerLocalService>();
        var orders = provider.GetRequiredService<IOrderLocalService>();
        var older = await AddAsync(customers, "Abena", "0241111111");
        var withOrder = await AddAsync(customers, "Yaa", "0242222222");
        await AddAsync(customers, "Esi", "0243333333");

        var (order, error) = await orders.SaveAsync(Biz, new CreateOrderRequest
        {
            CustomerId = withOrder,
            Lines =
            [
                new CreateOrderLineRequest
                {
                    GarmentTemplate = GarmentTemplate.Shirt,
                    Quantity = 1,
                    UnitPrice = 10m,
                    Unit = MeasurementUnit.Centimetres,
                    MeasurementFields = new Dictionary<string, decimal?>()
                }
            ]
        });
        Assert.Null(error);

        var recent = await customers.RecentAsync(Biz, 2);

        Assert.Equal(2, recent.Count);
        Assert.Equal(withOrder, recent[0].Id);
        Assert.DoesNotContain(recent, c => c.Id == older); // filled by newest added (Esi)

        // Orders list uses the same customer rule (phone variant here).
        var found = await orders.SearchAsync(Biz, search: "0242", status: null);
        Assert.Contains(found, o => o.Id == order!.Id);
    }

    [Fact]
    public async Task Update_RefreshesSearchKeys()
    {
        await using var provider = await BuildAsync();
        var customers = provider.GetRequiredService<ICustomerLocalService>();
        var id = await AddAsync(customers, "Old Name", "0241111111");

        await customers.UpdateAsync(Biz, id, new UpdateCustomerRequest("Adjoa New", "0559998888", null, null));

        Assert.Single(await customers.SearchAsync(Biz, "adj"));
        Assert.Single(await customers.SearchAsync(Biz, "+233 55"));
        Assert.Empty(await customers.SearchAsync(Biz, "old"));
    }

    [Fact]
    public async Task Debouncer_RunsOnlyLatest_AndCancelsStale()
    {
        var debouncer = new SearchDebouncer(TimeSpan.FromMilliseconds(50));
        var ran = new List<string>();
        CancellationToken firstToken = default;

        var first = debouncer.Run(async ct =>
        {
            firstToken = ct;
            ran.Add("first");
            await Task.Delay(Timeout.Infinite, ct);
        });
        await Task.Delay(120); // first starts and is now "in flight"
        var second = debouncer.Run(ct => { ran.Add("second"); return Task.CompletedTask; });
        var third = debouncer.Run(ct => { ran.Add("third"); return Task.CompletedTask; }); // supersedes second during delay

        await Task.WhenAll(first, second, third);

        Assert.Equal(["first", "third"], ran);
        Assert.True(firstToken.IsCancellationRequested);
    }

    private static async Task<string> AddAsync(ICustomerLocalService customers, string name, string phone)
    {
        var (customer, error) = await customers.SaveAsync(Biz, new CreateCustomerRequest(name, phone, null, null));
        Assert.Null(error);
        return customer!.Id;
    }

    private static async Task<ServiceProvider> BuildAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"needlebase-search-{Guid.NewGuid():N}.db3");
        var services = new ServiceCollection();
        services.AddDbContext<LocalDbContext>(o => o.UseSqlite($"Data Source={path};Pooling=False"));
        services.AddSingleton<ICustomerLocalService, CustomerLocalService>();
        services.AddSingleton<IMeasurementLocalService, MeasurementLocalService>();
        services.AddSingleton<IOrderLocalService, OrderLocalService>();
        var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await LocalSchemaMigrator.MigrateAsync(scope.ServiceProvider.GetRequiredService<LocalDbContext>());
        return provider;
    }

    [Theory]
    [InlineData("ama")]           // accent folding
    [InlineData("oko")]           // Ɔ / ɔ fold to o
    [InlineData("ansah")]         // hyphenated surname
    [InlineData("guess")]         // after an apostrophe
    public async Task Search_FoldsAccents_AndSplitsHyphensAndApostrophes(string term)
    {
        await using var provider = await BuildAsync();
        var customers = provider.GetRequiredService<ICustomerLocalService>();
        var id = await AddAsync(customers, "Ámá Ɔkɔ Owusu-Ansah N'Guessan", "0245550123");
        await AddAsync(customers, "Kwame Boateng", "0201112222");

        var results = await customers.SearchAsync(Biz, term);

        Assert.Equal(id, Assert.Single(results).Id);
    }

    [Theory]
    [InlineData("7911", true)]     // middle of a non-Ghana number
    [InlineData("123456", true)]
    [InlineData("911", false)]     // under 4 digits: prefix rules only
    public async Task Search_ForeignNumber_MatchesDigitsAnywhere(string term, bool found)
    {
        await using var provider = await BuildAsync();
        var customers = provider.GetRequiredService<ICustomerLocalService>();
        var id = await AddAsync(customers, "London Client", "+44 7911 123456");

        var results = await customers.SearchAsync(Biz, term);

        Assert.Equal(found, results.Any(r => r.Id == id));
    }
}
