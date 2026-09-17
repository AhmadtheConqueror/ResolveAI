using ResolveAI.Api.Enums;

namespace ResolveAI.Api.Entities;

public class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public AppUser User { get; set; } = null!;

    public NotificationType Type { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public Guid? IncidentId { get; set; }

    public Incident? Incident { get; set; }

    public bool IsRead { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ReadAt { get; set; }

    public Guid? ActorUserId { get; set; }

    public AppUser? ActorUser { get; set; }

    public string? DeduplicationKey { get; set; }

    /// <summary>Snapshot of the incident title at notification creation time.</summary>
    public string? IncidentTitle { get; set; }

    /// <summary>Snapshot of the incident number at notification creation time.</summary>
    public string? IncidentNumber { get; set; }
}
