using Microsoft.Extensions.Options;
using SupportDesk.Application.Configuration;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.Exceptions;

namespace SupportDesk.Application.Features.Tickets.Triage;

public sealed class TicketTriageService(IOptions<SlaOptions> slaOptions) : ITicketTriageService
{
    private readonly SlaOptions _options = slaOptions.Value;

    public TriageDecision EvaluateTriage(
        TicketPriority? requestedPriority,
        Customer customer,
        Category category,
        IReadOnlyList<Agent> activeAgents,
        IReadOnlyDictionary<int, int> openTicketCounts,
        DateTime nowUtc)
    {
        // BR-1: Priority determination
        var (priority, priorityReason) = DeterminePriority(requestedPriority, category);

        // BR-2, BR-3, BR-6: SLA window & due date calculation
        var (slaWindowHours, dueAtUtc, slaReason) = CalculateSla(priority, customer.Tier, nowUtc);

        // BR-4, BR-5: Agent assignment
        var (assignedAgent, assignmentReason) = SelectAgent(category, activeAgents, openTicketCounts);

        return new TriageDecision(
            priority,
            priorityReason,
            slaWindowHours,
            dueAtUtc,
            slaReason,
            assignedAgent,
            assignmentReason);
    }

    public EscalationDecision EvaluateEscalation(
        Ticket currentTicket,
        Customer customer,
        Category category,
        IReadOnlyList<Agent> activeAgents,
        IReadOnlyDictionary<int, int> openTicketCounts,
        DateTime nowUtc)
    {
        // BR-7: Escalation checks & priority increase
        if (currentTicket.Priority == TicketPriority.Critical)
        {
            throw new BusinessRuleViolationException(
                $"Ticket {currentTicket.Reference} is already at Critical priority and cannot be escalated.");
        }

        if (currentTicket.Status is TicketStatus.Resolved or TicketStatus.Closed)
        {
            throw new BusinessRuleViolationException(
                $"Ticket {currentTicket.Reference} is {currentTicket.Status} and cannot be escalated.");
        }

        var newPriority = currentTicket.Priority switch
        {
            TicketPriority.Low => TicketPriority.Medium,
            TicketPriority.Medium => TicketPriority.High,
            TicketPriority.High => TicketPriority.Critical,
            _ => throw new ArgumentOutOfRangeException(nameof(currentTicket.Priority))
        };

        // Recompute due date from moment of escalation per BR-2 and BR-3
        var (slaWindowHours, newDueAtUtc, slaReason) = CalculateSla(newPriority, customer.Tier, nowUtc);
        var escalationSlaReason = $"Escalated to {newPriority}: {slaReason} Calculated from escalation time.";

        // Re-evaluate owner: keep current agent if still eligible, else reassign per BR-5
        var (assignedAgent, assignmentReason) = ReevaluateAgentForEscalation(
            currentTicket,
            category,
            activeAgents,
            openTicketCounts);

        return new EscalationDecision(
            newPriority,
            slaWindowHours,
            newDueAtUtc,
            escalationSlaReason,
            assignedAgent,
            assignmentReason);
    }

    private static (TicketPriority Priority, string Reason) DeterminePriority(
        TicketPriority? requestedPriority,
        Category category)
    {
        if (category.ForcesCriticalPriority)
        {
            return (TicketPriority.Critical, $"Category '{category.Name}' forces Critical priority.");
        }

        if (requestedPriority.HasValue)
        {
            return (requestedPriority.Value, $"Requested priority '{requestedPriority.Value}' applied.");
        }

        return (TicketPriority.Medium, "Default priority 'Medium' applied.");
    }

    private (double WindowHours, DateTime DueAtUtc, string Reason) CalculateSla(
        TicketPriority priority,
        CustomerTier tier,
        DateTime startTimeUtc)
    {
        var baseHours = _options.GetBaseWindowHours(priority);
        double windowHours;
        string reason;

        if (tier == CustomerTier.Premium)
        {
            var reduced = baseHours * _options.PremiumCustomerMultiplier;
            windowHours = Math.Max(reduced, _options.MinimumWindowHours);

            reason = windowHours == _options.MinimumWindowHours && reduced < _options.MinimumWindowHours
                ? $"{priority} base SLA of {baseHours}h reduced for Premium customer to minimum floor of {windowHours}h."
                : $"{priority} base SLA of {baseHours}h reduced by {_options.PremiumCustomerMultiplier:P0} for Premium customer to {windowHours}h.";
        }
        else
        {
            windowHours = baseHours;
            reason = $"{priority} base SLA of {baseHours}h applied.";
        }

        var dueAtUtc = startTimeUtc.AddHours(windowHours);
        return (windowHours, dueAtUtc, reason);
    }

