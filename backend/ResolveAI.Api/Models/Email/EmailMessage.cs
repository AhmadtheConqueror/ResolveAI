namespace ResolveAI.Api.Models.Email;

public class EmailMessage
{
    public string To { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;

    public string HtmlBody { get; set; } = string.Empty;

    public string TextBody { get; set; } = string.Empty;
}
