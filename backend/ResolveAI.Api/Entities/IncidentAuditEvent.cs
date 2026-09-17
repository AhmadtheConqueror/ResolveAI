using ResolveAI.Api.Enums;

namespace ResolveAI.Api.Entities;

public class IncidentAuditEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid IncidentId { get; set; }

    public Incident Incident { get; set; } = null!;

    public IncidentAuditEventType EventType { get; set; }

    public Guid? ActorUserId { get; set; }

    public AppUser? ActorUser { get; set; }

    public string? ActorDisplayName { get; set; }

    public IncidentAuditActorType ActorType { get; set; }

    public string Summary { get; set; } = string.Empty;

    public string? OldValue { get; set; }

    public string? NewValue { get; set; }

    public string? Metadata { get; set; }

    public string? DeduplicationKey { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
