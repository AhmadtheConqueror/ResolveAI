namespace ResolveAI.Api.Models.AI;

public class SuggestedResolutionStep
{
    public int StepNumber { get; set; }

    public string Action { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public List<string> EvidenceIncidentNumbers { get; set; } = new();
}

public class SimilarResolvedIncidentEvidence
{
    public Guid IncidentId { get; set; }

    public string IncidentNumber { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public DateTime? ResolvedAt { get; set; }

    public string ResolutionExcerpt { get; set; } = string.Empty;

    public string MatchStrength { get; set; } = "Moderate";

    public string ReasonForMatch { get; set; } = string.Empty;
}

public class AIResolutionAnalysisResult
{
    public string Summary { get; set; } = string.Empty;

    public string LikelyIssue { get; set; } = string.Empty;

    public string Confidence { get; set; } = "Low";

    public List<SuggestedResolutionStep> SuggestedSteps { get; set; } = new();

    public List<SimilarResolvedIncidentEvidence> Evidence { get; set; } = new();

    public string Caveats { get; set; } = string.Empty;

    public bool HasSufficientEvidence { get; set; }

    public int CandidateCount { get; set; }

    public string Provider { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public string PromptVersion { get; set; } = "resolution-assistant-v1";
}

public class AIResolutionAnalysisResponse
{
    public Guid Id { get; set; }

    public Guid IncidentId { get; set; }

    public string Summary { get; set; } = string.Empty;

    public string LikelyIssue { get; set; } = string.Empty;

    public string Confidence { get; set; } = "Low";

    public IReadOnlyList<SuggestedResolutionStep> SuggestedSteps { get; set; } = Array.Empty<SuggestedResolutionStep>();

    public IReadOnlyList<SimilarResolvedIncidentEvidence> Evidence { get; set; } = Array.Empty<SimilarResolvedIncidentEvidence>();

    public string Caveats { get; set; } = string.Empty;

    public bool HasSufficientEvidence { get; set; }

    public int CandidateCount { get; set; }

    public string Provider { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public string PromptVersion { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public Guid RequestedByUserId { get; set; }

    public AIRequestedByUserResponse? RequestedByUser { get; set; }
}

public class AIRequestedByUserResponse
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;
}
