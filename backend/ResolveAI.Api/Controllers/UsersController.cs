using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ResolveAI.Api.Data;
using ResolveAI.Api.DTOs;
using ResolveAI.Api.Entities;

namespace ResolveAI.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IPasswordHasher<AppUser> _passwordHasher;

    public UsersController(
        AppDbContext context,
        IPasswordHasher<AppUser> passwordHasher)
    {
        _context = context;
        _passwordHasher = passwordHasher;
    }

    // ─── GET /api/users/technicians ──────────────────────────────────────────
    // Used by assignment flow — Manager/Admin only

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

    // ─── GET /api/users/options ──────────────────────────────────────────────
    // Returns available Roles and Departments for the create/edit user form

    [HttpGet("options")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetUserOptions()
    {
        var roles = await _context.Roles
            .AsNoTracking()
            .OrderBy(r => r.Name)
            .Select(r => new { id = r.Id, name = r.Name })
            .ToListAsync();

        var departments = await _context.Departments
            .AsNoTracking()
            .OrderBy(d => d.Name)
            .Select(d => new { id = d.Id, name = d.Name })
            .ToListAsync();

        return Ok(new { roles, departments });
    }

    // ─── GET /api/users ──────────────────────────────────────────────────────
    // Full user list — Admin only

    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetUsers()
    {
        var users = await _context.Users
            .AsNoTracking()
            .Include(u => u.Role)
            .Include(u => u.Department)
            .OrderByDescending(u => u.CreatedAt)
            .Select(u => new
            {
                id = u.Id,
                firstName = u.FirstName,
                lastName = u.LastName,
                email = u.Email,
                role = new
                {
                    id = u.Role.Id,
                    name = u.Role.Name
                },
                department = u.Department == null
                    ? null
                    : new
                    {
                        id = u.Department.Id,
                        name = u.Department.Name
                    },
                isActive = u.IsActive,
                emailNotificationsEnabled = u.EmailNotificationsEnabled,
                createdAt = u.CreatedAt
            })
            .ToListAsync();

        return Ok(users);
    }

    // ─── POST /api/users ─────────────────────────────────────────────────────
    // Admin provisions a new organizational user

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> CreateUser(CreateUserRequest request)
    {
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

        var role = await _context.Roles
            .SingleOrDefaultAsync(r => r.Id == request.RoleId);

        if (role is null)
        {
            return BadRequest(new
            {
                message = "The selected role does not exist."
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
            RoleId = role.Id,
            DepartmentId = request.DepartmentId,
            EmailNotificationsEnabled = request.EmailNotificationsEnabled ?? true,
            IsActive = true
        };

        user.PasswordHash = _passwordHasher.HashPassword(
            user,
            request.TemporaryPassword
        );

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        return Created(
            $"/api/users/{user.Id}",
            new
            {
                id = user.Id,
                firstName = user.FirstName,
                lastName = user.LastName,
                email = user.Email,
                role = new { id = role.Id, name = role.Name },
                emailNotificationsEnabled = user.EmailNotificationsEnabled,
                isActive = user.IsActive,
                createdAt = user.CreatedAt
            }
        );
    }

    // ─── PATCH /api/users/{id} ───────────────────────────────────────────────
    // Admin updates user profile fields

    [HttpPatch("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> UpdateUser(
        Guid id,
        UpdateUserRequest request)
    {
        var adminIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(adminIdClaim, out var adminId))
        {
            return Unauthorized(new { message = "Invalid authenticated user." });
        }

        var user = await _context.Users
            .Include(u => u.Role)
            .Include(u => u.Department)
            .SingleOrDefaultAsync(u => u.Id == id);

        if (user is null)
        {
            return NotFound(new { message = "User not found." });
        }

        var role = await _context.Roles
            .SingleOrDefaultAsync(r => r.Id == request.RoleId);

        if (role is null)
        {
            return BadRequest(new { message = "The selected role does not exist." });
        }

        // Admin safety: prevent demoting the only remaining active Admin
        if (user.IsActive && user.Role.Name == "Admin" && role.Name != "Admin")
        {
            var otherActiveAdminCount = await CountOtherActiveAdminsAsync(user.Id);

            if (otherActiveAdminCount == 0)
            {
                return Conflict(new
                {
                    message =
                        "Cannot demote this user. They are the only remaining active Admin."
                });
            }
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

        var email = request.Email.Trim().ToLowerInvariant();

        var emailTaken = await _context.Users
            .AnyAsync(u => u.Email == email && u.Id != id);

        if (emailTaken)
        {
            return Conflict(new
            {
                message = "A user with this email already exists."
            });
        }

        user.FirstName = request.FirstName.Trim();
        user.LastName = request.LastName.Trim();
        user.Email = email;
        user.RoleId = role.Id;
        user.DepartmentId = request.DepartmentId;

        if (request.EmailNotificationsEnabled.HasValue)
        {
            user.EmailNotificationsEnabled = request.EmailNotificationsEnabled.Value;
        }

        await _context.SaveChangesAsync();

        return Ok(new
        {
            id = user.Id,
            firstName = user.FirstName,
            lastName = user.LastName,
            email = user.Email,
            role = new { id = role.Id, name = role.Name },
            department = user.Department == null
                ? null
                : new { id = user.Department.Id, name = user.Department.Name },
            emailNotificationsEnabled = user.EmailNotificationsEnabled,
            isActive = user.IsActive
        });
    }

    // ─── PATCH /api/users/{id}/status ───────────────────────────────────────
    // Admin activates or deactivates a user account

    [HttpPatch("{id:guid}/status")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> UpdateUserStatus(
        Guid id,
        UpdateUserStatusRequest request)
    {
        var adminIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(adminIdClaim, out var adminId))
        {
            return Unauthorized(new { message = "Invalid authenticated user." });
        }

        var user = await _context.Users
            .Include(u => u.Role)
            .SingleOrDefaultAsync(u => u.Id == id);

        if (user is null)
        {
            return NotFound(new { message = "User not found." });
        }

        // Admin safety: prevent deactivating the only remaining active Admin
        if (!request.IsActive && user.IsActive && user.Role.Name == "Admin")
        {
            var otherActiveAdminCount = await CountOtherActiveAdminsAsync(user.Id);

            if (otherActiveAdminCount == 0)
            {
                return Conflict(new
                {
                    message =
                        "Cannot deactivate this account. They are the only remaining active Admin."
                });
            }
        }

        // Prevent Admin deactivating their own account
        if (!request.IsActive && user.Id == adminId)
        {
            return Conflict(new
            {
                message = "You cannot deactivate your own account."
            });
        }

        user.IsActive = request.IsActive;
        await _context.SaveChangesAsync();

        return Ok(new
        {
            id = user.Id,
            isActive = user.IsActive
        });
    }

    // ─── PATCH /api/users/{id}/password ─────────────────────────────────────
    // Admin resets a user's password to a new temporary value

    [HttpPatch("{id:guid}/password")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ResetUserPassword(
        Guid id,
        ResetUserPasswordRequest request)
    {
        var user = await _context.Users
            .SingleOrDefaultAsync(u => u.Id == id);

        if (user is null)
        {
            return NotFound(new { message = "User not found." });
        }

        user.PasswordHash = _passwordHasher.HashPassword(
            user,
            request.TemporaryPassword
        );

        await _context.SaveChangesAsync();

        return Ok(new { message = "Password reset successfully." });
    }

    private Task<int> CountOtherActiveAdminsAsync(Guid excludeUserId)
    {
        return _context.Users
            .CountAsync(u => u.IsActive && u.Role.Name == "Admin" && u.Id != excludeUserId);
    }
}
