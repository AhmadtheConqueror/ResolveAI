using Microsoft.AspNetCore.Identity;
using ResolveAI.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ResolveAI.Api.Data;
using ResolveAI.Api.DTOs;
using ResolveAI.Api.Entities;

namespace ResolveAI.Api.Controllers;

[ApiController]
[Route("api/[controller]")]

public class AuthController : ControllerBase
{
    private readonly ITokenService _tokenService;
    private readonly AppDbContext _context;
    private readonly IPasswordHasher<AppUser> _passwordHasher;
    private readonly IConfiguration _configuration;

    public AuthController(
        AppDbContext context,
        IPasswordHasher<AppUser> passwordHasher,
        ITokenService tokenService,
        IConfiguration configuration)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _configuration = configuration;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request)
    {
        var allowPublicRegistration = _configuration.GetValue<bool>("Features:AllowPublicRegistration", false);
        if (!allowPublicRegistration)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                message = "Public registration is disabled. Contact your ResolveAI administrator for access."
            });
        }

        var email = request.Email.Trim().ToLowerInvariant();

        var emailExists = await _context.Users
            .AnyAsync(u => u.Email == email);

        if (emailExists)
        {
            return Conflict(new
            {
                message = "A user with this email already exists."
            });
        }

        var employeeRole = await _context.Roles
            .SingleOrDefaultAsync(r => r.Name == "Employee");

        if (employeeRole is null)
        {
            return StatusCode(500, new
            {
                message = "Default Employee role is not configured."
            });
        }

        if (request.DepartmentId.HasValue)
        {
            var departmentExists = await _context.Departments
                .AnyAsync(d => d.Id == request.DepartmentId.Value);

            if (!departmentExists)
            {
                return BadRequest(new
                {
                    message = "The selected department does not exist."
                });
            }
        }

        var user = new AppUser
        {
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Email = email,
            RoleId = employeeRole.Id,
            DepartmentId = request.DepartmentId
        };

        user.PasswordHash = _passwordHasher.HashPassword(
            user,
            request.Password
        );

        _context.Users.Add(user);

        await _context.SaveChangesAsync();

        return Created("", new
        {
            id = user.Id,
            firstName = user.FirstName,
            lastName = user.LastName,
            email = user.Email,
            role = employeeRole.Name
        });
    }
    [HttpPost("login")]
public async Task<IActionResult> Login(LoginRequest request)
{
    var email = request.Email.Trim().ToLowerInvariant();

    var user = await _context.Users
        .Include(u => u.Role)
        .SingleOrDefaultAsync(u => u.Email == email);

    if (user is null)
    {
        return Unauthorized(new
        {
            message = "Invalid email or password."
        });
    }

    if (!user.IsActive)
    {
        return Unauthorized(new
        {
            message = "This account is inactive."
        });
    }

    var passwordResult = _passwordHasher.VerifyHashedPassword(
        user,
        user.PasswordHash,
        request.Password
    );

    if (passwordResult == PasswordVerificationResult.Failed)
    {
        return Unauthorized(new
        {
            message = "Invalid email or password."
        });
    }

    var token = _tokenService.CreateToken(user);

    return Ok(new
    {
        token,
        user = new
        {
            id = user.Id,
            firstName = user.FirstName,
            lastName = user.LastName,
            email = user.Email,
            role = user.Role.Name
        }
    });
}
}