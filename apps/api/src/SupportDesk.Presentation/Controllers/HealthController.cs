using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SupportDesk.Infrastructure.Data;

namespace SupportDesk.Presentation.Controllers;

/// <summary>
/// Unauthenticated health check endpoint verifying database connectivity.
/// </summary>
[ApiController]
[Route("health")]
[AllowAnonymous]
public sealed class HealthController(SupportDbContext dbContext) : ControllerBase
{
    /// <summary>Checks database reachability and returns health status.</summary>
    [HttpGet]
    public async Task<IActionResult> GetHealth(CancellationToken ct)
    {
        try
        {
            var canConnect = await dbContext.Database.CanConnectAsync(ct);
            if (canConnect)
            {
                return Ok(new
                {
                    status = "Healthy",
                    database = "Connected"
                });
            }

            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                status = "Unhealthy",
                database = "Unreachable"
            });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                status = "Unhealthy",
                database = "Error",
                error = ex.Message
            });
        }
    }
}
