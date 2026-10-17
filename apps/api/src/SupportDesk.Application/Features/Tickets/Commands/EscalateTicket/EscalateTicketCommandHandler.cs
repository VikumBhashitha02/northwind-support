using Microsoft.Extensions.Logging;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Exceptions;
using SupportDesk.Application.Features.Tickets.Queries.GetTicket;
using SupportDesk.Application.Features.Tickets.Triage;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.Repositories;

namespace SupportDesk.Application.Features.Tickets.Commands.EscalateTicket;

/// <summary>
/// Escalates a ticket to the next priority level, recalculating SLA due date and re-evaluating assignment.
/// </summary>
public sealed class EscalateTicketCommandHandler(
    ITicketRepository tickets,
    ICustomerRepository customers,
    ICategoryRepository categories,
    IAgentRepository agents,
    ITicketTriageService triageService,
    IUnitOfWork unitOfWork,
    IClock clock,
    GetTicketQueryHandler getTicket,
    ILogger<EscalateTicketCommandHandler> logger)
{
    /// <exception cref="NotFoundException">The ticket, customer, or category does not exist.</exception>
    /// <exception cref="Domain.Exceptions.BusinessRuleViolationException">The ticket is Critical, Resolved or Closed.</exception>
    public async Task<EscalateTicketResponse> HandleAsync(int id, EscalateTicketRequest request, CancellationToken ct)
    {
        var ticket = await tickets.GetByIdAsync(id, ct) ?? throw new NotFoundException("Ticket", id);

        var customer = await customers.GetByIdAsync(ticket.CustomerId, ct)
            ?? throw new NotFoundException("Customer", ticket.CustomerId);

        var category = await categories.GetByIdAsync(ticket.CategoryId, ct)
            ?? throw new NotFoundException("Category", ticket.CategoryId);

        var activeAgents = await agents.GetAllActiveWithSpecializationsAsync(ct);
        var openCounts = await tickets.GetOpenTicketCountsByAgentAsync(ct);

        var now = clock.UtcNow;
        var decision = triageService.EvaluateEscalation(
            ticket,
            customer,
            category,
            activeAgents,
            openCounts,
            now);

        var fromPriority = ticket.Priority;
        var fromAgentId = ticket.AssignedAgentId;
        var fromDueAtUtc = ticket.DueAtUtc;

        ticket.Escalate(decision.NewPriority, decision.NewDueAtUtc, decision.AssignedAgent?.Id, now);

        var escalation = new TicketEscalation(
            ticket.Id,
            fromPriority,
            decision.NewPriority,
            fromAgentId,
            decision.AssignedAgent?.Id,
            fromDueAtUtc,
            decision.NewDueAtUtc,
            request.Reason,
            request.EscalatedBy ?? string.Empty,
            now);

        await tickets.AddEscalationAsync(escalation, ct);
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "Ticket {Reference} escalated from {FromPriority} to {ToPriority} by {EscalatedBy}. Assigned agent: {AgentId}. Reason: {Reason}",
            ticket.Reference, fromPriority, decision.NewPriority, request.EscalatedBy, decision.AssignedAgent?.Id, request.Reason);

        var updatedTicket = await getTicket.HandleAsync(id, ct);

        var escalationDto = new TicketEscalationDto
        {
            Id = escalation.Id,
            TicketId = escalation.TicketId,
            FromPriority = escalation.FromPriority,
            ToPriority = escalation.ToPriority,
            FromAgentId = escalation.FromAgentId,
            ToAgentId = escalation.ToAgentId,
            FromDueAtUtc = escalation.FromDueAtUtc,
            ToDueAtUtc = escalation.ToDueAtUtc,
            Reason = escalation.Reason,
            EscalatedBy = escalation.EscalatedBy,
            EscalatedAtUtc = escalation.EscalatedAtUtc
        };

        return new EscalateTicketResponse(updatedTicket, escalationDto);
    }
}
