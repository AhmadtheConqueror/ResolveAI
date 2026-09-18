namespace ResolveAI.Api.Models.AI;

public class ResolutionEvidenceCandidate
{
    public Guid IncidentId { get; set; }

    public string IncidentNumber { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string DescriptionExcerpt { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public string Priority { get; set; } = string.Empty;

    public string ResolutionExcerpt { get; set; } = string.Empty;

    public DateTime? ResolvedAt { get; set; }

    public double CandidateScore { get; set; }
}
