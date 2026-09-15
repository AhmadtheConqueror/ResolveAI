using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ResolveAI.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ProfileController : ControllerBase
{
    [HttpGet]
    public IActionResult GetProfile()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var name = User.Identity?.Name;
        var role = User.FindFirstValue(ClaimTypes.Role);

        return Ok(new
        {
            message = "You are authenticated!",
            userId,
            name,
            role
        });
    }

    [HttpGet("admin-check")]
    [Authorize(Roles = "Admin")]
    public IActionResult AdminCheck()
    {
        return Ok(new
        {
            message = "Welcome, Admin. You have access to this endpoint."
        });
    }
}