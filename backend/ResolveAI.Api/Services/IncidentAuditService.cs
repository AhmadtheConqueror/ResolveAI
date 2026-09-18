using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ResolveAI.Api.Data;
using ResolveAI.Api.Entities;
using ResolveAI.Api.Enums;
using ResolveAI.Api.Models.Sla;

namespace ResolveAI.Api.Services;

public class IncidentAuditService : IIncidentAuditService
{
    private const string SystemActorName = "System";
    private const string AIActorName = "ResolveAI AI";

    private static readonly JsonSerializerOptions MetadataJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly AppDbContext _context;
    private readonly ISlaService _slaService;

    public IncidentAuditService(
        AppDbContext context,
        ISlaService slaService)
    {
        _context = context;
        _slaService = slaService;
    }

    public async Task RecordIncidentCreatedAsync(
        Incident incident,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        await AddUserEventAsync(
            incident.Id,
            IncidentAuditEventType.IncidentCreated,
            "Incident created",
            actorUserId,
            null,
            null,
            null,
            null,
            incident.CreatedAt,
            cancellationToken);
    }

    public Task RecordStatusChangedAsync(
        Incident incident,
        IncidentStatus oldStatus,
        IncidentStatus newStatus,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        return RecordStatusChangedAsync(
            incident,
            oldStatus,
            newStatus,
            actorUserId,
            reason: null,
            cancellationToken);
    }

    public async Task RecordStatusChangedAsync(
        Incident incident,
        IncidentStatus oldStatus,
        IncidentStatus newStatus,
        Guid actorUserId,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        if (oldStatus == newStatus)
        {
            return;
        }

        var actorName = await GetUserDisplayNameAsync(
            actorUserId,
            cancellationToken);

        var isAdministrativeClosure = newStatus == IncidentStatus.Closed &&
            oldStatus != IncidentStatus.Resolved;

        var (eventType, summary) = (oldStatus, newStatus) switch
        {
            (IncidentStatus.Assigned, IncidentStatus.InProgress) =>
                (IncidentAuditEventType.StatusChanged, $"{actorName} started work"),
            (IncidentStatus.WaitingForUser, IncidentStatus.InProgress) =>
                (IncidentAuditEventType.StatusChanged, $"{actorName} resumed work"),
            (_, IncidentStatus.WaitingForUser) =>
                (IncidentAuditEventType.StatusChanged, $"{actorName} marked waiting for user"),
            (IncidentStatus.Open, IncidentStatus.Triaged) =>
                (IncidentAuditEventType.IncidentTriaged, $"{actorName} triaged the incident"),
            (_, IncidentStatus.Resolved) =>
                (IncidentAuditEventType.IncidentResolved, $"{actorName} resolved the incident"),
            (IncidentStatus.Resolved, IncidentStatus.Closed) =>
                (IncidentAuditEventType.IncidentClosed, $"{actorName} closed the resolved incident"),
            _ when isAdministrativeClosure =>
                (IncidentAuditEventType.IncidentClosed, $"{actorName} administratively closed the incident"),
            _ =>
                (IncidentAuditEventType.StatusChanged, $"{actorName} changed status to {FormatStatus(newStatus)}")
        };

        string? metadata = null;
        if (isAdministrativeClosure && !string.IsNullOrWhiteSpace(reason))
        {
            metadata = SerializeMetadata(new
            {
                reason = reason.Trim(),
                isAdministrativeClosure = true
            });
        }

        await AddUserEventAsync(
            incident.Id,
            eventType,
            summary,
            actorUserId,
            FormatStatus(oldStatus),
            FormatStatus(newStatus),
            metadata,
            null,
            incident.UpdatedAt,
            cancellationToken);
    }

    public async Task RecordFirstResponseRecordedAsync(
        Incident incident,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        await AddUserEventAsync(
            incident.Id,
            IncidentAuditEventType.FirstResponseRecorded,
            "First response recorded",
            actorUserId,
            null,
            FormatTimestamp(incident.FirstRespondedAt),
            null,
            null,
            incident.FirstRespondedAt ?? DateTime.UtcNow,
            cancellationToken);
    }

