using Microsoft.AspNetCore.Mvc;

namespace ResolveAI.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new
        {
            status = "Healthy",
            application = "ResolveAI",
            timestamp = DateTime.UtcNow
        });
    }
}