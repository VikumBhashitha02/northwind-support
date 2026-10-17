using Microsoft.Extensions.Options;
using SupportDesk.Application.Configuration;
using SupportDesk.Application.Features.Tickets.Triage;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.Exceptions;
using SupportDesk.UnitTests.TestDoubles;
using Xunit;

namespace SupportDesk.UnitTests.Application.Features.Tickets.Triage;

public class TicketTriageServiceTests
{
    private readonly SlaOptions _options = new();
    private readonly TicketTriageService _service;
    private readonly DateTime _now = FixedClock.DefaultNow;

    public TicketTriageServiceTests()
    {
        _service = new TicketTriageService(Options.Create(_options));
    }

    private static Customer CreateCustomer(CustomerTier tier = CustomerTier.Standard) =>
        new("Contoso Ltd", "contact@contoso.com", "555-0100", tier, FixedClock.DefaultNow);

    private static Category CreateCategory(bool requiresSpecialist = false, bool forcesCritical = false) =>
        new("General", requiresSpecialist, forcesCritical);

    #region Priority Rules (BR-1)

    [Fact]
    public void EvaluateTriage_UsesRequestedPriority_WhenCategoryDoesNotForceCritical()
    {
        var customer = CreateCustomer();
        var category = CreateCategory(forcesCritical: false);

        var decision = _service.EvaluateTriage(
            TicketPriority.High, customer, category, [], new Dictionary<int, int>(), _now);

        Assert.Equal(TicketPriority.High, decision.Priority);
        Assert.Contains("Requested priority 'High' applied", decision.PriorityReason);
    }

    [Fact]
    public void EvaluateTriage_DefaultsToMedium_WhenNoPrioritySupplied()
    {
        var customer = CreateCustomer();
        var category = CreateCategory(forcesCritical: false);

        var decision = _service.EvaluateTriage(
            null, customer, category, [], new Dictionary<int, int>(), _now);

        Assert.Equal(TicketPriority.Medium, decision.Priority);
        Assert.Contains("Default priority 'Medium' applied", decision.PriorityReason);
    }

    [Theory]
    [InlineData(TicketPriority.Low)]
    [InlineData(TicketPriority.Medium)]
    [InlineData(TicketPriority.High)]
    [InlineData(null)]
    public void EvaluateTriage_ForcesCriticalPriority_WhenCategoryConfigured(TicketPriority? requested)
    {
        var customer = CreateCustomer();
        var category = new Category("Security Outage", requiresSpecialist: true, forcesCriticalPriority: true);

        var decision = _service.EvaluateTriage(
            requested, customer, category, [], new Dictionary<int, int>(), _now);

        Assert.Equal(TicketPriority.Critical, decision.Priority);
        Assert.Contains("forces Critical priority", decision.PriorityReason);
    }

    #endregion

    #region SLA Rules (BR-2, BR-3, BR-6)

    [Theory]
    [InlineData(TicketPriority.Critical, 4)]
    [InlineData(TicketPriority.High, 8)]
    [InlineData(TicketPriority.Medium, 24)]
    [InlineData(TicketPriority.Low, 72)]
    public void EvaluateTriage_CalculatesStandardSlaWindows(TicketPriority priority, double expectedHours)
    {
        var customer = CreateCustomer(CustomerTier.Standard);
        var category = CreateCategory();

        var decision = _service.EvaluateTriage(
            priority, customer, category, [], new Dictionary<int, int>(), _now);

        Assert.Equal(expectedHours, decision.SlaWindowHours);
        Assert.Equal(_now.AddHours(expectedHours), decision.DueAtUtc);
    }

    [Theory]
    [InlineData(TicketPriority.Critical, 2)]
    [InlineData(TicketPriority.High, 4)]
    [InlineData(TicketPriority.Medium, 12)]
    [InlineData(TicketPriority.Low, 36)]
    public void EvaluateTriage_HalvesSlaWindow_ForPremiumCustomer(TicketPriority priority, double expectedHours)
    {
        var customer = CreateCustomer(CustomerTier.Premium);
        var category = CreateCategory();

        var decision = _service.EvaluateTriage(
            priority, customer, category, [], new Dictionary<int, int>(), _now);

        Assert.Equal(expectedHours, decision.SlaWindowHours);
        Assert.Equal(_now.AddHours(expectedHours), decision.DueAtUtc);
        Assert.Contains("reduced", decision.SlaReason);
        Assert.Contains("50", decision.SlaReason);
    }

