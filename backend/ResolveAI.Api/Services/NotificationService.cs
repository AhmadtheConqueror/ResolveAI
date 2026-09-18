using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ResolveAI.Api.Data;
using ResolveAI.Api.Entities;
using ResolveAI.Api.Enums;
using ResolveAI.Api.Models.Sla;

namespace ResolveAI.Api.Services;

public class NotificationService : INotificationService
{
    private readonly AppDbContext _context;
    private readonly ISlaService _slaService;
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly ILogger<NotificationService>? _logger;
    private readonly List<ExternalDeliveryDraft> _pendingDeliveries = new();

    public NotificationService(
        AppDbContext context,
        ISlaService slaService,
        IServiceScopeFactory? scopeFactory = null,
        ILogger<NotificationService>? logger = null)
    {
        _context = context;
        _slaService = slaService;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task QueueIncidentCreatedAsync(
        Incident incident,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var recipients = await GetActiveManagersOrAdminFallbackAsync(
            actorUserId,
            cancellationToken);

        foreach (var recipientId in recipients)
        {
            await QueueNotificationAsync(
                recipientId,
                NotificationType.IncidentCreated,
                "New incident requires triage",
                $"{incident.IncidentNumber} · submitted and awaiting triage.",
                incident.Id,
                incident.Title,
                incident.IncidentNumber,
                actorUserId,
                null,
                cancellationToken,
                isEmailEligible: true);
        }
    }

    public Task QueueIncidentAssignedAsync(
        Incident incident,
        AppUser technician,
        Guid actorUserId,
        bool wasReassignment,
        CancellationToken cancellationToken = default)
    {
        return QueueIncidentAssignedAsync(
            incident,
            technician,
            actorUserId,
            wasReassignment,
            previousAssigneeName: null,
            cancellationToken);
    }

    public async Task QueueIncidentAssignedAsync(
        Incident incident,
        AppUser technician,
        Guid actorUserId,
        bool wasReassignment,
        string? previousAssigneeName,
        CancellationToken cancellationToken = default)
    {
        var type = wasReassignment
            ? NotificationType.IncidentReassigned
            : NotificationType.IncidentAssigned;

        var technicianTitle = wasReassignment
            ? "Incident reassigned to you"
            : "Incident assigned to you";

        var newAssigneeName = GetDisplayName(technician);

        var techMessage = wasReassignment
            ? (!string.IsNullOrWhiteSpace(previousAssigneeName)
                ? $"Incident {incident.IncidentNumber} has been reassigned to you. Previous assignee: {previousAssigneeName}."
                : $"Incident {incident.IncidentNumber} has been reassigned to you.")
            : $"Incident {incident.IncidentNumber} has been assigned to you.";

        await QueueNotificationAsync(
            technician.Id,
            type,
            technicianTitle,
            techMessage,
            incident.Id,
            incident.Title,
            incident.IncidentNumber,
            actorUserId,
            null,
            cancellationToken,
            isEmailEligible: true);

        var reporterTitle = wasReassignment
            ? "Your incident has been reassigned"
            : "Your incident has been assigned";

        var reporterMessage = wasReassignment
            ? (!string.IsNullOrWhiteSpace(previousAssigneeName)
                ? $"Incident {incident.IncidentNumber} has been reassigned to {newAssigneeName}. Previous assignee: {previousAssigneeName}."
                : $"Incident {incident.IncidentNumber} has been reassigned to {newAssigneeName}.")
            : $"Incident {incident.IncidentNumber} has been assigned to {newAssigneeName}.";

        await QueueNotificationAsync(
            incident.ReporterId,
            type,
            reporterTitle,
            reporterMessage,
            incident.Id,
            incident.Title,
            incident.IncidentNumber,
            actorUserId,
            null,
            cancellationToken,
            isEmailEligible: true);
    }

    public async Task QueueIncidentCommentAddedAsync(
        Incident incident,
        AppUser author,
        string commentExcerpt,
        CancellationToken cancellationToken = default)
    {
        var authorRole = author.Role?.Name;
        var authorName = GetDisplayName(author);
        var excerpt = TruncateExcerpt(commentExcerpt, 100);

        if (authorRole == "Employee")
        {
            if (incident.AssignedToId.HasValue)
            {
                await QueueNotificationAsync(
                    incident.AssignedToId.Value,
                    NotificationType.IncidentCommentAdded,
                    "New reply from reporter",
                    $"{incident.IncidentNumber} · {authorName}: {excerpt}",
                    incident.Id,
                    incident.Title,
                    incident.IncidentNumber,
                    author.Id,
                    null,
                    cancellationToken,
                    isEmailEligible: true);
            }
            else if (IncidentRequiresManagementAttention(incident))
            {
                var managerRecipients =
                    await GetActiveManagersOrAdminFallbackAsync(
                        author.Id,
                        cancellationToken);

                foreach (var recipientId in managerRecipients)
                {
                    await QueueNotificationAsync(
                        recipientId,
                        NotificationType.IncidentCommentAdded,
                        "Reporter replied on unassigned incident",
                        $"{incident.IncidentNumber} · {authorName}: {excerpt}",
                        incident.Id,
                        incident.Title,
                        incident.IncidentNumber,
                        author.Id,
                        null,
                        cancellationToken,
                        isEmailEligible: true);
                }
            }

            return;
        }

        if (authorRole == "Technician")
        {
            await QueueNotificationAsync(
                incident.ReporterId,
                NotificationType.IncidentCommentAdded,
                "New reply on your incident",
                $"{incident.IncidentNumber} · {authorName}: {excerpt}",
                incident.Id,
                incident.Title,
                incident.IncidentNumber,
                author.Id,
                null,
                cancellationToken,
                isEmailEligible: true);

            return;
        }

        if (authorRole is "Manager" or "Admin")
        {
            await QueueNotificationAsync(
                incident.ReporterId,
                NotificationType.IncidentCommentAdded,
                "New reply on your incident",
                $"{incident.IncidentNumber} · {authorName}: {excerpt}",
                incident.Id,
                incident.Title,
                incident.IncidentNumber,
                author.Id,
                null,
                cancellationToken,
                isEmailEligible: true);

            if (incident.AssignedToId.HasValue)
            {
                await QueueNotificationAsync(
                    incident.AssignedToId.Value,
                    NotificationType.IncidentCommentAdded,
                    "Manager update on your incident",
                    $"{incident.IncidentNumber} · {authorName}: {excerpt}",
                    incident.Id,
                    incident.Title,
                    incident.IncidentNumber,
                    author.Id,
                    null,
                    cancellationToken,
                    isEmailEligible: false);
            }
        }
    }

    public async Task QueueIncidentStatusChangedAsync(
        Incident incident,
        IncidentStatus oldStatus,
        IncidentStatus newStatus,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var actorName = await GetActorDisplayNameAsync(
            actorUserId,
            cancellationToken);

        if (newStatus == IncidentStatus.Resolved)
        {
            await QueueNotificationAsync(
                incident.ReporterId,
                NotificationType.IncidentResolved,
                "Incident resolved",
                $"{incident.IncidentNumber} · marked resolved by {actorName}. Please review and confirm closure.",
                incident.Id,
                incident.Title,
                incident.IncidentNumber,
                actorUserId,
                null,
                cancellationToken,
                isEmailEligible: true);

            return;
        }

        if (newStatus == IncidentStatus.Closed)
        {
            var isAdministrativeClosure = oldStatus != IncidentStatus.Resolved;
            var closureTitle = isAdministrativeClosure
                ? "Incident administratively closed"
                : "Incident closed";
            var closureMessage = isAdministrativeClosure
                ? $"{incident.IncidentNumber} · administratively closed by {actorName}."
                : $"{incident.IncidentNumber} · closed by {actorName}.";

            await QueueNotificationAsync(
                incident.ReporterId,
                NotificationType.IncidentClosed,
                closureTitle,
                closureMessage,
                incident.Id,
                incident.Title,
                incident.IncidentNumber,
                actorUserId,
                null,
                cancellationToken,
                isEmailEligible: true);

            if (incident.AssignedToId.HasValue)
            {
                await QueueNotificationAsync(
                    incident.AssignedToId.Value,
                    NotificationType.IncidentClosed,
                    closureTitle,
                    closureMessage,
                    incident.Id,
                    incident.Title,
                    incident.IncidentNumber,
                    actorUserId,
                    null,
                    cancellationToken,
                    isEmailEligible: false);
            }

            return;
        }

        var statusTitle = GetStatusChangeTitle(oldStatus, newStatus);

        await QueueNotificationAsync(
            incident.ReporterId,
            NotificationType.IncidentStatusChanged,
            statusTitle,
            $"{incident.IncidentNumber} · status changed from {FormatStatus(oldStatus)} to {FormatStatus(newStatus)} by {actorName}.",
            incident.Id,
            incident.Title,
            incident.IncidentNumber,
            actorUserId,
            null,
            cancellationToken,
            isEmailEligible: false);

        if (incident.AssignedToId.HasValue)
        {
            await QueueNotificationAsync(
                incident.AssignedToId.Value,
                NotificationType.IncidentStatusChanged,
                statusTitle,
                $"{incident.IncidentNumber} · status changed from {FormatStatus(oldStatus)} to {FormatStatus(newStatus)} by {actorName}.",
                incident.Id,
                incident.Title,
                incident.IncidentNumber,
                actorUserId,
                null,
                cancellationToken,
                isEmailEligible: false);
        }
    }

    public static string GetStatusChangeTitle(IncidentStatus oldStatus, IncidentStatus newStatus) =>
        (oldStatus, newStatus) switch
        {
            (IncidentStatus.Assigned, IncidentStatus.InProgress) => "Incident moved to In Progress",
            (IncidentStatus.WaitingForUser, IncidentStatus.InProgress) => "Incident work resumed",
            (IncidentStatus.InProgress, IncidentStatus.WaitingForUser) => "Incident waiting for user",
            (IncidentStatus.Open, IncidentStatus.Triaged) => "Incident triaged",
            (_, IncidentStatus.Resolved) => "Incident resolved",
            (IncidentStatus.Resolved, IncidentStatus.Closed) => "Incident closed",
            (_, IncidentStatus.Closed) => "Incident administratively closed",
            (_, IncidentStatus.InProgress) => "Incident moved to In Progress",
            (_, IncidentStatus.WaitingForUser) => "Incident waiting for user",
            (_, IncidentStatus.Triaged) => "Incident triaged",
            _ => "Incident status updated"
        };

    public async Task QueuePriorityChangedAsync(
        Incident incident,
        string previousPriority,
        string newPriority,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var actorName = await GetActorDisplayNameAsync(
            actorUserId,
            cancellationToken);

        var recipients = new List<Guid?> { incident.ReporterId };

        if (incident.AssignedToId.HasValue)
        {
            recipients.Add(incident.AssignedToId.Value);
        }

        foreach (var recipientId in await FilterActiveRecipientIdsAsync(
                     recipients,
                     actorUserId,
                     cancellationToken))
        {
            await QueueNotificationAsync(
                recipientId,
                NotificationType.PriorityChanged,
                "Incident priority changed",
                $"{incident.IncidentNumber} · priority changed from {previousPriority} to {newPriority} by {actorName}.",
                incident.Id,
                incident.Title,
                incident.IncidentNumber,
                actorUserId,
                null,
                cancellationToken,
                isEmailEligible: false);
        }
    }

    public async Task QueueSlaNotificationsForIncidentAsync(
        Incident incident,
        DateTime timestamp,
        CancellationToken cancellationToken = default)
    {
        if (incident.Status is IncidentStatus.Resolved or IncidentStatus.Closed)
        {
            return;
        }

        var sla = _slaService.CalculateDetail(incident, timestamp);

        if (sla.ResponseStatus == SlaStatus.AtRisk.ToString())
        {
            await QueueSlaEventAsync(
                incident,
                NotificationType.SlaAtRisk,
                "Response SLA at risk",
                $"{incident.IncidentNumber} · response SLA is at risk{FormatRemaining(sla.ResponseRemainingMinutes)}.",
                "response:atrisk",
                cancellationToken);
        }

        if (sla.ResponseStatus == SlaStatus.Breached.ToString())
        {
            await QueueSlaEventAsync(
                incident,
                NotificationType.SlaBreached,
                "Response SLA breached",
                $"{incident.IncidentNumber} · response SLA has breached{FormatOverdue(sla.ResponseOverdueMinutes)}.",
                "response:breached",
                cancellationToken);
        }

        if (sla.ResolutionStatus == SlaStatus.AtRisk.ToString())
        {
            await QueueSlaEventAsync(
                incident,
                NotificationType.SlaAtRisk,
                "Resolution SLA at risk",
                $"{incident.IncidentNumber} · resolution SLA is at risk{FormatRemaining(sla.ResolutionRemainingMinutes)}.",
                "resolution:atrisk",
                cancellationToken);
        }

        if (sla.ResolutionStatus == SlaStatus.Breached.ToString())
        {
            await QueueSlaEventAsync(
                incident,
                NotificationType.SlaBreached,
                "Resolution SLA breached",
                $"{incident.IncidentNumber} · resolution SLA has breached{FormatOverdue(sla.ResolutionOverdueMinutes)}.",
                "resolution:breached",
                cancellationToken);
        }
    }

    public async Task QueueSlaNotificationsForActiveIncidentsAsync(
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
            await QueueSlaNotificationsForIncidentAsync(
                incident,
                timestamp,
                cancellationToken);
        }
    }

