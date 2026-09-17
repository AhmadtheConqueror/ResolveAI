namespace ResolveAI.Api.Enums;

public enum IncidentAuditEventType
{
    IncidentCreated,
    IncidentTriaged,
    IncidentAssigned,
    IncidentReassigned,
    IncidentUnassigned,
    StatusChanged,
    CategoryChanged,
    PriorityChanged,
    FirstResponseRecorded,
    CommentAdded,
    ResolutionRecorded,
    IncidentResolved,
    IncidentClosed,
    SlaAtRisk,
    SlaBreached,
    AIAnalysisGenerated,
    AIRecommendationApplied
}
