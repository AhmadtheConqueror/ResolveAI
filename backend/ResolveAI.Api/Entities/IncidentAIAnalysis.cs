namespace ResolveAI.Api.Entities;

public class IncidentAIAnalysis
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid IncidentId { get; set; }

    public Incident Incident { get; set; } = null!;

    public string Provider { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public string CategoryRecommendation { get; set; } = string.Empty;

    public string PriorityRecommendation { get; set; } = string.Empty;

    public string Urgency { get; set; } = string.Empty;

    public double Confidence { get; set; }

    public string PossibleCause { get; set; } = string.Empty;

    public string ReasoningSummary { get; set; } = string.Empty;

    public string SuggestedActionsJson { get; set; } = "[]";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Guid RequestedByUserId { get; set; }

    public AppUser RequestedByUser { get; set; } = null!;

    public string PromptVersion { get; set; } = string.Empty;

    public bool CategoryApplied { get; set; } = false;

    public bool PriorityApplied { get; set; } = false;

    public DateTime? AppliedAt { get; set; }

    public Guid? AppliedByUserId { get; set; }

    public AppUser? AppliedByUser { get; set; }
}