    public async Task RecordAssignmentChangedAsync(
        Incident incident,
        Guid actorUserId,
        Guid? oldAssigneeId,
        string? oldAssigneeName,
        Guid? newAssigneeId,
        string? newAssigneeName,
        CancellationToken cancellationToken = default)
    {
        if (oldAssigneeId == newAssigneeId)
        {
            return;
        }

        var oldValue = string.IsNullOrWhiteSpace(oldAssigneeName)
            ? "Unassigned"
            : oldAssigneeName.Trim();
        var newValue = string.IsNullOrWhiteSpace(newAssigneeName)
            ? "Unassigned"
            : newAssigneeName.Trim();

        var eventType = newAssigneeId is null
            ? IncidentAuditEventType.IncidentUnassigned
            : oldAssigneeId.HasValue
                ? IncidentAuditEventType.IncidentReassigned
                : IncidentAuditEventType.IncidentAssigned;

        var summary = eventType switch
        {
            IncidentAuditEventType.IncidentUnassigned => "Incident unassigned",
            IncidentAuditEventType.IncidentReassigned => $"Reassigned to {newValue}",
            _ => $"Assigned to {newValue}"
        };

        var metadata = new
        {
            oldAssigneeId,
            newAssigneeId
        };

        await AddUserEventAsync(
            incident.Id,
            eventType,
            summary,
            actorUserId,
            oldValue,
            newValue,
            SerializeMetadata(metadata),
            null,
            incident.UpdatedAt,
            cancellationToken);
    }

    public Task RecordCommentAddedAsync(
        Incident incident,
        AppUser author,
        CancellationToken cancellationToken = default)
    {
        var authorName = GetDisplayName(author);

        AddEvent(
            incident.Id,
            IncidentAuditEventType.CommentAdded,
            $"{authorName} added a comment",
            IncidentAuditActorType.User,
            author.Id,
            authorName,
            null,
            null,
            null,
            null,
            incident.UpdatedAt);

        return Task.CompletedTask;
    }

    public async Task RecordResolutionRecordedAsync(
        Incident incident,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        await AddUserEventAsync(
            incident.Id,
            IncidentAuditEventType.ResolutionRecorded,
            "Resolution recorded",
            actorUserId,
            null,
            "Recorded",
            null,
            null,
            incident.UpdatedAt,
            cancellationToken);
    }

    public async Task RecordAIAnalysisGeneratedAsync(
        Incident incident,
        IncidentAIAnalysis analysis,
        Guid requestedByUserId,
        CancellationToken cancellationToken = default)
    {
        var requestedByName = await GetUserDisplayNameAsync(
            requestedByUserId,
            cancellationToken);

        var metadata = new
        {
            analysisId = analysis.Id,
            provider = analysis.Provider,
            model = analysis.Model,
            requestedByUserId,
            requestedByName
        };

        AddEvent(
            incident.Id,
            IncidentAuditEventType.AIAnalysisGenerated,
            "AI analysis generated",
            IncidentAuditActorType.AI,
            null,
            AIActorName,
            null,
            null,
            SerializeMetadata(metadata),
            null,
            analysis.CreatedAt);
    }

    public async Task RecordAIResolutionAnalysisGeneratedAsync(
        Incident incident,
        IncidentAIResolutionAnalysis analysis,
        Guid requestedByUserId,
        CancellationToken cancellationToken = default)
    {
        var requestedByName = await GetUserDisplayNameAsync(
            requestedByUserId,
            cancellationToken);

        var metadata = new
        {
            analysisId = analysis.Id,
            analysisType = "ResolutionAssistant",
            provider = analysis.Provider,
            model = analysis.Model,
            hasSufficientEvidence = analysis.HasSufficientEvidence,
            candidateCount = analysis.CandidateCount,
            confidence = analysis.Confidence,
            requestedByUserId,
            requestedByName
        };

        AddEvent(
            incident.Id,
            IncidentAuditEventType.AIAnalysisGenerated,
            "AI resolution assistance generated",
            IncidentAuditActorType.AI,
            null,
            AIActorName,
            null,
            null,
            SerializeMetadata(metadata),
            null,
            analysis.CreatedAt);
    }

