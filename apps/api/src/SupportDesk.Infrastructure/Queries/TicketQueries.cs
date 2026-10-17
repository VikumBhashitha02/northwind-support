using Microsoft.EntityFrameworkCore;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Common;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Infrastructure.Data;

namespace SupportDesk.Infrastructure.Queries;

/// <summary>
/// Ticket reads. Projected in the database and never tracked. Aggregates hold only each
/// other's ids, so the customer, category and agent labels are joined in here, on the read side.
/// </summary>
public sealed class TicketQueries(SupportDbContext db, IClock clock) : ITicketQueries
{
    public async Task<PagedResult<TicketListItemDto>> GetPagedAsync(TicketQuery query, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var filteredTickets = ApplyFilters(TicketsWithLabels(), query, now);

        var totalCount = await filteredTickets.CountAsync(ct);

        var items = await ApplySort(filteredTickets, query)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(x => new TicketListItemDto
            {
                Id = x.Ticket.Id,
                Reference = x.Ticket.Reference,
                Title = x.Ticket.Title,
                Status = x.Ticket.Status,
                Priority = x.Ticket.Priority,
                Customer = new CustomerSummaryDto(x.Customer.Id, x.Customer.Name, x.Customer.Tier),
                Category = new CategorySummaryDto(x.Category.Id, x.Category.Name),
                AssignedAgent = x.Agent == null ? null : new AgentSummaryDto(x.Agent.Id, x.Agent.FullName),
                CreatedAtUtc = x.Ticket.CreatedAtUtc,
                UpdatedAtUtc = x.Ticket.UpdatedAtUtc,
                DueAtUtc = x.Ticket.DueAtUtc,
                ResolvedAtUtc = x.Ticket.ResolvedAtUtc,
                SlaStatus = SlaEvaluator.Evaluate(x.Ticket.DueAtUtc, x.Ticket.ResolvedAtUtc, now, x.Ticket.CreatedAtUtc)
            })
            .ToListAsync(ct);

        return new PagedResult<TicketListItemDto>(items, query.Page, query.PageSize, totalCount);
    }

    private IQueryable<TicketWithLabels> ApplyFilters(
        IQueryable<TicketWithLabels> source,
        TicketQuery query,
        DateTime now)
    {
        if (query.Status.HasValue)
        {
            source = source.Where(x => x.Ticket.Status == query.Status.Value);
        }

        if (query.Priority.HasValue)
        {
            source = source.Where(x => x.Ticket.Priority == query.Priority.Value);
        }

        if (query.CategoryId.HasValue)
        {
            source = source.Where(x => x.Ticket.CategoryId == query.CategoryId.Value);
        }

        if (query.CustomerId.HasValue)
        {
            source = source.Where(x => x.Ticket.CustomerId == query.CustomerId.Value);
        }

        if (query.AssignedAgentId.HasValue)
        {
            source = source.Where(x => x.Ticket.AssignedAgentId == query.AssignedAgentId.Value);
        }

        if (query.UnassignedOnly)
        {
            source = source.Where(x => x.Ticket.AssignedAgentId == null);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            source = source.Where(x =>
                x.Ticket.Title.Contains(term) ||
                x.Ticket.Reference.Contains(term) ||
                x.Customer.Name.Contains(term));
        }

        if (query.SlaStatus.HasValue)
        {
            source = ApplySlaFilter(source, query.SlaStatus.Value, now);
        }

        return source;
    }

