using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ResolveAI.Api.Data;
using ResolveAI.Api.DTOs;
using ResolveAI.Api.Entities;
using ResolveAI.Api.Services;

namespace ResolveAI.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly ITokenService _tokenService;
    private readonly AppDbContext _context;
    private readonly IPasswordHasher<AppUser> _passwordHasher;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        AppDbContext context,
        IPasswordHasher<AppUser> passwordHasher,
        ITokenService tokenService,
        IConfiguration configuration,
        ILogger<AuthController> logger)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _configuration = configuration;
        _logger = logger;
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
    [EnableRateLimiting("login-limiter")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        var user = await _context.Users
            .Include(u => u.Role)
            .SingleOrDefaultAsync(u => u.Email == email);

        if (user is null)
        {
            _logger.LogWarning("Login failed for {Email}: account not found.", email);
            return Unauthorized(new
            {
                message = "Invalid email or password."
            });
        }

        if (!user.IsActive)
        {
            _logger.LogWarning("Login failed for user {UserId} ({Email}): account is inactive.", user.Id, email);
            return Unauthorized(new
            {
                message = "Invalid email or password."
            });
        }

        var passwordResult = _passwordHasher.VerifyHashedPassword(
            user,
            user.PasswordHash,
            request.Password
        );

        if (passwordResult == PasswordVerificationResult.Failed)
        {
            _logger.LogWarning("Login failed for user {UserId} ({Email}): invalid password.", user.Id, email);
            return Unauthorized(new
            {
                message = "Invalid email or password."
            });
        }

        _logger.LogInformation("Successful login for user {UserId} ({Email}) with role {Role}.", user.Id, email, user.Role.Name);

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