    public async Task RecordAIRecommendationAppliedAsync(
        Incident incident,
        IncidentAIAnalysis analysis,
        Guid actorUserId,
        string? oldCategory,
        string? newCategory,
        string? oldPriority,
        string? newPriority,
        CancellationToken cancellationToken = default)
    {
        var changes = new List<AuditChange>();

        if (!ValuesEqual(oldCategory, newCategory))
        {
            changes.Add(new AuditChange("Category", oldCategory, newCategory));
        }

        if (!ValuesEqual(oldPriority, newPriority))
        {
            changes.Add(new AuditChange("Priority", oldPriority, newPriority));
        }

        var metadata = new
        {
            analysisId = analysis.Id,
            provider = analysis.Provider,
            model = analysis.Model,
            changes
        };

        var oldValue = changes.Count == 1
            ? changes[0].OldValue
            : null;
        var newValue = changes.Count == 1
            ? changes[0].NewValue
            : null;

        await AddUserEventAsync(
            incident.Id,
            IncidentAuditEventType.AIRecommendationApplied,
            "AI recommendation applied",
            actorUserId,
            oldValue,
            newValue,
            SerializeMetadata(metadata),
            null,
            analysis.AppliedAt ?? incident.UpdatedAt,
            cancellationToken);
    }

    public async Task RecordSlaEventAsync(
        Incident incident,
        IncidentSlaDetail sla,
        string dimension,
        string state,
        DateTime timestamp,
        CancellationToken cancellationToken = default)
    {
        var normalizedDimension = dimension.Trim().ToLowerInvariant();
        var normalizedState = state.Trim().ToLowerInvariant();

        if (normalizedDimension is not ("response" or "resolution") ||
            normalizedState is not ("atrisk" or "breached"))
        {
            return;
        }

        var deduplicationKey =
            $"audit:sla:{incident.Id}:{normalizedDimension}:{normalizedState}";

        if (await AuditEventExistsAsync(deduplicationKey, cancellationToken))
        {
            return;
        }

        var isResponse = normalizedDimension == "response";
        var eventType = normalizedState == "breached"
            ? IncidentAuditEventType.SlaBreached
            : IncidentAuditEventType.SlaAtRisk;
        var summary =
            $"{(isResponse ? "Response" : "Resolution")} SLA {(normalizedState == "breached" ? "breached" : "at risk")}";

        var metadata = new
        {
            dimension = normalizedDimension,
            state = normalizedState,
            targetMinutes = isResponse
                ? sla.ResponseTargetMinutes
                : sla.ResolutionTargetMinutes,
            dueAt = isResponse
                ? sla.ResponseDueAt
                : sla.ResolutionDueAt,
            remainingMinutes = isResponse
                ? sla.ResponseRemainingMinutes
                : sla.ResolutionRemainingMinutes,
            overdueMinutes = isResponse
                ? sla.ResponseOverdueMinutes
                : sla.ResolutionOverdueMinutes
        };

        AddEvent(
            incident.Id,
            eventType,
            summary,
            IncidentAuditActorType.System,
            null,
            SystemActorName,
            null,
            null,
            SerializeMetadata(metadata),
            deduplicationKey,
            timestamp);
    }

    public async Task RecordSlaEventsForActiveIncidentsAsync(
        DateTime timestamp,
        CancellationToken cancellationToken = default)
    {
        var activeIncidents = await _context.Incidents
            .Include(i => i.Priority)
            .Where(i =>
                i.Status != IncidentStatus.Resolved &&
                i.Status != IncidentStatus.Closed)
            .ToListAsync(cancellationToken);

        foreach (var incident in activeIncidents)
        {
            var sla = _slaService.CalculateDetail(incident, timestamp);

            if (sla.ResponseStatus == SlaStatus.AtRisk.ToString())
            {
                await RecordSlaEventAsync(
                    incident,
                    sla,
                    "response",
                    "atrisk",
                    timestamp,
                    cancellationToken);
            }

            if (sla.ResponseStatus == SlaStatus.Breached.ToString())
            {
                await RecordSlaEventAsync(
                    incident,
                    sla,
                    "response",
                    "breached",
                    timestamp,
                    cancellationToken);
            }

            if (sla.ResolutionStatus == SlaStatus.AtRisk.ToString())
            {
                await RecordSlaEventAsync(
                    incident,
                    sla,
                    "resolution",
                    "atrisk",
                    timestamp,
                    cancellationToken);
            }

            if (sla.ResolutionStatus == SlaStatus.Breached.ToString())
            {
                await RecordSlaEventAsync(
                    incident,
                    sla,
                    "resolution",
                    "breached",
                    timestamp,
                    cancellationToken);
            }
        }
    }

