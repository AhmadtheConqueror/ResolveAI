using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ResolveAI.Api.Data;

namespace ResolveAI.Api.Controllers;

[ApiController]
[Route("api/admin/external-notifications")]
[Authorize(Roles = "Admin")]
public class ExternalNotificationsController : ControllerBase
{
    private readonly AppDbContext _context;

    public ExternalNotificationsController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetDeliveries([FromQuery] int limit = 50)
    {
        var clampedLimit = Math.Clamp(limit, 1, 100);

        var deliveries = await _context.ExternalNotificationDeliveries
            .AsNoTracking()
            .Include(d => d.Notification)
            .OrderByDescending(d => d.CreatedAt)
            .Take(clampedLimit)
            .Select(d => new
            {
                id = d.Id,
                notificationId = d.NotificationId,
                notificationTitle = d.Notification.Title,
                incidentId = d.Notification.IncidentId,
                incidentNumber = d.Notification.IncidentNumber,
                recipientAddress = d.RecipientAddress,
                channel = d.Channel.ToString(),
                provider = d.Provider,
                status = d.Status.ToString(),
                attemptCount = d.AttemptCount,
                lastAttemptAt = d.LastAttemptAt,
                sentAt = d.SentAt,
                providerMessageId = d.ProviderMessageId,
                lastError = d.LastError,
                idempotencyKey = d.IdempotencyKey,
                createdAt = d.CreatedAt,
                updatedAt = d.UpdatedAt
            })
            .ToListAsync();

        return Ok(deliveries);
    }
}
