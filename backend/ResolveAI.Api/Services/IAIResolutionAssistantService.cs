using ResolveAI.Api.Entities;
using ResolveAI.Api.Models.AI;

namespace ResolveAI.Api.Services;

public interface IAIResolutionAssistantService
{
    Task<AIResolutionAnalysisResult> GenerateResolutionAssistanceAsync(
        Incident incident,
        IReadOnlyList<ResolutionEvidenceCandidate> candidates,
        CancellationToken cancellationToken = default);
}
