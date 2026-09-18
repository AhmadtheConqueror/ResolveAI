using ResolveAI.Api.Entities;
using ResolveAI.Api.Models.AI;

namespace ResolveAI.Api.Services;

public interface IResolutionEvidenceService
{
    Task<IReadOnlyList<ResolutionEvidenceCandidate>> GetCandidatesAsync(
        Incident currentIncident,
        int maxCandidates = 25,
        CancellationToken cancellationToken = default);
}
