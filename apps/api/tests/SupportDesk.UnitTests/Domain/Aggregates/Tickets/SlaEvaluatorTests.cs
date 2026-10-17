using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.UnitTests.TestDoubles;
using Xunit;

namespace SupportDesk.UnitTests.Domain.Aggregates.Tickets;

public class SlaEvaluatorTests
{
    private static readonly DateTime Now = FixedClock.DefaultNow;

    [Fact]
    public void Evaluate_WithoutADueDate_IsNotApplicable() =>
        Assert.Equal(SlaStatus.NotApplicable, SlaEvaluator.Evaluate(null, null, Now));

    [Fact]
    public void Evaluate_ResolvedOnTime_IsMet() =>
        Assert.Equal(SlaStatus.Met, SlaEvaluator.Evaluate(Now, Now, Now.AddDays(1)));

    [Fact]
    public void Evaluate_ResolvedLate_IsBreached() =>
        Assert.Equal(SlaStatus.Breached, SlaEvaluator.Evaluate(Now, Now.AddMinutes(1), Now));

    [Fact]
    public void Evaluate_OpenAndPastDue_IsBreached() =>
        Assert.Equal(SlaStatus.Breached, SlaEvaluator.Evaluate(Now.AddMinutes(-1), null, Now));

    [Fact]
    public void Evaluate_OpenAndMoreThan25PercentRemaining_IsWithinSla()
    {
        // Total window: 4 hours (start 10:00, due 14:00)
        var start = Now;
        var due = start.AddHours(4);
        // Current time: 11:00 (3 hours = 75% remaining)
        var current = start.AddHours(1);

        Assert.Equal(SlaStatus.WithinSla, SlaEvaluator.Evaluate(due, null, current, start));
    }

    [Fact]
    public void Evaluate_OpenAndExactly25PercentRemaining_IsAtRisk()
    {
        // Total window: 4 hours = 240 minutes
        // 25% of 240 minutes = 60 minutes
        var start = Now;
        var due = start.AddHours(4);
        var current = due.AddMinutes(-60); // Exactly 25% remaining

        Assert.Equal(SlaStatus.AtRisk, SlaEvaluator.Evaluate(due, null, current, start));
    }

    [Fact]
    public void Evaluate_OpenAndLessThan25PercentRemaining_IsAtRisk()
    {
        var start = Now;
        var due = start.AddHours(4);
        var current = due.AddMinutes(-30); // 12.5% remaining

        Assert.Equal(SlaStatus.AtRisk, SlaEvaluator.Evaluate(due, null, current, start));
    }

    [Fact]
    public void Evaluate_OpenAndJustAbove25PercentRemaining_IsWithinSla()
    {
        var start = Now;
        var due = start.AddHours(4);
        var current = due.AddMinutes(-61); // 61 minutes > 60 minutes (25%)

        Assert.Equal(SlaStatus.WithinSla, SlaEvaluator.Evaluate(due, null, current, start));
    }
}
