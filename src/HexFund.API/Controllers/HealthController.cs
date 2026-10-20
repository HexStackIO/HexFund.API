using System.Reflection;
using HexFund.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HexFund.API.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    private static readonly string Version =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "unknown";

    [HttpGet]
    public async Task<IActionResult> GetHealth([FromServices] HexFundDbContext db, CancellationToken cancellationToken)
    {
        var databaseUp = await db.Database.CanConnectAsync(cancellationToken);

        var body = new
        {
            Status = databaseUp ? "Healthy" : "Unhealthy",
            Database = databaseUp ? "Up" : "Down",
            Timestamp = DateTimeOffset.UtcNow,
            Version
        };

        return databaseUp ? Ok(body) : StatusCode(StatusCodes.Status503ServiceUnavailable, body);
    }
}
