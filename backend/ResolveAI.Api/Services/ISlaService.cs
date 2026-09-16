using ResolveAI.Api.Entities;
using ResolveAI.Api.Models.Sla;

namespace ResolveAI.Api.Services;

public interface ISlaService
{
    IncidentSlaDetail CalculateDetail(Incident incident, DateTime? asOf = null);

    IncidentSlaSummary CalculateSummary(Incident incident, DateTime? asOf = null);

    IncidentSlaDetail CalculateDetail(
        DateTime createdAt,
        string? priorityName,
        DateTime? firstRespondedAt,
        DateTime? resolvedAt,
        DateTime? asOf = null);
}