    private async Task QueueSlaEventAsync(
        Incident incident,
        NotificationType type,
        string title,
        string message,
        string logicalEvent,
        CancellationToken cancellationToken)
    {
        var recipients = await GetSlaRecipientIdsAsync(
            incident,
            cancellationToken);

        var deduplicationKey = $"sla:{incident.Id}:{logicalEvent}";

        foreach (var recipientId in recipients)
        {
            await QueueNotificationAsync(
                recipientId,
                type,
                title,
                message,
                incident.Id,
                incident.Title,
                incident.IncidentNumber,
                null,
                deduplicationKey,
                cancellationToken,
                isEmailEligible: true);
        }
    }

    /// <summary>
    /// SLA notifications go to:
    ///   1. The assigned technician (if any)
    ///   2. All active Managers — or all active Admins if no Managers exist
    /// Unrelated technicians are never included.
    /// </summary>
    private async Task<IReadOnlyList<Guid>> GetSlaRecipientIdsAsync(
        Incident incident,
        CancellationToken cancellationToken)
    {
        var recipients = new HashSet<Guid>();

        // 1. Assigned technician
        if (incident.AssignedToId.HasValue)
        {
            var activeTechnicianIds = await FilterActiveRecipientIdsAsync(
                new Guid?[] { incident.AssignedToId.Value },
                null,
                cancellationToken);

            foreach (var technicianId in activeTechnicianIds)
            {
                recipients.Add(technicianId);
            }
        }

        // 2. Managers (or Admin fallback)
        var managerIds = await GetActiveManagersOrAdminFallbackAsync(
            null,
            cancellationToken);

        foreach (var managerId in managerIds)
        {
            recipients.Add(managerId);
        }

        return recipients.ToList();
    }