    [Fact]
    public void EvaluateTriage_AppliesMinimumOneHourFloor_ForPremiumCustomer()
    {
        // Custom SLA config with 1 hour base window for Critical
        var customOptions = new SlaOptions();
        customOptions.BaseWindowHours["Critical"] = 1.0;
        var service = new TicketTriageService(Options.Create(customOptions));

        var customer = CreateCustomer(CustomerTier.Premium);
        var category = CreateCategory();

        var decision = service.EvaluateTriage(
            TicketPriority.Critical, customer, category, [], new Dictionary<int, int>(), _now);

        // 1.0 * 0.5 = 0.5, clamped to 1.0 floor
        Assert.Equal(1.0, decision.SlaWindowHours);
        Assert.Equal(_now.AddHours(1.0), decision.DueAtUtc);
        Assert.Contains("minimum floor of 1h", decision.SlaReason);
    }

    #endregion

    #region Agent Assignment Rules (BR-4, BR-5)

    [Fact]
    public void EvaluateTriage_AssignsAgentWithFewestOpenTickets()
    {
        var customer = CreateCustomer();
        var category = CreateCategory();

        var agent1 = new Agent("Agent 1", "agent1@example.com", 10, _now); // id 0
        var agent2 = new Agent("Agent 2", "agent2@example.com", 10, _now); // id 0
        typeof(Agent).GetProperty(nameof(Agent.Id))!.SetValue(agent1, 1);
        typeof(Agent).GetProperty(nameof(Agent.Id))!.SetValue(agent2, 2);

        var openCounts = new Dictionary<int, int>
        {
            [1] = 5,
            [2] = 2 // Fewer open tickets
        };

        var decision = _service.EvaluateTriage(
            TicketPriority.Medium, customer, category, [agent1, agent2], openCounts, _now);

        Assert.NotNull(decision.AssignedAgent);
        Assert.Equal(2, decision.AssignedAgent.Id);
        Assert.Contains("Agent 2 (2 open tickets)", decision.AssignmentReason);
    }

    [Fact]
    public void EvaluateTriage_UsesLowestAgentId_AsDeterministicTieBreaker()
    {
        var customer = CreateCustomer();
        var category = CreateCategory();

        var agent1 = new Agent("Agent 1", "agent1@example.com", 10, _now);
        var agent2 = new Agent("Agent 2", "agent2@example.com", 10, _now);
        typeof(Agent).GetProperty(nameof(Agent.Id))!.SetValue(agent1, 10);
        typeof(Agent).GetProperty(nameof(Agent.Id))!.SetValue(agent2, 5); // Lower ID

        var openCounts = new Dictionary<int, int>
        {
            [10] = 3,
            [5] = 3 // Tie
        };

        var decision = _service.EvaluateTriage(
            TicketPriority.Medium, customer, category, [agent1, agent2], openCounts, _now);

        Assert.NotNull(decision.AssignedAgent);
        Assert.Equal(5, decision.AssignedAgent.Id);
    }

    [Fact]
    public void EvaluateTriage_ExcludesInactiveAgents()
    {
        var customer = CreateCustomer();
        var category = CreateCategory();

        var inactiveAgent = new Agent("Inactive", "inactive@example.com", 10, _now);
        inactiveAgent.Deactivate();
        typeof(Agent).GetProperty(nameof(Agent.Id))!.SetValue(inactiveAgent, 1);

        var decision = _service.EvaluateTriage(
            TicketPriority.Medium, customer, category, [inactiveAgent], new Dictionary<int, int>(), _now);

        Assert.Null(decision.AssignedAgent);
        Assert.Contains("No eligible agent", decision.AssignmentReason);
    }

