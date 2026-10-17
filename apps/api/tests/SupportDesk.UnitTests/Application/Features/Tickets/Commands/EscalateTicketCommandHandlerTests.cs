using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Exceptions;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.Exceptions;
using SupportDesk.UnitTests.TestDoubles;
using Xunit;

namespace SupportDesk.UnitTests.Application.Features.Tickets.Commands;

public class EscalateTicketCommandHandlerTests
{
    private static EscalateTicketRequest Request(string reason = "Issue escalated by client request", string actor = "Agent Smith") =>
        new() { Reason = reason, EscalatedBy = actor };

    [Fact]
    public async Task HandleAsync_WhenTicketDoesNotExist_ThrowsNotFound()
    {
        var context = new TicketCommandTestContext();
        context.Tickets.Setup(t => t.GetByIdAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Ticket?)null);

        var ex = await Assert.ThrowsAsync<NotFoundException>(
            () => context.EscalateTicket.HandleAsync(999, Request(), CancellationToken.None));

        Assert.Contains("Ticket", ex.Message);
        context.VerifySaved(Times.Never());
    }

    [Fact]
    public async Task HandleAsync_WhenTicketIsCritical_ThrowsConflict()
    {
        var context = new TicketCommandTestContext();
        var ticket = Ticket.Raise("TCK-0001", "Title", "Desc", 1, 1, TicketPriority.Critical, context.Clock.UtcNow);
        context.ExistingTicket(ticket);

        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => context.EscalateTicket.HandleAsync(ticket.Id, Request(), CancellationToken.None));

        Assert.Contains("already at Critical priority", ex.Message);
        context.VerifySaved(Times.Never());
    }

    [Theory]
    [InlineData(TicketStatus.Resolved)]
    [InlineData(TicketStatus.Closed)]
    public async Task HandleAsync_WhenTicketIsResolvedOrClosed_ThrowsConflict(TicketStatus terminalStatus)
    {
        var context = new TicketCommandTestContext();
        var ticket = Ticket.Raise("TCK-0001", "Title", "Desc", 1, 1, TicketPriority.Medium, context.Clock.UtcNow);
        ticket.ChangeStatus(terminalStatus, context.Clock.UtcNow);
        context.ExistingTicket(ticket);

        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => context.EscalateTicket.HandleAsync(ticket.Id, Request(), CancellationToken.None));

        Assert.Contains($"is {terminalStatus} and cannot be escalated", ex.Message);
        context.VerifySaved(Times.Never());
    }

    [Fact]
    public async Task HandleAsync_StepsUpPriority_RecalculatesDueAt_AndRecordsEscalation()
    {
        var context = new TicketCommandTestContext();
        var initialDue = context.Clock.UtcNow.AddHours(24);
        var ticket = Ticket.Raise("TCK-0001", "Title", "Desc", 1, 1, TicketPriority.Medium, context.Clock.UtcNow, initialDue);
        context.ExistingTicket(ticket);

        // Advance clock by 2 hours
        context.Clock.Advance(TimeSpan.FromHours(2));
        var escalationTime = context.Clock.UtcNow;

        TicketEscalation? recordedEscalation = null;
        context.Tickets.Setup(t => t.AddEscalationAsync(It.IsAny<TicketEscalation>(), It.IsAny<CancellationToken>()))
            .Callback<TicketEscalation, CancellationToken>((esc, _) => recordedEscalation = esc)
            .Returns(Task.CompletedTask);

        var response = await context.EscalateTicket.HandleAsync(ticket.Id, Request("Escalating due to SLA urgency", "Lead Agent"), CancellationToken.None);

        Assert.NotNull(response);
        // Priority increased from Medium to High
        Assert.Equal(TicketPriority.High, ticket.Priority);
        // High SLA is 8 hours from escalation time
        Assert.Equal(escalationTime.AddHours(8), ticket.DueAtUtc);

        // Escalation audit history verified
        Assert.NotNull(recordedEscalation);
        Assert.Equal(ticket.Id, recordedEscalation.TicketId);
        Assert.Equal(TicketPriority.Medium, recordedEscalation.FromPriority);
        Assert.Equal(TicketPriority.High, recordedEscalation.ToPriority);
        Assert.Equal("Escalating due to SLA urgency", recordedEscalation.Reason);
        Assert.Equal("Lead Agent", recordedEscalation.EscalatedBy);
        Assert.Equal(escalationTime, recordedEscalation.EscalatedAtUtc);

        context.VerifySaved(Times.Once());
    }
}
