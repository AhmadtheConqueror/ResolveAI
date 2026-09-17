namespace ResolveAI.Api.Models.Email;

public class ResendOptions
{
    public const string SectionName = "Resend";

    public string? ApiKey { get; set; }

    public string FromEmail { get; set; } = "notifications@resolveai.dev";

    public string FromAddress
    {
        get => FromEmail;
        set => FromEmail = value;
    }

    public string FromName { get; set; } = "ResolveAI Notifications";

    public string ApiUrl { get; set; } = "https://api.resend.com/emails";
}
