using ResolveAI.Api.Entities;

namespace ResolveAI.Api.Services;

public interface IAIIncidentService
{
    Task<AIIncidentAnalysisResult> AnalyzeIncidentAsync(
        Incident incident,
        CancellationToken cancellationToken = default);
}
