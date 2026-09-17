using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ResolveAI.Api.Data;

namespace ResolveAI.Api.Middleware;

/// <summary>
/// Centralized middleware that validates the authenticated user against the database.
/// Rejects requests if the user was deactivated or deleted.
/// Synchronizes role claims if the user's role changed in the database since the token was issued.
/// </summary>
public class UserValidationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<UserValidationMiddleware> _logger;

    public UserValidationMiddleware(
        RequestDelegate next,
        ILogger<UserValidationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, AppDbContext dbContext)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var userIdClaim = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(userIdClaim, out var userId))
            {
                _logger.LogWarning("Authenticated request rejected: invalid NameIdentifier claim.");
                await WriteUnauthorizedResponseAsync(context, "Invalid authentication token.");
                return;
            }

            var user = await dbContext.Users
                .AsNoTracking()
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null || !user.IsActive)
            {
                _logger.LogWarning("Authenticated request rejected: user {UserId} does not exist or is inactive.", userId);
                await WriteUnauthorizedResponseAsync(context, "User account is inactive or no longer exists.");
                return;
            }

            // If the user's database role differs from the JWT claim, update the principal's role claim
            // so stale privilege claims from an old JWT are never trusted.
            var tokenRole = context.User.FindFirstValue(ClaimTypes.Role);
            if (!string.Equals(tokenRole, user.Role.Name, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation(
                    "User {UserId} role claim updated from {TokenRole} to {DbRole} to reflect database truth.",
                    userId,
                    tokenRole,
                    user.Role.Name);

                if (context.User.Identity is ClaimsIdentity identity)
                {
                    var existingRoleClaims = identity.FindAll(ClaimTypes.Role).ToList();
                    foreach (var claim in existingRoleClaims)
                    {
                        identity.RemoveClaim(claim);
                    }
                    identity.AddClaim(new Claim(ClaimTypes.Role, user.Role.Name));
                }
            }
        }

        await _next(context);
    }

    private static async Task WriteUnauthorizedResponseAsync(HttpContext context, string detail)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/problem+json";

        var problemDetails = new
        {
            type = "https://tools.ietf.org/html/rfc9110#section-15.5.2",
            title = "Unauthorized",
            status = StatusCodes.Status401Unauthorized,
            detail,
            traceId = context.TraceIdentifier
        };

        await context.Response.WriteAsJsonAsync(problemDetails);
    }
}
