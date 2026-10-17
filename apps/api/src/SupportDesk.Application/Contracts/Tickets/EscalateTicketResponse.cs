namespace SupportDesk.Application.Contracts.Tickets;

/// <summary>
/// The result of escalating a ticket, containing the updated ticket and the recorded escalation.
/// </summary>
public sealed record EscalateTicketResponse(
    TicketDetailDto Ticket,
    TicketEscalationDto Escalation);