    private static (Agent? Agent, string Reason) SelectAgent(
        Category category,
        IReadOnlyList<Agent> activeAgents,
        IReadOnlyDictionary<int, int> openTicketCounts)
    {
        var eligible = activeAgents
            .Where(a => a.IsActive)
            .Where(a => !category.RequiresSpecialist || a.Specializations.Any(s => s.CategoryId == category.Id))
            .Where(a => openTicketCounts.GetValueOrDefault(a.Id) < a.MaxOpenTickets)
            .OrderBy(a => openTicketCounts.GetValueOrDefault(a.Id))
            .ThenBy(a => a.Id) // Deterministic tie-breaker
            .ToList();

        var selected = eligible.FirstOrDefault();
        if (selected is not null)
        {
            var openCount = openTicketCounts.GetValueOrDefault(selected.Id);
            return (selected, $"Assigned to {selected.FullName} ({openCount} open tickets).");
        }

        return (null, BuildUnassignedReason(category, activeAgents, openTicketCounts));
    }

    private static (Agent? Agent, string Reason) ReevaluateAgentForEscalation(
        Ticket currentTicket,
        Category category,
        IReadOnlyList<Agent> activeAgents,
        IReadOnlyDictionary<int, int> openTicketCounts)
    {
        if (currentTicket.AssignedAgentId.HasValue)
        {
            var currentAgent = activeAgents.FirstOrDefault(a => a.Id == currentTicket.AssignedAgentId.Value);
            if (currentAgent is not null && currentAgent.IsActive)
            {
                var isSpecialistQualified = !category.RequiresSpecialist ||
                                            currentAgent.Specializations.Any(s => s.CategoryId == category.Id);

                // Since this ticket is already one of the current agent's open tickets,
                // keeping them leaves their count <= MaxOpenTickets.
                var currentOpenCount = openTicketCounts.GetValueOrDefault(currentAgent.Id);
                var withinLimit = currentOpenCount <= currentAgent.MaxOpenTickets;

                if (isSpecialistQualified && withinLimit)
                {
                    return (currentAgent, $"Retained current agent {currentAgent.FullName} ({currentOpenCount} open tickets).");
                }
            }
        }

        // Current agent is not eligible or ticket was unassigned: select new agent per BR-5
        var (newAgent, reason) = SelectAgent(category, activeAgents, openTicketCounts);
        if (newAgent is not null)
        {
            return (newAgent, $"Reassigned to {newAgent.FullName} ({openTicketCounts.GetValueOrDefault(newAgent.Id)} open tickets).");
        }

        return (null, reason);
    }

    private static string BuildUnassignedReason(
        Category category,
        IReadOnlyList<Agent> activeAgents,
        IReadOnlyDictionary<int, int> openTicketCounts)
    {
        if (activeAgents.Count == 0)
        {
            return "No eligible agent: No active agents available.";
        }

        if (category.RequiresSpecialist)
        {
            var specialists = activeAgents
                .Where(a => a.Specializations.Any(s => s.CategoryId == category.Id))
                .ToList();

            if (specialists.Count == 0)
            {
                return $"No eligible agent: Category '{category.Name}' requires a specialist, but no active agent has this specialization.";
            }

            if (specialists.All(a => openTicketCounts.GetValueOrDefault(a.Id) >= a.MaxOpenTickets))
            {
                return $"No eligible agent: All specialists for '{category.Name}' are at maximum ticket capacity.";
            }
        }

        if (activeAgents.All(a => openTicketCounts.GetValueOrDefault(a.Id) >= a.MaxOpenTickets))
        {
            return "No eligible agent: All eligible agents are at maximum ticket capacity.";
        }

        return "No eligible agent available.";
    }
}
