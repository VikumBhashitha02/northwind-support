using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;

namespace SupportDesk.Application.Features.Tickets.Triage;

public interface ITicketTriageService
{
    TriageDecision EvaluateTriage(
        TicketPriority? requestedPriority,
        Customer customer,
        Category category,
        IReadOnlyList<Agent> activeAgents,
        IReadOnlyDictionary<int, int> openTicketCounts,
        DateTime nowUtc);

    EscalationDecision EvaluateEscalation(
        Ticket currentTicket,
        Customer customer,
        Category category,
        IReadOnlyList<Agent> activeAgents,
        IReadOnlyDictionary<int, int> openTicketCounts,
        DateTime nowUtc);
}

public sealed record TriageDecision(
    TicketPriority Priority,
    string PriorityReason,
    double SlaWindowHours,
    DateTime DueAtUtc,
    string SlaReason,
    Agent? AssignedAgent,
    string AssignmentReason);

public sealed record EscalationDecision(
    TicketPriority NewPriority,
    double SlaWindowHours,
    DateTime NewDueAtUtc,
    string SlaReason,
    Agent? AssignedAgent,
    string AssignmentReason);