    private IQueryable<TicketWithLabels> ApplySlaFilter(
        IQueryable<TicketWithLabels> source,
        SlaStatus slaStatus,
        DateTime now)
    {
        var isSqlServer = db.Database.IsSqlServer();

        return slaStatus switch
        {
            SlaStatus.NotApplicable =>
                source.Where(x => x.Ticket.DueAtUtc == null),

            SlaStatus.Met =>
                source.Where(x =>
                    x.Ticket.DueAtUtc != null &&
                    x.Ticket.ResolvedAtUtc != null &&
                    x.Ticket.ResolvedAtUtc <= x.Ticket.DueAtUtc),

            SlaStatus.Breached =>
                source.Where(x =>
                    x.Ticket.DueAtUtc != null && (
                        (x.Ticket.ResolvedAtUtc != null && x.Ticket.ResolvedAtUtc > x.Ticket.DueAtUtc) ||
                        (x.Ticket.ResolvedAtUtc == null && now > x.Ticket.DueAtUtc))),

            SlaStatus.AtRisk when isSqlServer =>
                source.Where(x =>
                    x.Ticket.DueAtUtc != null &&
                    x.Ticket.ResolvedAtUtc == null &&
                    now <= x.Ticket.DueAtUtc &&
                    EF.Functions.DateDiffSecond(now, x.Ticket.DueAtUtc!.Value) * 4 <=
                    EF.Functions.DateDiffSecond(x.Ticket.CreatedAtUtc, x.Ticket.DueAtUtc!.Value)),

            SlaStatus.AtRisk =>
                // SQLite provider translation for testing: unresolved, before due, and remaining <= 25% of total
                source.Where(x =>
                    x.Ticket.DueAtUtc != null &&
                    x.Ticket.ResolvedAtUtc == null &&
                    now <= x.Ticket.DueAtUtc &&
                    (x.Ticket.DueAtUtc.Value - now) <= (x.Ticket.DueAtUtc.Value - x.Ticket.CreatedAtUtc) * 0.25),

            SlaStatus.WithinSla when isSqlServer =>
                source.Where(x =>
                    x.Ticket.DueAtUtc != null &&
                    x.Ticket.ResolvedAtUtc == null &&
                    now <= x.Ticket.DueAtUtc &&
                    EF.Functions.DateDiffSecond(now, x.Ticket.DueAtUtc!.Value) * 4 >
                    EF.Functions.DateDiffSecond(x.Ticket.CreatedAtUtc, x.Ticket.DueAtUtc!.Value)),

            SlaStatus.WithinSla =>
                // SQLite provider translation for testing: unresolved, before due, and remaining > 25% of total
                source.Where(x =>
                    x.Ticket.DueAtUtc != null &&
                    x.Ticket.ResolvedAtUtc == null &&
                    now <= x.Ticket.DueAtUtc &&
                    (x.Ticket.DueAtUtc.Value - now) > (x.Ticket.DueAtUtc.Value - x.Ticket.CreatedAtUtc) * 0.25),

            _ => source
        };
    }

