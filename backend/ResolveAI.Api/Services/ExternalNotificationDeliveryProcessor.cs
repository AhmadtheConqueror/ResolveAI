using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ResolveAI.Api.Data;
using ResolveAI.Api.Entities;
using ResolveAI.Api.Enums;
using ResolveAI.Api.Models.Email;

namespace ResolveAI.Api.Services;

public class ExternalNotificationDeliveryProcessor : IExternalNotificationDeliveryProcessor
{
    private const int BatchSize = 10;
    private const int MaxAttempts = 4;

    private readonly AppDbContext _context;
    private readonly IEmailSender _emailSender;
    private readonly IEmailTemplateService _templateService;
    private readonly ExternalNotificationOptions _options;
    private readonly ILogger<ExternalNotificationDeliveryProcessor> _logger;

    public ExternalNotificationDeliveryProcessor(
        AppDbContext context,
        IEmailSender emailSender,
        IEmailTemplateService templateService,
        IOptions<ExternalNotificationOptions> options,
        ILogger<ExternalNotificationDeliveryProcessor> logger)
    {
        _context = context;
        _emailSender = emailSender;
        _templateService = templateService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<int> ProcessPendingDeliveriesAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.EmailEnabled)
        {
            _logger.LogDebug("External email processing skipped: ExternalNotifications:EmailEnabled is false.");
            return 0;
        }

        var now = DateTime.UtcNow;

        // Find pending or failed deliveries that are due for attempt
        var candidateDeliveries = await _context.ExternalNotificationDeliveries
            .Where(d =>
                (d.Status == ExternalDeliveryStatus.Pending || d.Status == ExternalDeliveryStatus.Failed) &&
                (!d.NextAttemptAt.HasValue || d.NextAttemptAt.Value <= now))
            .OrderBy(d => d.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        if (candidateDeliveries.Count == 0)
        {
            return 0;
        }

        // Atomically claim the batch
        foreach (var delivery in candidateDeliveries)
        {
            delivery.Status = ExternalDeliveryStatus.Processing;
            delivery.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(cancellationToken);

        var processedCount = 0;

        foreach (var delivery in candidateDeliveries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await ProcessSingleDeliveryAsync(delivery, cancellationToken);
                processedCount++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Unhandled exception while processing delivery {DeliveryId} for {RecipientAddress}",
                    delivery.Id,
                    delivery.RecipientAddress);

                delivery.AttemptCount++;
                delivery.LastAttemptAt = DateTime.UtcNow;
                delivery.LastError = Truncate(ex.Message, 500);

                if (delivery.AttemptCount >= MaxAttempts)
                {
                    delivery.Status = ExternalDeliveryStatus.PermanentlyFailed;
                    delivery.NextAttemptAt = null;
                }
                else
                {
                    delivery.Status = ExternalDeliveryStatus.Failed;
                    var delayMinutes = delivery.AttemptCount switch
                    {
                        1 => 1,
                        2 => 5,
                        _ => 30
                    };
                    delivery.NextAttemptAt = DateTime.UtcNow.AddMinutes(delayMinutes);
                }

                delivery.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync(CancellationToken.None);
            }
        }

        return processedCount;
    }

    private async Task ProcessSingleDeliveryAsync(
        ExternalNotificationDelivery delivery,
        CancellationToken cancellationToken)
    {
        var notification = await _context.Notifications
            .Include(n => n.Incident).ThenInclude(i => i!.Priority)
            .Include(n => n.Incident).ThenInclude(i => i!.AssignedTo)
            .SingleOrDefaultAsync(n => n.Id == delivery.NotificationId, cancellationToken);

        if (notification is null)
        {
            _logger.LogWarning(
                "Notification {NotificationId} not found for delivery {DeliveryId}. Marking PermanentlyFailed.",
                delivery.NotificationId,
                delivery.Id);

            delivery.Status = ExternalDeliveryStatus.PermanentlyFailed;
            delivery.LastError = "Notification record not found.";
            delivery.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
            return;
        }

        var recipient = await _context.Users
            .SingleOrDefaultAsync(u => u.Id == notification.UserId, cancellationToken);

        if (recipient is null || !recipient.IsActive)
        {
            _logger.LogWarning(
                "Recipient user {UserId} not found or inactive for delivery {DeliveryId}. Marking PermanentlyFailed.",
                notification.UserId,
                delivery.Id);

            delivery.Status = ExternalDeliveryStatus.PermanentlyFailed;
            delivery.LastError = "Recipient user not found or inactive.";
            delivery.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
            return;
        }

        var emailMessage = _templateService.BuildEmail(delivery, notification, recipient);

        _logger.LogInformation(
            "Executing email delivery attempt {AttemptCount} for delivery {DeliveryId} to {RecipientAddress} (Key: {IdempotencyKey})",
            delivery.AttemptCount + 1,
            delivery.Id,
            delivery.RecipientAddress,
            delivery.IdempotencyKey);

        delivery.AttemptCount++;
        delivery.LastAttemptAt = DateTime.UtcNow;
        delivery.UpdatedAt = DateTime.UtcNow;

        var result = await _emailSender.SendAsync(
            emailMessage,
            delivery.IdempotencyKey,
            cancellationToken);

        if (result.Success)
        {
            delivery.Status = ExternalDeliveryStatus.Sent;
            delivery.SentAt = DateTime.UtcNow;
            delivery.ProviderMessageId = result.ProviderMessageId;
            delivery.LastError = null;
            delivery.NextAttemptAt = null;

            _logger.LogInformation(
                "External delivery {DeliveryId} marked Sent. ProviderMessageId: {ProviderMessageId}",
                delivery.Id,
                result.ProviderMessageId);
        }
        else
        {
            delivery.LastError = Truncate(result.ErrorMessage, 500);

            if (result.IsTransient && delivery.AttemptCount < MaxAttempts)
            {
                delivery.Status = ExternalDeliveryStatus.Failed;
                var delayMinutes = delivery.AttemptCount switch
                {
                    1 => 1,
                    2 => 5,
                    _ => 30
                };
                delivery.NextAttemptAt = DateTime.UtcNow.AddMinutes(delayMinutes);

                _logger.LogWarning(
                    "External delivery {DeliveryId} failed with transient error: {Error}. Retry scheduled in {DelayMinutes} minute(s) at {NextAttemptAt}",
                    delivery.Id,
                    delivery.LastError,
                    delayMinutes,
                    delivery.NextAttemptAt);
            }
            else
            {
                delivery.Status = ExternalDeliveryStatus.PermanentlyFailed;
                delivery.NextAttemptAt = null;

                _logger.LogWarning(
                    "External delivery {DeliveryId} permanently failed (Transient: {IsTransient}, AttemptCount: {AttemptCount}): {Error}",
                    delivery.Id,
                    result.IsTransient,
                    delivery.AttemptCount,
                    delivery.LastError);
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
