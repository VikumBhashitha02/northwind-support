namespace SupportDesk.Application.Contracts.Tickets;

public sealed record EscalateTicketRequest
{
    public string Reason { get; init; } = string.Empty;

    public string? EscalatedBy { get; init; }
}
