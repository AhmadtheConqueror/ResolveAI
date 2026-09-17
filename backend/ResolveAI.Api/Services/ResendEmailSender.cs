using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ResolveAI.Api.Models.Email;

namespace ResolveAI.Api.Services;

public class ResendEmailSender : IEmailSender
{
    private readonly HttpClient _httpClient;
    private readonly ResendOptions _resendOptions;
    private readonly ExternalNotificationOptions _externalOptions;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<ResendEmailSender> _logger;

    public ResendEmailSender(
        HttpClient httpClient,
        IOptions<ResendOptions> resendOptions,
        IOptions<ExternalNotificationOptions> externalOptions,
        IHostEnvironment environment,
        ILogger<ResendEmailSender> logger)
    {
        _httpClient = httpClient;
        _resendOptions = resendOptions.Value;
        _externalOptions = externalOptions.Value;
        _environment = environment;
        _logger = logger;
    }

    public async Task<EmailSendResult> SendAsync(
        EmailMessage message,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (!_externalOptions.EmailEnabled)
        {
            _logger.LogInformation("External email sending skipped: ExternalNotifications:EmailEnabled is disabled in configuration.");
            return EmailSendResult.Failed("External email delivery is disabled.", isTransient: false);
        }

        if (string.IsNullOrWhiteSpace(_resendOptions.ApiKey))
        {
            _logger.LogWarning("Resend email sending skipped: Resend ApiKey is missing.");
            return EmailSendResult.Failed("Resend ApiKey is not configured.", isTransient: false);
        }

        var recipient = message.To;
        var subject = message.Subject;

        // Apply development test override if configured
        if (_environment.IsDevelopment() &&
            !string.IsNullOrWhiteSpace(_externalOptions.OverrideRecipient))
        {
            _logger.LogInformation(
                "Development email override active: routing email intended for {OriginalRecipient} to {OverrideRecipient}",
                recipient,
                _externalOptions.OverrideRecipient);

            recipient = _externalOptions.OverrideRecipient;
            subject = $"[DEV TO: {message.To}] {subject}";
        }

        var fromFormatted = string.IsNullOrWhiteSpace(_resendOptions.FromName)
            ? _resendOptions.FromAddress
            : $"{_resendOptions.FromName} <{_resendOptions.FromAddress}>";

        var payload = new
        {
            from = fromFormatted,
            to = new[] { recipient },
            subject = subject,
            html = message.HtmlBody,
            text = message.TextBody
        };

        var requestJson = JsonSerializer.Serialize(payload);

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails")
        {
            Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _resendOptions.ApiKey);

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        }

        _logger.LogInformation(
            "Sending email via Resend to {Recipient} with idempotency key {IdempotencyKey}",
            recipient,
            idempotencyKey);

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var statusCode = (int)response.StatusCode;
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var providerMessageId = ExtractMessageId(responseBody);
                _logger.LogInformation(
                    "Email successfully delivered via Resend. ProviderMessageId: {ProviderMessageId}",
                    providerMessageId);

                return EmailSendResult.Succeeded(providerMessageId);
            }

            var isTransient = statusCode switch
            {
                429 => true,
                408 => true,
                >= 500 and <= 599 => true,
                _ => false
            };

            var errorMessage = ExtractErrorMessage(responseBody, statusCode);

            _logger.LogWarning(
                "Resend email sending failed with status {StatusCode} (isTransient: {IsTransient}): {ErrorMessage}",
                statusCode,
                isTransient,
                errorMessage);

            return EmailSendResult.Failed(
                errorMessage,
                isTransient: isTransient,
                statusCode: statusCode);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Transient network or timeout error while dispatching email via Resend to {Recipient}",
                recipient);

            return EmailSendResult.Failed(
                $"Network/timeout failure: {ex.Message}",
                isTransient: true);
        }
    }

    private static string? ExtractMessageId(string responseJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            if (doc.RootElement.TryGetProperty("id", out var idElement))
            {
                return idElement.GetString();
            }
        }
        catch
        {
            // fallback
        }

        return null;
    }

    private static string ExtractErrorMessage(string responseJson, int statusCode)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            if (doc.RootElement.TryGetProperty("message", out var msgElement))
            {
                return msgElement.GetString() ?? $"HTTP {statusCode}";
            }

            if (doc.RootElement.TryGetProperty("error", out var errorElement))
            {
                return errorElement.ToString();
            }
        }
        catch
        {
            // fallback
        }

        return $"Resend HTTP {statusCode} response";
    }
}
