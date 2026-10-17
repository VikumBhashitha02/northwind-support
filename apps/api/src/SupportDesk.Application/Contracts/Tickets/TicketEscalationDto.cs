using SupportDesk.Domain.Aggregates.Tickets;

namespace SupportDesk.Application.Contracts.Tickets;

/// <summary>
/// Details of an escalation applied to a ticket.
/// </summary>
public sealed record TicketEscalationDto
{
    public required int Id { get; init; }

    public required int TicketId { get; init; }

    public required TicketPriority FromPriority { get; init; }

    public required TicketPriority ToPriority { get; init; }

    public int? FromAgentId { get; init; }

    public int? ToAgentId { get; init; }

    public DateTime? FromDueAtUtc { get; init; }

    public required DateTime ToDueAtUtc { get; init; }

    public required string Reason { get; init; }

    public required string EscalatedBy { get; init; }

    public required DateTime EscalatedAtUtc { get; init; }
}
