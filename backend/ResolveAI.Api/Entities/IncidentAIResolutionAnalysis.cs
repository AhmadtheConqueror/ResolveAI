namespace ResolveAI.Api.Entities;

public class IncidentAIResolutionAnalysis
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid IncidentId { get; set; }

    public Incident Incident { get; set; } = null!;

    public Guid RequestedByUserId { get; set; }

    public AppUser RequestedByUser { get; set; } = null!;

    public string Summary { get; set; } = string.Empty;

    public string LikelyIssue { get; set; } = string.Empty;

    public string Confidence { get; set; } = "Low";

    public string SuggestedStepsJson { get; set; } = "[]";

    public string EvidenceJson { get; set; } = "[]";

    public string Caveats { get; set; } = string.Empty;

    public bool HasSufficientEvidence { get; set; }

    public int CandidateCount { get; set; }

    public string Provider { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public string PromptVersion { get; set; } = "resolution-assistant-v1";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
