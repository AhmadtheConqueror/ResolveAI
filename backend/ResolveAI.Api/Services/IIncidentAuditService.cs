using ResolveAI.Api.Entities;
using ResolveAI.Api.Enums;
using ResolveAI.Api.Models.Sla;

namespace ResolveAI.Api.Services;

public interface IIncidentAuditService
{
    Task RecordIncidentCreatedAsync(
        Incident incident,
        Guid actorUserId,
        CancellationToken cancellationToken = default);

    Task RecordStatusChangedAsync(
        Incident incident,
        IncidentStatus oldStatus,
        IncidentStatus newStatus,
        Guid actorUserId,
        CancellationToken cancellationToken = default);

    Task RecordStatusChangedAsync(
        Incident incident,
        IncidentStatus oldStatus,
        IncidentStatus newStatus,
        Guid actorUserId,
        string? reason,
        CancellationToken cancellationToken = default);

    Task RecordFirstResponseRecordedAsync(
        Incident incident,
        Guid actorUserId,
        CancellationToken cancellationToken = default);

    Task RecordAssignmentChangedAsync(
        Incident incident,
        Guid actorUserId,
        Guid? oldAssigneeId,
        string? oldAssigneeName,
        Guid? newAssigneeId,
        string? newAssigneeName,
        CancellationToken cancellationToken = default);

    Task RecordCommentAddedAsync(
        Incident incident,
        AppUser author,
        CancellationToken cancellationToken = default);

    Task RecordResolutionRecordedAsync(
        Incident incident,
        Guid actorUserId,
        CancellationToken cancellationToken = default);

    Task RecordAIAnalysisGeneratedAsync(
        Incident incident,
        IncidentAIAnalysis analysis,
        Guid requestedByUserId,
        CancellationToken cancellationToken = default);

    Task RecordAIResolutionAnalysisGeneratedAsync(
        Incident incident,
        IncidentAIResolutionAnalysis analysis,
        Guid requestedByUserId,
        CancellationToken cancellationToken = default);

    Task RecordAIRecommendationAppliedAsync(
        Incident incident,
        IncidentAIAnalysis analysis,
        Guid actorUserId,
        string? oldCategory,
        string? newCategory,
        string? oldPriority,
        string? newPriority,
        CancellationToken cancellationToken = default);

    Task RecordSlaEventAsync(
        Incident incident,
        IncidentSlaDetail sla,
        string dimension,
        string state,
        DateTime timestamp,
        CancellationToken cancellationToken = default);

    Task RecordSlaEventsForActiveIncidentsAsync(
        DateTime timestamp,
        CancellationToken cancellationToken = default);
}
