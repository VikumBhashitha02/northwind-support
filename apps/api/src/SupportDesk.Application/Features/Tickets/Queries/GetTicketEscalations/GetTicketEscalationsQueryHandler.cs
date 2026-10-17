using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Exceptions;
using SupportDesk.Domain.Repositories;

namespace SupportDesk.Application.Features.Tickets.Queries.GetTicketEscalations;

/// <summary>
/// Retrieves escalation history for a ticket, newest first.
/// </summary>
public sealed class GetTicketEscalationsQueryHandler(
    ITicketRepository tickets,
    ITicketQueries ticketQueries)
{
    /// <exception cref="NotFoundException">The ticket does not exist.</exception>
    public async Task<IReadOnlyList<TicketEscalationDto>> HandleAsync(int id, CancellationToken ct)
    {
        if (!await tickets.ExistsAsync(id, ct))
        {
            throw new NotFoundException("Ticket", id);
        }

        return await ticketQueries.GetEscalationsAsync(id, ct);
    }
}
