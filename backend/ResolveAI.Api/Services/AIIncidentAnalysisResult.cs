namespace ResolveAI.Api.Services;

public class AIIncidentAnalysisResult
{
    public string Provider { get; init; } = string.Empty;

    public string Model { get; init; } = string.Empty;

    public string CategoryRecommendation { get; init; } = string.Empty;

    public string PriorityRecommendation { get; init; } = string.Empty;

    public string Urgency { get; init; } = string.Empty;

    public double Confidence { get; init; }

    public string PossibleCause { get; init; } = string.Empty;

    public string ReasoningSummary { get; init; } = string.Empty;

    public IReadOnlyList<string> SuggestedActions { get; init; } =
        Array.Empty<string>();

    public string PromptVersion { get; init; } = string.Empty;
}
