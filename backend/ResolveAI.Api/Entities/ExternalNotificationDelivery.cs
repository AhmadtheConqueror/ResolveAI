using ResolveAI.Api.Enums;

namespace ResolveAI.Api.Entities;

public class ExternalNotificationDelivery
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid NotificationId { get; set; }

    public Notification Notification { get; set; } = null!;

    public ExternalDeliveryChannel Channel { get; set; } = ExternalDeliveryChannel.Email;

    public string RecipientAddress { get; set; } = string.Empty;

    public string Provider { get; set; } = "Resend";

    public ExternalDeliveryStatus Status { get; set; } = ExternalDeliveryStatus.Pending;

    public int AttemptCount { get; set; }

    public DateTime? LastAttemptAt { get; set; }

    public DateTime? SentAt { get; set; }

    public string? ProviderMessageId { get; set; }

    public string? LastError { get; set; }

    public string IdempotencyKey { get; set; } = string.Empty;

    public DateTime? NextAttemptAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
