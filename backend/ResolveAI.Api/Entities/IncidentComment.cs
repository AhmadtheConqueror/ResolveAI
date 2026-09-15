namespace ResolveAI.Api.Entities;

public class IncidentComment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid IncidentId { get; set; }

    public Incident Incident { get; set; } = null!;

    public Guid UserId { get; set; }

    public AppUser User { get; set; } = null!;

    public string Comment { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