    public Task<TicketDetailDto?> GetDetailAsync(int id, CancellationToken ct)
    {
        var now = clock.UtcNow;

        return TicketsWithLabels()
            .Where(x => x.Ticket.Id == id)
            .Select(x => new TicketDetailDto
            {
                Id = x.Ticket.Id,
                Reference = x.Ticket.Reference,
                Title = x.Ticket.Title,
                Description = x.Ticket.Description,
                Status = x.Ticket.Status,
                Priority = x.Ticket.Priority,
                Customer = new CustomerContactDto(
                    x.Customer.Id, x.Customer.Name, x.Customer.Email, x.Customer.Phone, x.Customer.Tier),
                Category = new CategoryDto(x.Category.Id, x.Category.Name, x.Category.RequiresSpecialist),
                AssignedAgent = x.Agent == null ? null : new AgentSummaryDto(x.Agent.Id, x.Agent.FullName),
                CreatedAtUtc = x.Ticket.CreatedAtUtc,
                UpdatedAtUtc = x.Ticket.UpdatedAtUtc,
                DueAtUtc = x.Ticket.DueAtUtc,
                ResolvedAtUtc = x.Ticket.ResolvedAtUtc,
                SlaStatus = SlaEvaluator.Evaluate(x.Ticket.DueAtUtc, x.Ticket.ResolvedAtUtc, now, x.Ticket.CreatedAtUtc)
            })
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<TicketListItemDto>> GetForCustomerAsync(int customerId, CancellationToken ct)
    {
        var now = clock.UtcNow;

        return await TicketsWithLabels()
            .Where(x => x.Ticket.CustomerId == customerId)
            .OrderByDescending(x => x.Ticket.CreatedAtUtc)
            .ThenByDescending(x => x.Ticket.Id)
            .Select(x => new TicketListItemDto
            {
                Id = x.Ticket.Id,
                Reference = x.Ticket.Reference,
                Title = x.Ticket.Title,
                Status = x.Ticket.Status,
                Priority = x.Ticket.Priority,
                Customer = new CustomerSummaryDto(x.Customer.Id, x.Customer.Name, x.Customer.Tier),
                Category = new CategorySummaryDto(x.Category.Id, x.Category.Name),
                AssignedAgent = x.Agent == null ? null : new AgentSummaryDto(x.Agent.Id, x.Agent.FullName),
                CreatedAtUtc = x.Ticket.CreatedAtUtc,
                UpdatedAtUtc = x.Ticket.UpdatedAtUtc,
                DueAtUtc = x.Ticket.DueAtUtc,
                ResolvedAtUtc = x.Ticket.ResolvedAtUtc,
                SlaStatus = SlaEvaluator.Evaluate(x.Ticket.DueAtUtc, x.Ticket.ResolvedAtUtc, now, x.Ticket.CreatedAtUtc)
            })
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<TicketEscalationDto>> GetEscalationsAsync(int ticketId, CancellationToken ct)
    {
        return await db.Set<TicketEscalation>()
            .AsNoTracking()
            .Where(x => x.TicketId == ticketId)
            .OrderByDescending(x => x.EscalatedAtUtc)
            .ThenByDescending(x => x.Id)
            .Select(x => new TicketEscalationDto
            {
                Id = x.Id,
                TicketId = x.TicketId,
                FromPriority = x.FromPriority,
                ToPriority = x.ToPriority,
                FromAgentId = x.FromAgentId,
                ToAgentId = x.ToAgentId,
                FromDueAtUtc = x.FromDueAtUtc,
                ToDueAtUtc = x.ToDueAtUtc,
                Reason = x.Reason,
                EscalatedBy = x.EscalatedBy,
                EscalatedAtUtc = x.EscalatedAtUtc
            })
            .ToListAsync(ct);
    }

    /// <summary>
    /// Every ticket with the rows that label it. Filter and sort this, then project.
    /// </summary>
    private IQueryable<TicketWithLabels> TicketsWithLabels() =>
        from ticket in db.Set<Ticket>().AsNoTracking()
        join customer in db.Set<Customer>() on ticket.CustomerId equals customer.Id
        join category in db.Set<Category>() on ticket.CategoryId equals category.Id
        from agent in db.Set<Agent>().Where(a => a.Id == ticket.AssignedAgentId).DefaultIfEmpty()
        select new TicketWithLabels { Ticket = ticket, Customer = customer, Category = category, Agent = agent };

    private static IQueryable<TicketWithLabels> ApplySort(IQueryable<TicketWithLabels> source, TicketQuery query)
    {
        var descending = !string.Equals(query.SortDirection, "asc", StringComparison.OrdinalIgnoreCase);

        var sorted = query.SortBy?.ToLowerInvariant() switch
        {
            "updatedatutc" => descending
                ? source.OrderByDescending(x => x.Ticket.UpdatedAtUtc)
                : source.OrderBy(x => x.Ticket.UpdatedAtUtc),
            "dueatutc" => descending
                ? source.OrderByDescending(x => x.Ticket.DueAtUtc)
                : source.OrderBy(x => x.Ticket.DueAtUtc),
            "priority" => descending
                ? source.OrderByDescending(x => x.Ticket.Priority)
                : source.OrderBy(x => x.Ticket.Priority),
            "status" => descending
                ? source.OrderByDescending(x => x.Ticket.Status)
                : source.OrderBy(x => x.Ticket.Status),
            _ => descending
                ? source.OrderByDescending(x => x.Ticket.CreatedAtUtc)
                : source.OrderBy(x => x.Ticket.CreatedAtUtc)
        };

        // Keeps paging stable when the sort column has duplicates.
        return sorted.ThenByDescending(x => x.Ticket.Id);
    }

    private sealed class TicketWithLabels
    {
        public required Ticket Ticket { get; init; }

        public required Customer Customer { get; init; }

        public required Category Category { get; init; }

        public Agent? Agent { get; init; }
    }
}
