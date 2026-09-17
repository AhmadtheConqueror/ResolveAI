using ResolveAI.Api.Models.Email;

namespace ResolveAI.Api.Services;

public interface IEmailSender
{
    Task<EmailSendResult> SendAsync(
        EmailMessage message,
        string idempotencyKey,
        CancellationToken cancellationToken = default);
}
