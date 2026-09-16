using ResolveAI.Api.Entities;
using ResolveAI.Api.Enums;

namespace ResolveAI.Api.Services;

public interface INotificationService
{
    Task QueueIncidentCreatedAsync(
        Incident incident,
        Guid actorUserId,
        CancellationToken cancellationToken = default);

    Task QueueIncidentAssignedAsync(
        Incident incident,
        AppUser technician,
        Guid actorUserId,
        bool wasReassignment,
        CancellationToken cancellationToken = default);

    Task QueueIncidentCommentAddedAsync(
        Incident incident,
        AppUser author,
        CancellationToken cancellationToken = default);

    Task QueueIncidentStatusChangedAsync(
        Incident incident,
        IncidentStatus oldStatus,
        IncidentStatus newStatus,
        Guid actorUserId,
        CancellationToken cancellationToken = default);

    Task QueuePriorityChangedAsync(
        Incident incident,
        string previousPriority,
        string newPriority,
        Guid actorUserId,
        CancellationToken cancellationToken = default);

    Task QueueSlaNotificationsForIncidentAsync(
        Incident incident,
        DateTime timestamp,
        CancellationToken cancellationToken = default);

    Task QueueSlaNotificationsForActiveIncidentsAsync(
        DateTime timestamp,
        CancellationToken cancellationToken = default);
}