    private async Task AddUserEventAsync(
        Guid incidentId,
        IncidentAuditEventType eventType,
        string summary,
        Guid actorUserId,
        string? oldValue,
        string? newValue,
        string? metadata,
        string? deduplicationKey,
        DateTime createdAt,
        CancellationToken cancellationToken)
    {
        var actorName = await GetUserDisplayNameAsync(
            actorUserId,
            cancellationToken);

        AddEvent(
            incidentId,
            eventType,
            summary,
            IncidentAuditActorType.User,
            actorUserId,
            actorName,
            oldValue,
            newValue,
            metadata,
            deduplicationKey,
            createdAt);
    }

    private void AddEvent(
        Guid incidentId,
        IncidentAuditEventType eventType,
        string summary,
        IncidentAuditActorType actorType,
        Guid? actorUserId,
        string? actorDisplayName,
        string? oldValue,
        string? newValue,
        string? metadata,
        string? deduplicationKey,
        DateTime createdAt)
    {
        _context.IncidentAuditEvents.Add(new IncidentAuditEvent
        {
            IncidentId = incidentId,
            EventType = eventType,
            ActorType = actorType,
            ActorUserId = actorUserId,
            ActorDisplayName = actorDisplayName,
            Summary = summary,
            OldValue = oldValue,
            NewValue = newValue,
            Metadata = metadata,
            DeduplicationKey = deduplicationKey,
            CreatedAt = createdAt
        });
    }

    private async Task<bool> AuditEventExistsAsync(
        string deduplicationKey,
        CancellationToken cancellationToken)
    {
        var alreadyTracked = _context.ChangeTracker
            .Entries<IncidentAuditEvent>()
            .Any(entry =>
                entry.Entity.DeduplicationKey == deduplicationKey);

        if (alreadyTracked)
        {
            return true;
        }

        return await _context.IncidentAuditEvents
            .AsNoTracking()
            .AnyAsync(
                e => e.DeduplicationKey == deduplicationKey,
                cancellationToken);
    }

    private async Task<string> GetUserDisplayNameAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var user = await _context.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new
            {
                u.FirstName,
                u.LastName,
                u.Email
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (user is null)
        {
            return "Unknown user";
        }

        var displayName = $"{user.FirstName} {user.LastName}".Trim();
        return string.IsNullOrWhiteSpace(displayName)
            ? user.Email
            : displayName;
    }

    private static string GetDisplayName(AppUser user)
    {
        var displayName = $"{user.FirstName} {user.LastName}".Trim();
        return string.IsNullOrWhiteSpace(displayName)
            ? user.Email
            : displayName;
    }

    private static string SerializeMetadata(object metadata)
    {
        return JsonSerializer.Serialize(metadata, MetadataJsonOptions);
    }

    private static string FormatStatus(IncidentStatus status)
    {
        return status switch
        {
            IncidentStatus.InProgress => "In Progress",
            IncidentStatus.WaitingForUser => "Waiting for User",
            _ => status.ToString()
        };
    }

    private static string? FormatTimestamp(DateTime? timestamp)
    {
        return timestamp?.ToString("O");
    }

    private static bool ValuesEqual(string? left, string? right)
    {
        return string.Equals(
            left?.Trim(),
            right?.Trim(),
            StringComparison.OrdinalIgnoreCase);
    }

    private sealed record AuditChange(
        string Field,
        string? OldValue,
        string? NewValue);
}
