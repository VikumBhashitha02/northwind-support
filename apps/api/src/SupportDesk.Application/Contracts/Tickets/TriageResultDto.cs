using SupportDesk.Domain.Aggregates.Tickets;

namespace SupportDesk.Application.Contracts.Tickets;

/// <summary>
/// Details of the automatic triage decisions made when a ticket was created.
/// </summary>
public sealed record TriageResultDto(
    TicketPriority Priority,
    string PriorityReason,
    double SlaWindowHours,
    DateTime DueAtUtc,
    string SlaReason,
    AgentSummaryDto? AssignedAgent,
    string AssignmentReason,
    SlaStatus SlaStatus);
