using SupportDesk.Domain.Aggregates.Tickets;

namespace SupportDesk.Application.Configuration;

/// <summary>
/// SLA configuration options bound from appsettings Sla section.
/// </summary>
public sealed class SlaOptions
{
    public const string SectionName = "Sla";

    public Dictionary<string, double> BaseWindowHours { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Critical"] = 4,
        ["High"] = 8,
        ["Medium"] = 24,
        ["Low"] = 72
    };

    public double PremiumCustomerMultiplier { get; set; } = 0.5;

    public double MinimumWindowHours { get; set; } = 1.0;

    public double AtRiskThresholdPercent { get; set; } = 25.0;

    public double GetBaseWindowHours(TicketPriority priority)
    {
        var key = priority.ToString();
        if (BaseWindowHours != null && BaseWindowHours.TryGetValue(key, out var hours))
        {
            return hours;
        }

        return priority switch
        {
            TicketPriority.Critical => 4,
            TicketPriority.High => 8,
            TicketPriority.Medium => 24,
            TicketPriority.Low => 72,
            _ => 24
        };
    }
}
