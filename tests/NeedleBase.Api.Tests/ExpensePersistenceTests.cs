using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NeedleBase.Application.Audit;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Auth;
using NeedleBase.Application.Contracts.Customers;
using NeedleBase.Application.Contracts.Orders;
using NeedleBase.Application.Sync;
using NeedleBase.Domain.Entities;
using NeedleBase.Domain.Enums;
using NeedleBase.Infrastructure.Persistence;
using Npgsql;

namespace NeedleBase.Api.Tests;

/// <summary>Expenses table (plans step 7b-2, chunk 2): sync stamps, audit rows, order FK, tenant filter and shop purge.</summary>
[Collection(SyncEpochCollection.Name)]
public class ExpensePersistenceTests : IClassFixture<NeedleBaseApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly DateTimeOffset Now = new(2026, 11, 27, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 27);

    private readonly NeedleBaseApiFactory _factory;
    private readonly HttpClient _client;

    public ExpensePersistenceTests(NeedleBaseApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Insert_StampsSync_AndRecordsAudit_VoidAuditsAndRestamps()
    {
        var (_, businessId) = await RegisterAsync("Audit");
        var id = IdGenerator.NewId();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Expenses.Add(Expense.Create(id, businessId, 120m, ExpenseCategory.Materials, Today, "Makola lining", null, "U1", Today, Now));
            await db.SaveChangesAsync();
        }

        var (tx1, modified1) = await StampsAsync(id);
        Assert.True(tx1 > 0);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var row = await db.Expenses.IgnoreQueryFilters().SingleAsync(x => x.Id == id);
            Assert.Equal(Today, row.SpentOn);
            Assert.Equal(120m, row.Amount);
            row.Void("Entered twice by mistake", "U1", Now.AddMinutes(1));
            await db.SaveChangesAsync();
        }

        var (tx2, modified2) = await StampsAsync(id);
        Assert.True(tx2 > tx1);
        Assert.True(modified2 >= modified1);

        var events = await EventsAsync(businessId);
        var recorded = Assert.Single(events, e => e.Action == AuditActions.ExpenseRecorded);
        Assert.Equal(id, recorded.EntityId);
        Assert.Equal("expense", recorded.EntityType);
        Assert.Contains("GH₵", recorded.Summary);
        Assert.Contains("Fabric and materials", recorded.Summary);
        Assert.DoesNotContain("Makola", recorded.ChangesJson ?? string.Empty);   // notes are masked
        Assert.DoesNotContain("RecordedByUserId", recorded.ChangesJson ?? string.Empty);
        var voided = Assert.Single(events, e => e.Action == AuditActions.ExpenseVoided);
        Assert.Contains("Entered twice", voided.Summary);
        Assert.Equal("Expense voided", AuditActions.Display(AuditActions.ExpenseVoided));
        Assert.Contains(AuditActions.Groups, g => g.Value == "expense." && g.Label == "Expenses");
    }

    [Fact]
    public async Task LinkedOrder_CannotBeDeleted_WhileExpensesPointAtIt_AndOtherShopsCantSeeIt()
    {
        var (token, businessId) = await RegisterAsync("Fk");
        var (_, otherBusinessId) = await RegisterAsync("Other");
        var orderId = await CreateOrderAsync(token);
        var id = IdGenerator.NewId();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Expenses.Add(Expense.Create(id, businessId, 45.5m, ExpenseCategory.Transport, Today, null, orderId, null, Today, Now));
            await db.SaveChangesAsync();

            var ex = await Assert.ThrowsAnyAsync<PostgresException>(() =>
                db.Database.ExecuteSqlRawAsync("DELETE FROM \"Orders\" WHERE \"Id\" = {0}", orderId));
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, ex.SqlState);

            // Fail-closed tenant filter: no current business → nothing visible.
            db.CurrentBusinessId = null;
            Assert.False(await db.Expenses.AnyAsync(x => x.Id == id));
            // Cross-tenant: another shop's context never returns this row.
            db.CurrentBusinessId = otherBusinessId;
            Assert.False(await db.Expenses.AnyAsync(x => x.Id == id));
            Assert.True(await db.Expenses.IgnoreQueryFilters()
                .AnyAsync(x => x.Id == id && x.OrderId == orderId && x.BusinessId == businessId));
        }
    }

    [Fact]
    public async Task OwnerShopDelete_PurgesExpenses()
    {
        var (token, businessId) = await RegisterAsync("Purge");
        var orderId = await CreateOrderAsync(token);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Expenses.Add(Expense.Create(IdGenerator.NewId(), businessId, 300m, ExpenseCategory.RentAndBills, Today, null, null, null, Today, Now));
            db.Expenses.Add(Expense.Create(IdGenerator.NewId(), businessId, 80m, ExpenseCategory.Materials, Today, null, orderId, null, Today, Now));
            await db.SaveChangesAsync();
        }

        using var deleted = await SendAsync(HttpMethod.Post, "/api/me/delete-account", token,
            new DeleteAccountRequest { Password = "Passw0rd!", Confirmation = DeleteAccountConfirmations.OwnerShop });
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        using var check = _factory.Services.CreateScope();
        var after = check.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await after.Expenses.IgnoreQueryFilters().AnyAsync(x => x.BusinessId == businessId));
    }

    private async Task<(long Tx, DateTimeOffset Modified)> StampsAsync(string id)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = await db.Expenses.IgnoreQueryFilters().AsNoTracking().Where(x => x.Id == id)
            .Select(x => new { Tx = EF.Property<long>(x, SyncTime.TxColumn), Modified = EF.Property<DateTimeOffset>(x, SyncTime.ModifiedColumn) })
            .SingleAsync();
        return (row.Tx, row.Modified);
    }

    private async Task<List<AuditEvent>> EventsAsync(string businessId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.AuditEvents.IgnoreQueryFilters().AsNoTracking().Where(e => e.BusinessId == businessId).ToListAsync();
    }

    private async Task<string> CreateOrderAsync(string token)
    {
        using var customerResponse = await SendAsync(HttpMethod.Post, "/api/customers", token,
            new CreateCustomerRequest("Efua Mensah", "+233245550123", null, null));
        Assert.Equal(HttpStatusCode.Created, customerResponse.StatusCode);
        var customer = (await customerResponse.Content.ReadFromJsonAsync<CustomerDetailDto>(JsonOptions))!;
        using var response = await SendAsync(HttpMethod.Post, "/api/orders", token, new CreateOrderRequest
        {
            CustomerId = customer.Id,
            TotalAmount = 800m,
            Lines =
            [
                new CreateOrderLineRequest
                {
                    GarmentTemplate = GarmentTemplate.Dress, Quantity = 1, UnitPrice = 800m, Unit = MeasurementUnit.Centimetres,
                    MeasurementFields = new Dictionary<string, decimal?>()
                }
            ]
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<OrderDetailDto>(JsonOptions))!.Id;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        return await _client.SendAsync(request);
    }

    private async Task<(string AccessToken, string BusinessId)> RegisterAsync(string label)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var email = $"expense{label.ToLowerInvariant()}{suffix}@ama-stitches.test";
        var register = new RegisterBusinessRequest(email, "Passw0rd!", "Ama", "Owusu", $"Expense {label} {suffix}", "+233200000000");
        using var response = await _client.PostAsJsonAsync("/api/auth/register-business", register, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = (await response.Content.ReadFromJsonAsync<AuthPayload>(JsonOptions))!;
        return (payload.AccessToken!, payload.User!.BusinessId!);
    }

    private sealed record AuthPayload(string? AccessToken, string? RefreshToken, AuthUserDto? User);
}
