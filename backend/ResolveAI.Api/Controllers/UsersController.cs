using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ResolveAI.Api.Data;

namespace ResolveAI.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly AppDbContext _context;

    public UsersController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet("technicians")]
    [Authorize(Roles = "Manager,Admin")]
    public async Task<IActionResult> GetTechnicians()
    {
        var technicians = await _context.Users
            .AsNoTracking()
            .Include(u => u.Role)
            .Where(u =>
                u.IsActive &&
                u.Role.Name == "Technician"
            )
            .OrderBy(u => u.LastName)
            .ThenBy(u => u.FirstName)
            .Select(u => new
            {
                id = u.Id,
                firstName = u.FirstName,
                lastName = u.LastName,
                email = u.Email
            })
            .ToListAsync();

        return Ok(technicians);
    }
}
