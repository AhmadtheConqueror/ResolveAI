using ResolveAI.Api.Entities;
using ResolveAI.Api.Models.Sla;

namespace ResolveAI.Api.Services;

public class SlaService : ISlaService
{
    private const double AtRiskThresholdFraction = 0.75;

    private static readonly Dictionary<string, (int ResponseMinutes, int ResolutionMinutes)> SlaPolicyByPriority =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Low"] = (480, 4320),      // 8h response, 72h resolution
            ["Medium"] = (240, 2880),   // 4h response, 48h resolution
            ["High"] = (120, 1440),     // 2h response, 24h resolution
            ["Critical"] = (30, 480),   // 30m response, 8h resolution
        };

    public IncidentSlaDetail CalculateDetail(Incident incident, DateTime? asOf = null)
    {
        return CalculateDetail(
            incident.CreatedAt,
            incident.Priority?.Name,
            incident.FirstRespondedAt,
            incident.ResolvedAt,
            asOf);
    }

    public IncidentSlaSummary CalculateSummary(Incident incident, DateTime? asOf = null)
    {
        var detail = CalculateDetail(incident, asOf);
        return new IncidentSlaSummary
        {
            OverallStatus = detail.OverallStatus,
            ResponseStatus = detail.ResponseStatus,
            ResolutionStatus = detail.ResolutionStatus,
            ResponseDueAt = detail.ResponseDueAt,
            ResolutionDueAt = detail.ResolutionDueAt,
            RequiresEscalation = detail.RequiresEscalation
        };
    }

    public IncidentSlaDetail CalculateDetail(
        DateTime createdAt,
        string? priorityName,
        DateTime? firstRespondedAt,
        DateTime? resolvedAt,
        DateTime? asOf = null)
    {
        var now = asOf ?? DateTime.UtcNow;

        if (string.IsNullOrWhiteSpace(priorityName) ||
            !SlaPolicyByPriority.TryGetValue(priorityName.Trim(), out var policy))
        {
            return new IncidentSlaDetail
            {
                ResponseTargetMinutes = 0,
                ResolutionTargetMinutes = 0,
                ResponseStatus = SlaStatus.NotApplicable.ToString(),
                ResolutionStatus = SlaStatus.NotApplicable.ToString(),
                OverallStatus = SlaStatus.NotApplicable.ToString(),
                RequiresEscalation = false
            };
        }

        var (responseTargetMin, resolutionTargetMin) = policy;
        var responseDueAt = createdAt.AddMinutes(responseTargetMin);
        var resolutionDueAt = createdAt.AddMinutes(resolutionTargetMin);

        // Response SLA status
        SlaStatus responseStatus;
        int? responseRemainingMinutes = null;
        int? responseOverdueMinutes = null;

        if (firstRespondedAt.HasValue)
        {
            if (firstRespondedAt.Value <= responseDueAt)
            {
                responseStatus = SlaStatus.Met;
                responseRemainingMinutes = 0;
                responseOverdueMinutes = 0;
            }
            else
            {
                responseStatus = SlaStatus.Breached;
                responseRemainingMinutes = 0;
                responseOverdueMinutes = (int)Math.Max(1, Math.Round((firstRespondedAt.Value - responseDueAt).TotalMinutes));
            }
        }
        else
        {
            if (now > responseDueAt)
            {
                responseStatus = SlaStatus.Breached;
                responseRemainingMinutes = 0;
                responseOverdueMinutes = (int)Math.Max(1, Math.Round((now - responseDueAt).TotalMinutes));
            }
            else
            {
                var elapsedMinutes = (now - createdAt).TotalMinutes;
                if (elapsedMinutes >= AtRiskThresholdFraction * responseTargetMin)
                {
                    responseStatus = SlaStatus.AtRisk;
                }
                else
                {
                    responseStatus = SlaStatus.OnTrack;
                }

                responseRemainingMinutes = (int)Math.Max(0, Math.Round((responseDueAt - now).TotalMinutes));
                responseOverdueMinutes = 0;
            }
        }

        // Resolution SLA status
        SlaStatus resolutionStatus;
        int? resolutionRemainingMinutes = null;
        int? resolutionOverdueMinutes = null;

        if (resolvedAt.HasValue)
        {
            if (resolvedAt.Value <= resolutionDueAt)
            {
                resolutionStatus = SlaStatus.Met;
                resolutionRemainingMinutes = 0;
                resolutionOverdueMinutes = 0;
            }
            else
            {
                resolutionStatus = SlaStatus.Breached;
                resolutionRemainingMinutes = 0;
                resolutionOverdueMinutes = (int)Math.Max(1, Math.Round((resolvedAt.Value - resolutionDueAt).TotalMinutes));
            }
        }
        else
        {
            if (now > resolutionDueAt)
            {
                resolutionStatus = SlaStatus.Breached;
                resolutionRemainingMinutes = 0;
                resolutionOverdueMinutes = (int)Math.Max(1, Math.Round((now - resolutionDueAt).TotalMinutes));
            }
            else
            {
                var elapsedMinutes = (now - createdAt).TotalMinutes;
                if (elapsedMinutes >= AtRiskThresholdFraction * resolutionTargetMin)
                {
                    resolutionStatus = SlaStatus.AtRisk;
                }
                else
                {
                    resolutionStatus = SlaStatus.OnTrack;
                }

                resolutionRemainingMinutes = (int)Math.Max(0, Math.Round((resolutionDueAt - now).TotalMinutes));
                resolutionOverdueMinutes = 0;
            }
        }

        // Overall SLA status
        SlaStatus overallStatus;
        if (responseStatus == SlaStatus.Breached || resolutionStatus == SlaStatus.Breached)
        {
            overallStatus = SlaStatus.Breached;
        }
        else if (resolvedAt.HasValue && resolutionStatus == SlaStatus.Met)
        {
            overallStatus = SlaStatus.Met;
        }
        else if (responseStatus == SlaStatus.AtRisk || resolutionStatus == SlaStatus.AtRisk)
        {
            overallStatus = SlaStatus.AtRisk;
        }
        else
        {
            overallStatus = SlaStatus.OnTrack;
        }

        var responseBreached = responseStatus == SlaStatus.Breached;
        var resolutionBreached = resolutionStatus == SlaStatus.Breached;
        var requiresEscalation = responseBreached || resolutionBreached;

        return new IncidentSlaDetail
        {
            ResponseTargetMinutes = responseTargetMin,
            ResolutionTargetMinutes = resolutionTargetMin,
            ResponseDueAt = responseDueAt,
            ResolutionDueAt = resolutionDueAt,
            FirstRespondedAt = firstRespondedAt,
            ResolvedAt = resolvedAt,
            ResponseBreached = responseBreached,
            ResolutionBreached = resolutionBreached,
            OverallBreached = responseBreached || resolutionBreached,
            ResponseRemainingMinutes = responseRemainingMinutes,
            ResolutionRemainingMinutes = resolutionRemainingMinutes,
            ResponseOverdueMinutes = responseOverdueMinutes,
            ResolutionOverdueMinutes = resolutionOverdueMinutes,
            ResponseStatus = responseStatus.ToString(),
            ResolutionStatus = resolutionStatus.ToString(),
            OverallStatus = overallStatus.ToString(),
            RequiresEscalation = requiresEscalation
        };
    }
}