    [Fact]
    public void EvaluateTriage_ExcludesFullAgents()
    {
        var customer = CreateCustomer();
        var category = CreateCategory();

        var fullAgent = new Agent("Full Agent", "full@example.com", maxOpenTickets: 5, _now);
        typeof(Agent).GetProperty(nameof(Agent.Id))!.SetValue(fullAgent, 1);

        var openCounts = new Dictionary<int, int> { [1] = 5 }; // At max capacity

        var decision = _service.EvaluateTriage(
            TicketPriority.Medium, customer, category, [fullAgent], openCounts, _now);

        Assert.Null(decision.AssignedAgent);
        Assert.Contains("All eligible agents are at maximum ticket capacity", decision.AssignmentReason);
    }

    [Fact]
    public void EvaluateTriage_RequiresSpecialist_WhenCategoryConfigured()
    {
        var customer = CreateCustomer();
        var category = CreateCategory(requiresSpecialist: true);
        typeof(Category).GetProperty(nameof(Category.Id))!.SetValue(category, 42);

        var generalAgent = new Agent("General Agent", "gen@example.com", 10, _now);
        typeof(Agent).GetProperty(nameof(Agent.Id))!.SetValue(generalAgent, 1);

        var specialistAgent = new Agent("Specialist Agent", "spec@example.com", 10, _now);
        typeof(Agent).GetProperty(nameof(Agent.Id))!.SetValue(specialistAgent, 2);
        specialistAgent.AddSpecialization(42);

        var openCounts = new Dictionary<int, int> { [1] = 1, [2] = 3 };

        var decision = _service.EvaluateTriage(
            TicketPriority.Medium, customer, category, [generalAgent, specialistAgent], openCounts, _now);

        // Specialist must be selected even though general agent has fewer open tickets
        Assert.NotNull(decision.AssignedAgent);
        Assert.Equal(2, decision.AssignedAgent.Id);
    }

    [Fact]
    public void EvaluateTriage_WhenNoEligibleSpecialist_LeavesUnassignedWithReason()
    {
        var customer = CreateCustomer();
        var category = CreateCategory(requiresSpecialist: true);
        typeof(Category).GetProperty(nameof(Category.Id))!.SetValue(category, 99);

        var generalAgent = new Agent("General", "gen@example.com", 10, _now);
        typeof(Agent).GetProperty(nameof(Agent.Id))!.SetValue(generalAgent, 1);

        var decision = _service.EvaluateTriage(
            TicketPriority.Medium, customer, category, [generalAgent], new Dictionary<int, int>(), _now);

        Assert.Null(decision.AssignedAgent);
        Assert.Contains("requires a specialist, but no active agent has this specialization", decision.AssignmentReason);
    }

    #endregion

    #region Escalation Rules (BR-7)

    [Theory]
    [InlineData(TicketPriority.Low, TicketPriority.Medium)]
    [InlineData(TicketPriority.Medium, TicketPriority.High)]
    [InlineData(TicketPriority.High, TicketPriority.Critical)]
    public void EvaluateEscalation_StepsPriorityUpOneLevel(TicketPriority from, TicketPriority expectedTo)
    {
        var customer = CreateCustomer();
        var category = CreateCategory();
        var ticket = Ticket.Raise("TCK-0001", "Title", "Desc", customer.Id, category.Id, from, _now);

        var decision = _service.EvaluateEscalation(
            ticket, customer, category, [], new Dictionary<int, int>(), _now);

        Assert.Equal(expectedTo, decision.NewPriority);
    }

    [Fact]
    public void EvaluateEscalation_ThrowsConflict_WhenAlreadyCritical()
    {
        var customer = CreateCustomer();
        var category = CreateCategory();
        var ticket = Ticket.Raise("TCK-0001", "Title", "Desc", customer.Id, category.Id, TicketPriority.Critical, _now);

        var ex = Assert.Throws<BusinessRuleViolationException>(() =>
            _service.EvaluateEscalation(ticket, customer, category, [], new Dictionary<int, int>(), _now));

        Assert.Contains("already at Critical priority", ex.Message);
    }

