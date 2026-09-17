namespace ResolveAI.Api.Models.Email;

public class EmailSendResult
{
    public bool Success { get; set; }

    public string? ProviderMessageId { get; set; }

    public string? ErrorMessage { get; set; }

    public bool IsTransient { get; set; }

    public int? StatusCode { get; set; }

    public static EmailSendResult Succeeded(string? providerMessageId) => new()
    {
        Success = true,
        ProviderMessageId = providerMessageId
    };

    public static EmailSendResult Failed(
        string errorMessage,
        bool isTransient = true,
        int? statusCode = null) => new()
    {
        Success = false,
        ErrorMessage = errorMessage,
        IsTransient = isTransient,
        StatusCode = statusCode
    };
}
