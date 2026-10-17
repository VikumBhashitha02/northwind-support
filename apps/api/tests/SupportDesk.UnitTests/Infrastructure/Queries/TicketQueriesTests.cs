using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Infrastructure.Data;
using SupportDesk.Infrastructure.Queries;
using SupportDesk.UnitTests.TestDoubles;
using Xunit;

namespace SupportDesk.UnitTests.Infrastructure.Queries;

public sealed class TicketQueriesTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SupportDbContext _db;
    private readonly FixedClock _clock;
    private readonly TicketQueries _queries;

    public TicketQueriesTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<SupportDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new SupportDbContext(options);
        _db.Database.EnsureCreated();

        _clock = new FixedClock();
        _queries = new TicketQueries(_db, _clock);

        SeedData();
    }

    private void SeedData()
    {
        var now = FixedClock.DefaultNow;

        var customer1 = new Customer("Contoso Ltd", "contact@contoso.com", "555-0100", CustomerTier.Premium, now);
        var customer2 = new Customer("Fabrikam Inc", "info@fabrikam.com", "555-0200", CustomerTier.Standard, now);
        _db.Set<Customer>().AddRange(customer1, customer2);

        var category1 = new Category("General", requiresSpecialist: false, forcesCriticalPriority: false);
        var category2 = new Category("Technical", requiresSpecialist: true, forcesCriticalPriority: false);
        _db.Set<Category>().AddRange(category1, category2);

        var agent1 = new Agent("Alex Turner", "alex@northwind.local", 10, now);
        var agent2 = new Agent("Ben Osei", "ben@northwind.local", 8, now);
        _db.Set<Agent>().AddRange(agent1, agent2);

        _db.SaveChanges();

        // 5 seeded tickets with diverse priorities, statuses, customers, and assignments:
        // 1: Customer 1, Category 1, High, Open, Agent 1
        var t1 = Ticket.Raise("TCK-0001", "Printer broken", "Description", customer1.Id, category1.Id, TicketPriority.High, now);
        t1.AssignTo(agent1.Id, now);
        t1.ChangeStatus(TicketStatus.Open, now);

        // 2: Customer 1, Category 2, Critical, InProgress, Agent 1
        var t2 = Ticket.Raise("TCK-0002", "Network timeout", "Description", customer1.Id, category2.Id, TicketPriority.Critical, now);
        t2.AssignTo(agent1.Id, now);
        t2.ChangeStatus(TicketStatus.InProgress, now);

        // 3: Customer 2, Category 1, High, Open, Agent 2
        var t3 = Ticket.Raise("TCK-0003", "Email issue", "Description", customer2.Id, category1.Id, TicketPriority.High, now);
        t3.AssignTo(agent2.Id, now);
        t3.ChangeStatus(TicketStatus.Open, now);

        // 4: Customer 2, Category 1, Low, New, Unassigned
        var t4 = Ticket.Raise("TCK-0004", "Invoice question", "Description", customer2.Id, category1.Id, TicketPriority.Low, now);

        // 5: Customer 2, Category 2, Critical, New, Unassigned
        var t5 = Ticket.Raise("TCK-0005", "Security alert", "Description", customer2.Id, category2.Id, TicketPriority.Critical, now);

        _db.Set<Ticket>().AddRange(t1, t2, t3, t4, t5);
        _db.SaveChanges();
    }

    [Fact]
    public async Task GetPagedAsync_FiltersByExactPriority()
    {
        var query = new TicketQuery { Priority = TicketPriority.High };

        var result = await _queries.GetPagedAsync(query, CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, item => Assert.Equal(TicketPriority.High, item.Priority));
        Assert.Contains(result.Items, item => item.Reference == "TCK-0001");
        Assert.Contains(result.Items, item => item.Reference == "TCK-0003");
    }

    [Fact]
    public async Task GetPagedAsync_CombinesMultipleFiltersWithAnd()
    {
        var query = new TicketQuery
        {
            Priority = TicketPriority.High,
            CustomerId = 1
        };

        var result = await _queries.GetPagedAsync(query, CancellationToken.None);

        Assert.Equal(1, result.TotalCount);
        var single = Assert.Single(result.Items);
        Assert.Equal("TCK-0001", single.Reference);
        Assert.Equal(TicketPriority.High, single.Priority);
        Assert.Equal(1, single.Customer.Id);
    }

    [Fact]
    public async Task GetPagedAsync_FiltersByCustomerNameSearch_Trimmed()
    {
        var query = new TicketQuery { Search = "  Contoso  " };

        var result = await _queries.GetPagedAsync(query, CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, item => Assert.Equal("Contoso Ltd", item.Customer.Name));
    }

    [Fact]
    public async Task GetPagedAsync_FiltersByReferenceSearch()
    {
        var query = new TicketQuery { Search = "TCK-0004" };

        var result = await _queries.GetPagedAsync(query, CancellationToken.None);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("TCK-0004", result.Items[0].Reference);
    }

    [Fact]
    public async Task GetPagedAsync_FiltersByTitleSearch()
    {
        var query = new TicketQuery { Search = "Security" };

        var result = await _queries.GetPagedAsync(query, CancellationToken.None);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("TCK-0005", result.Items[0].Reference);
    }

    [Fact]
    public async Task GetPagedAsync_FiltersUnassignedOnly()
    {
        var query = new TicketQuery { UnassignedOnly = true };

        var result = await _queries.GetPagedAsync(query, CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, item => Assert.Null(item.AssignedAgent));
        Assert.Contains(result.Items, item => item.Reference == "TCK-0004");
        Assert.Contains(result.Items, item => item.Reference == "TCK-0005");
    }

    [Fact]
    public async Task GetPagedAsync_CalculatesFilteredCountAndAppliesPagination()
    {
        // Customer 2 has 3 tickets: TCK-0003, TCK-0004, TCK-0005
        var page1Query = new TicketQuery
        {
            CustomerId = 2,
            Page = 1,
            PageSize = 2
        };

        var page1 = await _queries.GetPagedAsync(page1Query, CancellationToken.None);

        Assert.Equal(3, page1.TotalCount);
        Assert.Equal(2, page1.TotalPages);
        Assert.Equal(2, page1.Items.Count);

        var page2Query = new TicketQuery
        {
            CustomerId = 2,
            Page = 2,
            PageSize = 2
        };

        var page2 = await _queries.GetPagedAsync(page2Query, CancellationToken.None);

        Assert.Equal(3, page2.TotalCount);
        Assert.Equal(2, page2.TotalPages);
        Assert.Single(page2.Items);
    }

    [Fact]
    public async Task GetPagedAsync_IgnoresBlankOrWhitespaceSearch()
    {
        var query = new TicketQuery { Search = "     " };

        var result = await _queries.GetPagedAsync(query, CancellationToken.None);

        Assert.Equal(5, result.TotalCount);
    }

    [Fact]
    public async Task GetPagedAsync_FiltersByStatusAndCategory()
    {
        var query = new TicketQuery
        {
            Status = TicketStatus.New,
            CategoryId = 2
        };

        var result = await _queries.GetPagedAsync(query, CancellationToken.None);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("TCK-0005", result.Items[0].Reference);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