    [Theory]
    [InlineData(TicketStatus.Resolved)]
    [InlineData(TicketStatus.Closed)]
    public void EvaluateEscalation_ThrowsConflict_WhenResolvedOrClosed(TicketStatus terminalStatus)
    {
        var customer = CreateCustomer();
        var category = CreateCategory();
        var ticket = Ticket.Raise("TCK-0001", "Title", "Desc", customer.Id, category.Id, TicketPriority.Medium, _now);
        ticket.ChangeStatus(terminalStatus, _now);

        var ex = Assert.Throws<BusinessRuleViolationException>(() =>
            _service.EvaluateEscalation(ticket, customer, category, [], new Dictionary<int, int>(), _now));

        Assert.Contains($"is {terminalStatus} and cannot be escalated", ex.Message);
    }

    [Fact]
    public void EvaluateEscalation_RecalculatesDueAt_FromEscalationTime()
    {
        var customer = CreateCustomer(CustomerTier.Standard);
        var category = CreateCategory();
        var ticket = Ticket.Raise("TCK-0001", "Title", "Desc", customer.Id, category.Id, TicketPriority.Medium, _now);

        // Escalation happens 10 hours later
        var escalationTime = _now.AddHours(10);
        // Medium escalated to High -> High base SLA is 8 hours
        var decision = _service.EvaluateEscalation(
            ticket, customer, category, [], new Dictionary<int, int>(), escalationTime);

        Assert.Equal(TicketPriority.High, decision.NewPriority);
        Assert.Equal(8.0, decision.SlaWindowHours);
        Assert.Equal(escalationTime.AddHours(8), decision.NewDueAtUtc);
    }

    [Fact]
    public void EvaluateEscalation_RetainsCurrentAgent_IfStillEligible()
    {
        var customer = CreateCustomer();
        var category = CreateCategory();
        var ticket = Ticket.Raise("TCK-0001", "Title", "Desc", customer.Id, category.Id, TicketPriority.Medium, _now);

        var agent1 = new Agent("Current Agent", "agent1@example.com", 10, _now);
        typeof(Agent).GetProperty(nameof(Agent.Id))!.SetValue(agent1, 1);
        ticket.AssignTo(1, _now);

        var agent2 = new Agent("Other Agent", "agent2@example.com", 10, _now);
        typeof(Agent).GetProperty(nameof(Agent.Id))!.SetValue(agent2, 2);

        var openCounts = new Dictionary<int, int>
        {
            [1] = 4, // Current agent has 4
            [2] = 1  // Other agent has fewer, but current agent is retained!
        };

        var decision = _service.EvaluateEscalation(
            ticket, customer, category, [agent1, agent2], openCounts, _now);

        Assert.NotNull(decision.AssignedAgent);
        Assert.Equal(1, decision.AssignedAgent.Id);
        Assert.Contains("Retained current agent Current Agent", decision.AssignmentReason);
    }

    [Fact]
    public void EvaluateEscalation_ReassignsAgent_WhenCurrentAgentNoLongerEligible()
    {
        var customer = CreateCustomer();
        var category = CreateCategory(requiresSpecialist: true);
        typeof(Category).GetProperty(nameof(Category.Id))!.SetValue(category, 50);

        var ticket = Ticket.Raise("TCK-0001", "Title", "Desc", customer.Id, category.Id, TicketPriority.Medium, _now);

        // Current agent is inactive
        var agent1 = new Agent("Old Agent", "old@example.com", 10, _now);
        agent1.Deactivate();
        typeof(Agent).GetProperty(nameof(Agent.Id))!.SetValue(agent1, 1);
        ticket.AssignTo(1, _now);

        // New eligible agent with specialization
        var agent2 = new Agent("Specialist Agent", "spec@example.com", 10, _now);
        typeof(Agent).GetProperty(nameof(Agent.Id))!.SetValue(agent2, 2);
        agent2.AddSpecialization(50);

        var openCounts = new Dictionary<int, int> { [1] = 1, [2] = 2 };

        var decision = _service.EvaluateEscalation(
            ticket, customer, category, [agent1, agent2], openCounts, _now);

        Assert.NotNull(decision.AssignedAgent);
        Assert.Equal(2, decision.AssignedAgent.Id);
        Assert.Contains("Reassigned to Specialist Agent", decision.AssignmentReason);
    }

    #endregion
}
