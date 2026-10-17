using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using SupportDesk.Application.Contracts.Common;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Features.Tickets.Commands.AssignTicket;
using SupportDesk.Application.Features.Tickets.Commands.ChangeTicketStatus;
using SupportDesk.Application.Features.Tickets.Commands.EscalateTicket;
using SupportDesk.Application.Features.Tickets.Commands.RaiseTicket;
using SupportDesk.Application.Features.Tickets.Queries.GetTicket;
using SupportDesk.Application.Features.Tickets.Queries.GetTicketEscalations;
using SupportDesk.Application.Features.Tickets.Queries.SearchTickets;

namespace SupportDesk.Presentation.Controllers;

[ApiController]
[Route("api/tickets")]
[Produces("application/json")]
public sealed class TicketsController : ControllerBase
{
    /// <summary>Searches tickets with filters, sorting and paging.</summary>
    [HttpGet]
    public Task<PagedResult<TicketListItemDto>> Search(
        [FromQuery] TicketQuery query,
        [FromServices] SearchTicketsQueryHandler handler,
        CancellationToken ct) =>
        handler.HandleAsync(query, ct);

    /// <summary>Returns one ticket.</summary>
    [HttpGet("{id:int}")]
    public Task<TicketDetailDto> Get(int id, [FromServices] GetTicketQueryHandler handler, CancellationToken ct) =>
        handler.HandleAsync(id, ct);

    /// <summary>Raises a new ticket.</summary>
    [HttpPost]
    public async Task<ActionResult<TicketDetailDto>> Create(
        CreateTicketRequest request,
        [FromServices] RaiseTicketCommandHandler handler,
        CancellationToken ct)
    {
        var created = await handler.HandleAsync(request, ct);

        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    /// <summary>Moves a ticket to another status.</summary>
    [HttpPatch("{id:int}/status")]
    public Task<TicketDetailDto> UpdateStatus(
        int id,
        UpdateTicketStatusRequest request,
        [FromServices] ChangeTicketStatusCommandHandler handler,
        CancellationToken ct) =>
        handler.HandleAsync(id, request, ct);

    /// <summary>Assigns the ticket to an agent, or unassigns it when no agent is given.</summary>
    [HttpPatch("{id:int}/assignment")]
    public Task<TicketDetailDto> Assign(
        int id,
        AssignTicketRequest request,
        [FromServices] AssignTicketCommandHandler handler,
        CancellationToken ct) =>
        handler.HandleAsync(id, request, ct);

    /// <summary>Escalates a ticket to the next priority level.</summary>
    [HttpPost("{id:int}/escalate")]
    public async Task<ActionResult<EscalateTicketResponse>> Escalate(
        int id,
        EscalateTicketRequest request,
        [FromServices] EscalateTicketCommandHandler handler,
        CancellationToken ct)
    {
        var actorName = User.FindFirstValue(System.Security.Claims.ClaimTypes.Name)
            ?? User.FindFirstValue(System.Security.Claims.ClaimTypes.Email)
            ?? User.Identity?.Name
            ?? (!string.IsNullOrWhiteSpace(request.EscalatedBy) ? request.EscalatedBy : null);

        if (string.IsNullOrWhiteSpace(actorName))
        {
            throw new SupportDesk.Application.Exceptions.ValidationException("EscalatedBy", "Escalated by actor could not be determined.");
        }

        var secureRequest = request with { EscalatedBy = actorName };
        var result = await handler.HandleAsync(id, secureRequest, ct);
        return Ok(result);
    }

    /// <summary>Gets the escalation history for a ticket, newest first.</summary>
    [HttpGet("{id:int}/escalations")]
    public Task<IReadOnlyList<TicketEscalationDto>> GetEscalations(
        int id,
        [FromServices] GetTicketEscalationsQueryHandler handler,
        CancellationToken ct) =>
        handler.HandleAsync(id, ct);
}