    private async Task<IReadOnlyList<Guid>> GetActiveManagersOrAdminFallbackAsync(
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var managers = await GetActiveUserIdsInRoleAsync(
            "Manager",
            actorUserId,
            cancellationToken);

        if (managers.Count > 0)
        {
            return managers;
        }

        return await GetActiveUserIdsInRoleAsync(
            "Admin",
            actorUserId,
            cancellationToken);
    }

    private async Task<List<Guid>> GetActiveUserIdsInRoleAsync(
        string role,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        return await _context.Users
            .AsNoTracking()
            .Where(u =>
                u.IsActive &&
                u.Role.Name == role &&
                (!actorUserId.HasValue || u.Id != actorUserId.Value))
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);
    }

    private async Task<List<Guid>> FilterActiveRecipientIdsAsync(
        IEnumerable<Guid?> userIds,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var ids = userIds
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Where(id => !actorUserId.HasValue || id != actorUserId.Value)
            .Distinct()
            .ToList();

        if (ids.Count == 0)
        {
            return new List<Guid>();
        }

        return await _context.Users
            .AsNoTracking()
            .Where(u => u.IsActive && ids.Contains(u.Id))
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);
    }

    private async Task QueueNotificationAsync(
        Guid userId,
        NotificationType type,
        string title,
        string message,
        Guid? incidentId,
        string? incidentTitle,
        string? incidentNumber,
        Guid? actorUserId,
        string? deduplicationKey,
        CancellationToken cancellationToken,
        bool isEmailEligible = false)
    {
        if (actorUserId.HasValue && userId == actorUserId.Value)
        {
            return;
        }

        var recipient = await GetActiveRecipientAsync(userId, cancellationToken);
        if (recipient is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(deduplicationKey) &&
            await NotificationExistsAsync(userId, deduplicationKey, cancellationToken))
        {
            return;
        }

        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Type = type,
            Title = title,
            Message = message,
            IncidentId = incidentId,
            IncidentTitle = incidentTitle,
            IncidentNumber = incidentNumber,
            ActorUserId = actorUserId,
            DeduplicationKey = deduplicationKey,
            CreatedAt = DateTime.UtcNow
        };

        _context.Notifications.Add(notification);

        if (isEmailEligible)
        {
            QueueEmailDeliverySafe(notification, recipient);
        }
    }

    private void QueueEmailDeliverySafe(
        Notification notification,
        RecipientUserInfo recipient)
    {
        try
        {
            if (!recipient.EmailNotificationsEnabled)
            {
                return;
            }

            if (!IsValidEmail(recipient.Email))
            {
                _logger?.LogInformation(
                    "Skipping email delivery for user {UserId}: email address '{Email}' is missing or invalid.",
                    recipient.Id,
                    recipient.Email);
                return;
            }

            var idempotencyKey = $"email/{notification.Id}/{recipient.Id}";

            _pendingDeliveries.Add(new ExternalDeliveryDraft(
                notification.Id,
                recipient.Email,
                idempotencyKey));

            _logger?.LogInformation(
                "External email delivery drafted: {RecipientAddress} (Notification: {NotificationId}, Key: {IdempotencyKey})",
                recipient.Email,
                notification.Id,
                idempotencyKey);
        }
        catch (Exception ex)
        {
            // Email delivery drafting failure must NEVER fail notification or incident workflows
            _logger?.LogError(
                ex,
                "Failed to draft external email delivery for notification {NotificationId}. In-app notification preserved.",
                notification.Id);
        }
    }

    public async Task StagePendingExternalDeliveriesAsync(CancellationToken cancellationToken = default)
    {
        if (_pendingDeliveries.Count == 0)
        {
            return;
        }

        var deliveriesToStage = _pendingDeliveries.ToList();
        _pendingDeliveries.Clear();

        IServiceScope? scope = null;
        try
        {
            AppDbContext stagingContext;
            (scope, stagingContext) = CreateStagingContext();

            await using (stagingContext)
            {
                foreach (var draft in deliveriesToStage)
                {
                    var delivery = new ExternalNotificationDelivery
                    {
                        Id = Guid.NewGuid(),
                        NotificationId = draft.NotificationId,
                        Channel = ExternalDeliveryChannel.Email,
                        RecipientAddress = draft.RecipientAddress,
                        Provider = "Resend",
                        Status = ExternalDeliveryStatus.Pending,
                        AttemptCount = 0,
                        NextAttemptAt = DateTime.UtcNow,
                        IdempotencyKey = draft.IdempotencyKey,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };

                    stagingContext.ExternalNotificationDeliveries.Add(delivery);
                }

                await stagingContext.SaveChangesAsync(cancellationToken);

                _logger?.LogInformation(
                    "Staged {Count} external email deliveries in separate DbContext scope.",
                    deliveriesToStage.Count);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(
                ex,
                "Failed to stage external email deliveries in separate scope. Core business transaction and in-app notifications are preserved.");
        }
        finally
        {
            scope?.Dispose();
        }
    }

    private (IServiceScope? scope, AppDbContext context) CreateStagingContext()
    {
        if (_scopeFactory != null)
        {
            var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return (scope, context);
        }

        var connection = _context.Database.GetDbConnection();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connection)
            .Options;

        var stagingContext = new AppDbContext(options);
        var currentTx = _context.Database.CurrentTransaction;
        if (currentTx != null)
        {
            stagingContext.Database.UseTransaction(currentTx.GetDbTransaction());
        }

        return (null, stagingContext);
    }

    private static bool IsValidEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        var trimmed = email.Trim();
        if (!System.Net.Mail.MailAddress.TryCreate(trimmed, out var mailAddress))
        {
            return false;
        }

        return string.Equals(mailAddress.Address, trimmed, StringComparison.OrdinalIgnoreCase) &&
               mailAddress.Host.Contains('.');
    }

    private record ExternalDeliveryDraft(
        Guid NotificationId,
        string RecipientAddress,
        string IdempotencyKey);

    private record RecipientUserInfo(
        Guid Id,
        string Email,
        bool IsActive,
        bool EmailNotificationsEnabled);

    private async Task<RecipientUserInfo?> GetActiveRecipientAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var local = _context.ChangeTracker
            .Entries<AppUser>()
            .FirstOrDefault(e => e.Entity.Id == userId)?.Entity;

        if (local is not null)
        {
            return local.IsActive
                ? new RecipientUserInfo(local.Id, local.Email, local.IsActive, local.EmailNotificationsEnabled)
                : null;
        }

        return await _context.Users
            .AsNoTracking()
            .Where(u => u.Id == userId && u.IsActive)
            .Select(u => new RecipientUserInfo(
                u.Id,
                u.Email,
                u.IsActive,
                u.EmailNotificationsEnabled))
            .SingleOrDefaultAsync(cancellationToken);
    }

    private async Task<bool> NotificationExistsAsync(
        Guid userId,
        string deduplicationKey,
        CancellationToken cancellationToken)
    {
        var alreadyTracked = _context.ChangeTracker
            .Entries<Notification>()
            .Any(entry =>
                entry.Entity.UserId == userId &&
                entry.Entity.DeduplicationKey == deduplicationKey);

        if (alreadyTracked)
        {
            return true;
        }

        return await _context.Notifications
            .AsNoTracking()
            .AnyAsync(
                n =>
                    n.UserId == userId &&
                    n.DeduplicationKey == deduplicationKey,
                cancellationToken);
    }

    private async Task<string> GetActorDisplayNameAsync(
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        var actor = await _context.Users
            .AsNoTracking()
            .Where(u => u.Id == actorUserId)
            .Select(u => new
            {
                u.FirstName,
                u.LastName
            })
            .SingleOrDefaultAsync(cancellationToken);

        return actor is null
            ? "Someone"
            : $"{actor.FirstName} {actor.LastName}".Trim();
    }

    private static bool IncidentRequiresManagementAttention(Incident incident)
    {
        return incident.AssignedToId is null &&
            incident.Status is not IncidentStatus.Resolved and not IncidentStatus.Closed;
    }

    private static string GetDisplayName(AppUser user)
    {
        return $"{user.FirstName} {user.LastName}".Trim();
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

    private static string FormatRemaining(int? minutes)
    {
        return minutes.HasValue
            ? $" with {FormatMinutes(minutes.Value)} remaining"
            : string.Empty;
    }

    private static string FormatOverdue(int? minutes)
    {
        return minutes.HasValue
            ? $" by {FormatMinutes(minutes.Value)}"
            : string.Empty;
    }

    private static string FormatMinutes(int minutes)
    {
        if (minutes < 60)
        {
            return $"{minutes} minutes";
        }

        var hours = minutes / 60;
        var remainingMinutes = minutes % 60;

        if (hours < 24)
        {
            return remainingMinutes == 0
                ? $"{hours} hours"
                : $"{hours} hours {remainingMinutes} minutes";
        }

        var days = hours / 24;
        var remainingHours = hours % 24;

        return remainingHours == 0
            ? $"{days} days"
            : $"{days} days {remainingHours} hours";
    }

    private static string TruncateExcerpt(string text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        // Remove newlines and collapse whitespace for the excerpt
        var single = string.Join(" ", text.Split(
            new[] { '\r', '\n' },
            StringSplitOptions.RemoveEmptyEntries));

        if (single.Length <= maxLength)
        {
            return single;
        }

        return single[..maxLength].TrimEnd() + "…";
    }
}
