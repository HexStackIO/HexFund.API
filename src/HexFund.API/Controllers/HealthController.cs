using Microsoft.AspNetCore.Mvc;

namespace HexFund.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    private readonly ILogger<HealthController> _logger;

    public HealthController(ILogger<HealthController> logger)
    {
        _logger = logger;
    }

    [HttpGet]
    public IActionResult GetHealth()
    {
        return Ok(new
        {
            Status = "Healthy",
            Timestamp = DateTimeOffset.UtcNow,
            Version = "1.0.0"
        });
    }
}
