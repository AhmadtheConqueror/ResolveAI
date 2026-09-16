namespace ResolveAI.Api.Models.Sla;

public enum SlaStatus
{
    OnTrack,
    AtRisk,
    Breached,
    Met,
    NotApplicable
}

public class IncidentSlaDetail
{
    public int ResponseTargetMinutes { get; set; }
    public int ResolutionTargetMinutes { get; set; }
    public DateTime? ResponseDueAt { get; set; }
    public DateTime? ResolutionDueAt { get; set; }
    public DateTime? FirstRespondedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public bool ResponseBreached { get; set; }
    public bool ResolutionBreached { get; set; }
    public bool OverallBreached { get; set; }
    public int? ResponseRemainingMinutes { get; set; }
    public int? ResolutionRemainingMinutes { get; set; }
    public int? ResponseOverdueMinutes { get; set; }
    public int? ResolutionOverdueMinutes { get; set; }
    public string ResponseStatus { get; set; } = SlaStatus.OnTrack.ToString();
    public string ResolutionStatus { get; set; } = SlaStatus.OnTrack.ToString();
    public string OverallStatus { get; set; } = SlaStatus.OnTrack.ToString();
    public bool RequiresEscalation { get; set; }
}

public class IncidentSlaSummary
{
    public string OverallStatus { get; set; } = SlaStatus.OnTrack.ToString();
    public string ResponseStatus { get; set; } = SlaStatus.OnTrack.ToString();
    public string ResolutionStatus { get; set; } = SlaStatus.OnTrack.ToString();
    public DateTime? ResponseDueAt { get; set; }
    public DateTime? ResolutionDueAt { get; set; }
    public bool RequiresEscalation { get; set; }
}
