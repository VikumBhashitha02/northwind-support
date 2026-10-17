using SupportDesk.Domain.Aggregates.Agents;

namespace SupportDesk.Application.Abstractions;

/// <summary>
/// Generates signed JWT access tokens for authenticated agents.
/// </summary>
public interface IJwtTokenGenerator
{
    (string Token, DateTime ExpiresAtUtc) GenerateToken(Agent agent);
}
