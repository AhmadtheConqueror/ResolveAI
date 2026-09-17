using ResolveAI.Api.Entities;
using ResolveAI.Api.Models.Email;

namespace ResolveAI.Api.Services;

public interface IEmailTemplateService
{
    EmailMessage BuildEmail(
        ExternalNotificationDelivery delivery,
        Notification notification,
        AppUser recipient);
}
