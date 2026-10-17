using Microsoft.Extensions.Logging;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Auth;
using SupportDesk.Application.Exceptions;
using SupportDesk.Domain.Repositories;

namespace SupportDesk.Application.Features.Auth.Commands.Login;

/// <summary>
/// Authenticates an agent via email and password, returning a signed JWT access token.
/// </summary>
public sealed class LoginCommandHandler(
    IAgentRepository agents,
    IPasswordHasher passwordHasher,
    IJwtTokenGenerator tokenGenerator,
    ILogger<LoginCommandHandler> logger)
{
    /// <exception cref="UnauthorizedException">Email unknown, password invalid, or agent inactive.</exception>
    public async Task<LoginResponse> HandleAsync(LoginRequest request, CancellationToken ct)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var agent = await agents.GetByEmailAsync(normalizedEmail, ct);

        // Generic 401 response for unknown email or invalid password to prevent account enumeration
        if (agent is null || string.IsNullOrEmpty(agent.PasswordHash) ||
            !passwordHasher.VerifyPassword(request.Password, agent.PasswordHash))
        {
            logger.LogWarning("Authentication failed for email: {Email}", normalizedEmail);
            throw new UnauthorizedException("Invalid email or password.");
        }

        // Prevent inactive agents from authenticating
        if (!agent.IsActive)
        {
            logger.LogWarning("Authentication rejected for inactive agent: {AgentId} ({Email})", agent.Id, agent.Email);
            throw new UnauthorizedException("Invalid email or password.");
        }

        var (token, expiresAtUtc) = tokenGenerator.GenerateToken(agent);

        logger.LogInformation("Agent {AgentId} ({Email}) logged in successfully.", agent.Id, agent.Email);

        return new LoginResponse
        {
            Token = token,
            ExpiresAtUtc = expiresAtUtc,
            User = new CurrentUserDto(agent.Id, agent.FullName, agent.Email)
        };
    }
}
