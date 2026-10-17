using Microsoft.Extensions.Logging;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Exceptions;
using SupportDesk.Application.Features.Tickets.Queries.GetTicket;
using SupportDesk.Application.Features.Tickets.Triage;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.Repositories;

namespace SupportDesk.Application.Features.Tickets.Commands.RaiseTicket;

/// <summary>
/// Raises a ticket for a customer and performs automatic triage and agent assignment.
/// </summary>
public sealed class RaiseTicketCommandHandler(
    ITicketRepository tickets,
    ICustomerRepository customers,
    ICategoryRepository categories,
    IAgentRepository agents,
    ITicketTriageService triageService,
    IUnitOfWork unitOfWork,
    IClock clock,
    GetTicketQueryHandler getTicket,
    ILogger<RaiseTicketCommandHandler> logger)
{
    /// <exception cref="NotFoundException">The customer or the category does not exist.</exception>
    public async Task<TicketDetailDto> HandleAsync(CreateTicketRequest request, CancellationToken ct)
    {
        if (!await customers.ExistsAsync(request.CustomerId, ct))
        {
            throw new NotFoundException("Customer", request.CustomerId);
        }

        var customer = await customers.GetByIdAsync(request.CustomerId, ct)
            ?? throw new NotFoundException("Customer", request.CustomerId);

        if (!await categories.ExistsAsync(request.CategoryId, ct))
        {
            throw new NotFoundException("Category", request.CategoryId);
        }

        var category = await categories.GetByIdAsync(request.CategoryId, ct)
            ?? throw new NotFoundException("Category", request.CategoryId);

        var activeAgents = await agents.GetAllActiveWithSpecializationsAsync(ct);
        var openCounts = await tickets.GetOpenTicketCountsByAgentAsync(ct);

        var now = clock.UtcNow;
        var triage = triageService.EvaluateTriage(
            request.RequestedPriority,
            customer,
            category,
            activeAgents,
            openCounts,
            now);

        var ticket = Ticket.Raise(
            await tickets.NextReferenceAsync(ct),
            request.Title,
            request.Description,
            request.CustomerId,
            request.CategoryId,
            triage.Priority,
            now,
            triage.DueAtUtc);

        if (triage.AssignedAgent is not null)
        {
            ticket.AssignTo(triage.AssignedAgent.Id, now);
        }

        await tickets.AddAsync(ticket, ct);
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "Ticket {Reference} raised for customer {CustomerId} in category {CategoryId}. Assigned: {AssignedAgentId}, Priority: {Priority}, Due: {DueAtUtc}",
            ticket.Reference, ticket.CustomerId, ticket.CategoryId, ticket.AssignedAgentId, ticket.Priority, ticket.DueAtUtc);

        var detail = await getTicket.HandleAsync(ticket.Id, ct);
        detail.Triage = new TriageResultDto(
            triage.Priority,
            triage.PriorityReason,
            triage.SlaWindowHours,
            triage.DueAtUtc,
            triage.SlaReason,
            triage.AssignedAgent is null ? null : new AgentSummaryDto(triage.AssignedAgent.Id, triage.AssignedAgent.FullName),
            triage.AssignmentReason,
            SlaEvaluator.Evaluate(ticket.DueAtUtc, ticket.ResolvedAtUtc, now, ticket.CreatedAtUtc));

        return detail;
    }
}
