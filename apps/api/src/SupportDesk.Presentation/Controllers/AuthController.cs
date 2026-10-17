using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SupportDesk.Application.Contracts.Auth;
using SupportDesk.Application.Exceptions;
using SupportDesk.Application.Features.Auth.Commands.Login;
using SupportDesk.Domain.Repositories;

namespace SupportDesk.Presentation.Controllers;

[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public sealed class AuthController : ControllerBase
{
    /// <summary>Authenticates an agent and returns a signed JWT access token.</summary>
    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(
        LoginRequest request,
        [FromServices] LoginCommandHandler handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(request, ct);
        return Ok(result);
    }

    /// <summary>Returns the current authenticated agent's profile.</summary>
    [HttpGet("me")]
    public async Task<ActionResult<CurrentUserDto>> GetMe(
        [FromServices] IAgentRepository agents,
        CancellationToken ct)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdStr, out var agentId))
        {
            throw new UnauthorizedException("Invalid authentication claims.");
        }

        var agent = await agents.GetByIdAsync(agentId, ct);
        if (agent is null || !agent.IsActive)
        {
            throw new UnauthorizedException("Agent not found or inactive.");
        }

        return Ok(new CurrentUserDto(agent.Id, agent.FullName, agent.Email));
    }
}
