using SupportDesk.Domain.Aggregates.Tickets;

namespace SupportDesk.Domain.Repositories;

/// <summary>
/// Loads and stores <see cref="Ticket"/> aggregates. Changes are committed through
/// <see cref="IUnitOfWork"/>.
/// </summary>
public interface ITicketRepository
{
    /// <summary>Loads a tracked ticket for modification, or null when it does not exist.</summary>
    Task<Ticket?> GetByIdAsync(int id, CancellationToken ct);

    Task<bool> ExistsAsync(int id, CancellationToken ct);

    /// <summary>The next unused ticket reference, e.g. TCK-0041.</summary>
    Task<string> NextReferenceAsync(CancellationToken ct);

    Task AddAsync(Ticket ticket, CancellationToken ct);

    Task AddEscalationAsync(TicketEscalation escalation, CancellationToken ct);

    /// <summary>Counts open (not Resolved or Closed) tickets grouped by assigned agent id.</summary>
    Task<Dictionary<int, int>> GetOpenTicketCountsByAgentAsync(CancellationToken ct);
}
