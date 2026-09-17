namespace ResolveAI.Api.Models.Email;

public class ExternalNotificationOptions
{
    public const string SectionName = "ExternalNotifications";

    public bool EmailEnabled { get; set; }

    /// <summary>
    /// When specified in Development environment, all emails are routed to this test address.
    /// Never used in Production.
    /// </summary>
    public string? OverrideRecipient { get; set; }
}
