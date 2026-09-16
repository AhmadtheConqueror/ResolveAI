using ResolveAI.Api.Enums;

namespace ResolveAI.Api.Entities;

public class Incident
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string IncidentNumber { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public IncidentStatus Status { get; set; } = IncidentStatus.Open;

    public Guid ReporterId { get; set; }

    public AppUser Reporter { get; set; } = null!;

    public Guid? AssignedToId { get; set; }

    public AppUser? AssignedTo { get; set; }

    public Guid CategoryId { get; set; }

    public Category Category { get; set; } = null!;

    public Guid PriorityId { get; set; }

    public Priority Priority { get; set; } = null!;

    public string? Resolution { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? FirstRespondedAt { get; set; }

    public DateTime? ResolvedAt { get; set; }

    public DateTime? ClosedAt { get; set; }

    public ICollection<IncidentComment> Comments { get; set; } =
        new List<IncidentComment>();

    public ICollection<IncidentAIAnalysis> AIAnalyses { get; set; } =
        new List<IncidentAIAnalysis>();
}
