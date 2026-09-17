using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using ResolveAI.Api.Entities;
using ResolveAI.Api.Enums;
using ResolveAI.Api.Models.Email;

namespace ResolveAI.Api.Services;

public class EmailTemplateService : IEmailTemplateService
{
    private readonly string _frontendBaseUrl;
    private readonly BrandingOptions _brandingOptions;

    public EmailTemplateService(
        IConfiguration configuration,
        IOptions<BrandingOptions>? brandingOptions = null)
    {
        _frontendBaseUrl = (configuration["Frontend:BaseUrl"] ?? "http://localhost:5173").TrimEnd('/');
        _brandingOptions = brandingOptions?.Value ?? new BrandingOptions();
    }

    public EmailMessage BuildEmail(
        ExternalNotificationDelivery delivery,
        Notification notification,
        AppUser recipient)
    {
        var recipientName = string.IsNullOrWhiteSpace(recipient.FirstName)
            ? "there"
            : recipient.FirstName.Trim();

        var incidentLink = notification.IncidentId.HasValue
            ? $"{_frontendBaseUrl}/incidents/{notification.IncidentId.Value}"
            : $"{_frontendBaseUrl}/incidents";

        var incidentTitle = notification.IncidentTitle ?? "Incident";
        var incidentNumber = notification.IncidentNumber ?? "INC";

        var subject = DetermineSubject(notification, incidentNumber);

        var textBody = BuildPlainTextBody(
            recipientName,
            notification,
            incidentTitle,
            incidentNumber,
            incidentLink);

        var htmlBody = BuildHtmlBody(
            recipientName,
            notification,
            incidentTitle,
            incidentNumber,
            incidentLink);

        return new EmailMessage
        {
            To = delivery.RecipientAddress,
            Subject = subject,
            TextBody = textBody,
            HtmlBody = htmlBody
        };
    }

    private static string DetermineSubject(Notification notification, string incidentNumber)
    {
        return notification.Type switch
        {
            NotificationType.IncidentAssigned =>
                notification.Title.Contains("reassigned", StringComparison.OrdinalIgnoreCase)
                    ? $"[ResolveAI] Incident reassigned to you — {incidentNumber}"
                    : (notification.Title.Contains("Your incident", StringComparison.OrdinalIgnoreCase)
                        ? $"[ResolveAI] Your incident has been assigned — {incidentNumber}"
                        : $"[ResolveAI] Incident assigned to you — {incidentNumber}"),

            NotificationType.IncidentReassigned =>
                notification.Title.Contains("Your incident", StringComparison.OrdinalIgnoreCase)
                    ? $"[ResolveAI] Your incident has been reassigned — {incidentNumber}"
                    : $"[ResolveAI] Incident reassigned to you — {incidentNumber}",

            NotificationType.IncidentCommentAdded =>
                $"[ResolveAI] Reporter replied — {incidentNumber}",

            NotificationType.IncidentResolved =>
                $"[ResolveAI] Incident resolved — {incidentNumber}",

            NotificationType.IncidentClosed =>
                $"[ResolveAI] Incident closed — {incidentNumber}",

            NotificationType.IncidentCreated =>
                $"[ResolveAI] New incident requires triage — {incidentNumber}",

            NotificationType.SlaAtRisk =>
                notification.Title.Contains("Response", StringComparison.OrdinalIgnoreCase) ||
                notification.DeduplicationKey?.Contains(":response:") == true
                    ? $"[ResolveAI] Response SLA at risk — {incidentNumber}"
                    : $"[ResolveAI] Resolution SLA at risk — {incidentNumber}",

            NotificationType.SlaBreached =>
                notification.Title.Contains("Response", StringComparison.OrdinalIgnoreCase) ||
                notification.DeduplicationKey?.Contains(":response:") == true
                    ? $"[ResolveAI] Response SLA breached — {incidentNumber}"
                    : $"[ResolveAI] Resolution SLA breached — {incidentNumber}",

            _ => $"[ResolveAI] {notification.Title} — {incidentNumber}"
        };
    }

    private static readonly Dictionary<string, (int ResponseMinutes, int ResolutionMinutes)> SlaPolicyByPriority =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Low"] = (480, 4320),      // 8h response, 72h resolution
            ["Medium"] = (240, 2880),   // 4h response, 48h resolution
            ["High"] = (120, 1440),     // 2h response, 24h resolution
            ["Critical"] = (30, 480),   // 30m response, 8h resolution
        };

    private static string ExtractExplanation(Notification notification)
    {
        if (string.IsNullOrWhiteSpace(notification.Message))
        {
            return notification.Title;
        }

        var parts = notification.Message.Split(" · ", 2, StringSplitOptions.TrimEntries);
        var text = parts.Length == 2 ? parts[1] : notification.Message;
        if (string.IsNullOrWhiteSpace(text))
        {
            return notification.Title;
        }

        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    private static string GetPriority(Notification notification)
    {
        return notification.Incident?.Priority?.Name ?? "Standard";
    }

    private static string GetAssignedTechnician(Notification notification)
    {
        var assigned = notification.Incident?.AssignedTo;
        if (assigned == null)
        {
            return "Unassigned";
        }

        var fullName = $"{assigned.FirstName} {assigned.LastName}".Trim();
        return string.IsNullOrWhiteSpace(fullName) ? "Assigned" : fullName;
    }

    private static (bool IsSla, string SlaType, string SlaState, string TimeLabel, string TimeStatus, string? TargetDeadline) GetSlaDetails(Notification notification)
    {
        var isSla = notification.Type is NotificationType.SlaAtRisk or NotificationType.SlaBreached;
        if (!isSla)
        {
            return (false, string.Empty, string.Empty, string.Empty, string.Empty, null);
        }

        var slaType = notification.Title.Contains("Response", StringComparison.OrdinalIgnoreCase) ||
                      notification.DeduplicationKey?.Contains(":response:") == true
            ? "Response"
            : "Resolution";

        var slaState = notification.Type == NotificationType.SlaBreached ? "Breached" : "At Risk";
        var timeLabel = slaState == "Breached" ? "Amount Overdue" : "Time Remaining";

        var rawExplanation = ExtractExplanation(notification);
        var timeStatus = string.Empty;
        var lower = rawExplanation.ToLowerInvariant();
        var byIndex = lower.IndexOf("by ", StringComparison.Ordinal);
        var withIndex = lower.IndexOf("with ", StringComparison.Ordinal);

        if (byIndex >= 0)
        {
            timeStatus = rawExplanation[(byIndex + 3)..].TrimEnd('.');
        }
        else if (withIndex >= 0)
        {
            timeStatus = rawExplanation[(withIndex + 5)..].TrimEnd('.');
            if (timeStatus.EndsWith(" remaining", StringComparison.OrdinalIgnoreCase))
            {
                timeStatus = timeStatus[..^10].Trim();
            }
        }
        else
        {
            timeStatus = rawExplanation.TrimEnd('.');
        }

        string? targetDeadline = null;
        if (notification.Incident?.Priority?.Name != null)
        {
            if (SlaPolicyByPriority.TryGetValue(notification.Incident.Priority.Name, out var policy))
            {
                var targetMinutes = slaType == "Response" ? policy.ResponseMinutes : policy.ResolutionMinutes;
                var deadline = notification.Incident.CreatedAt.AddMinutes(targetMinutes);
                targetDeadline = deadline.ToString("yyyy-MM-dd HH:mm UTC");
            }
        }

        return (true, slaType, slaState, timeLabel, timeStatus, targetDeadline);
    }

    private static string BuildPlainTextBody(
        string recipientName,
        Notification notification,
        string incidentTitle,
        string incidentNumber,
        string incidentLink)
    {
        var explanation = ExtractExplanation(notification);
        var priority = GetPriority(notification);
        var assignedTo = GetAssignedTechnician(notification);
        var (isSla, slaType, slaState, timeLabel, timeStatus, targetDeadline) = GetSlaDetails(notification);

        var sb = new StringBuilder();
        sb.AppendLine($"Hello {recipientName},");
        sb.AppendLine();
        sb.AppendLine(notification.Title.ToUpperInvariant());
        sb.AppendLine();
        sb.AppendLine(explanation);
        sb.AppendLine();
        sb.AppendLine("----------------------------------------");
        sb.AppendLine($"Incident:            {incidentNumber}");
        sb.AppendLine($"Title:               {incidentTitle}");
        sb.AppendLine($"Priority:            {priority}");

        if (isSla)
        {
            sb.AppendLine($"SLA Type:            {slaType} SLA");
            sb.AppendLine($"State:               {slaState}");
            if (!string.IsNullOrWhiteSpace(targetDeadline))
            {
                sb.AppendLine($"Target / Deadline:   {targetDeadline}");
            }
            if (!string.IsNullOrWhiteSpace(timeStatus))
            {
                sb.AppendLine($"{(timeLabel + ":").PadRight(21)}{timeStatus}");
            }
            sb.AppendLine($"Assigned Technician: {assignedTo}");
        }
        else
        {
            sb.AppendLine($"Assigned To:         {assignedTo}");
        }

        sb.AppendLine("----------------------------------------");
        sb.AppendLine();

        if (notification.Type == NotificationType.IncidentResolved)
        {
            sb.AppendLine("Please review the resolution in ResolveAI and confirm closure.");
            sb.AppendLine();
        }

        sb.AppendLine($"View in ResolveAI: {incidentLink}");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine("This is an automated notification from ResolveAI Enterprise Incident Management.");

        return sb.ToString();
    }

    private string BuildHtmlBody(
        string recipientName,
        Notification notification,
        string incidentTitle,
        string incidentNumber,
        string incidentLink)
    {
        var safeRecipient = WebUtility.HtmlEncode(recipientName);
        var safeEventTitle = WebUtility.HtmlEncode(notification.Title);
        var safeExplanation = WebUtility.HtmlEncode(ExtractExplanation(notification));
        var safeIncidentTitle = WebUtility.HtmlEncode(incidentTitle);
        var safeIncidentNumber = WebUtility.HtmlEncode(incidentNumber);
        var safePriority = WebUtility.HtmlEncode(GetPriority(notification));
        var safeAssignedTo = WebUtility.HtmlEncode(GetAssignedTechnician(notification));
        var safeLink = WebUtility.HtmlEncode(incidentLink);

        var (isSla, slaType, slaState, timeLabel, timeStatus, targetDeadline) = GetSlaDetails(notification);

        var logoUrl = _brandingOptions.LogoUrl?.Trim();
        var logoHtml = !string.IsNullOrWhiteSpace(logoUrl)
            ? $@"<div style=""margin-bottom: 12px;""><img src=""{WebUtility.HtmlEncode(logoUrl)}"" alt=""ResolveAI Logo"" style=""max-height: 40px; max-width: 180px; display: block; border: 0;"" /></div>"
            : string.Empty;

        var calloutMessage = notification.Type == NotificationType.IncidentResolved
            ? @"<p style=""color: #0284c7; font-weight: 500; font-size: 14px; margin: 16px 0;"">Please review the resolution in ResolveAI and confirm closure.</p>"
            : string.Empty;

        var detailsTable = isSla
            ? $@"<table style=""width: 100%; border-collapse: collapse; margin: 16px 0; font-size: 14px; background-color: #f8fafc; border: 1px solid #e2e8f0; border-radius: 6px;"">
    <tr>
        <td style=""padding: 9px 14px; color: #64748b; font-weight: 500; width: 145px; border-bottom: 1px solid #e2e8f0;"">Incident</td>
        <td style=""padding: 9px 14px; color: #0f172a; font-weight: 600; border-bottom: 1px solid #e2e8f0;"">{safeIncidentNumber}</td>
    </tr>
    <tr>
        <td style=""padding: 9px 14px; color: #64748b; font-weight: 500; border-bottom: 1px solid #e2e8f0;"">Title</td>
        <td style=""padding: 9px 14px; color: #0f172a; border-bottom: 1px solid #e2e8f0;"">{safeIncidentTitle}</td>
    </tr>
    <tr>
        <td style=""padding: 9px 14px; color: #64748b; font-weight: 500; border-bottom: 1px solid #e2e8f0;"">Priority</td>
        <td style=""padding: 9px 14px; color: #0f172a; border-bottom: 1px solid #e2e8f0;"">{safePriority}</td>
    </tr>
    <tr>
        <td style=""padding: 9px 14px; color: #64748b; font-weight: 500; border-bottom: 1px solid #e2e8f0;"">SLA Type</td>
        <td style=""padding: 9px 14px; color: #0f172a; font-weight: 600; border-bottom: 1px solid #e2e8f0;"">{WebUtility.HtmlEncode(slaType)} SLA</td>
    </tr>
    <tr>
        <td style=""padding: 9px 14px; color: #64748b; font-weight: 500; border-bottom: 1px solid #e2e8f0;"">State</td>
        <td style=""padding: 9px 14px; color: {(slaState == "Breached" ? "#dc2626" : "#d97706")}; font-weight: 700; border-bottom: 1px solid #e2e8f0;"">{WebUtility.HtmlEncode(slaState)}</td>
    </tr>{(!string.IsNullOrWhiteSpace(targetDeadline) ? $@"
    <tr>
        <td style=""padding: 9px 14px; color: #64748b; font-weight: 500; border-bottom: 1px solid #e2e8f0;"">Target / Deadline</td>
        <td style=""padding: 9px 14px; color: #0f172a; border-bottom: 1px solid #e2e8f0;"">{WebUtility.HtmlEncode(targetDeadline)}</td>
    </tr>" : string.Empty)}{(!string.IsNullOrWhiteSpace(timeStatus) ? $@"
    <tr>
        <td style=""padding: 9px 14px; color: #64748b; font-weight: 500; border-bottom: 1px solid #e2e8f0;"">{WebUtility.HtmlEncode(timeLabel)}</td>
        <td style=""padding: 9px 14px; color: #0f172a; border-bottom: 1px solid #e2e8f0;"">{WebUtility.HtmlEncode(timeStatus)}</td>
    </tr>" : string.Empty)}
    <tr>
        <td style=""padding: 9px 14px; color: #64748b; font-weight: 500;"">Assigned Technician</td>
        <td style=""padding: 9px 14px; color: #0f172a;"">{safeAssignedTo}</td>
    </tr>
</table>"
            : $@"<table style=""width: 100%; border-collapse: collapse; margin: 16px 0; font-size: 14px; background-color: #f8fafc; border: 1px solid #e2e8f0; border-radius: 6px;"">
    <tr>
        <td style=""padding: 9px 14px; color: #64748b; font-weight: 500; width: 120px; border-bottom: 1px solid #e2e8f0;"">Incident</td>
        <td style=""padding: 9px 14px; color: #0f172a; font-weight: 600; border-bottom: 1px solid #e2e8f0;"">{safeIncidentNumber}</td>
    </tr>
    <tr>
        <td style=""padding: 9px 14px; color: #64748b; font-weight: 500; border-bottom: 1px solid #e2e8f0;"">Title</td>
        <td style=""padding: 9px 14px; color: #0f172a; border-bottom: 1px solid #e2e8f0;"">{safeIncidentTitle}</td>
    </tr>
    <tr>
        <td style=""padding: 9px 14px; color: #64748b; font-weight: 500; border-bottom: 1px solid #e2e8f0;"">Priority</td>
        <td style=""padding: 9px 14px; color: #0f172a; border-bottom: 1px solid #e2e8f0;"">{safePriority}</td>
    </tr>
    <tr>
        <td style=""padding: 9px 14px; color: #64748b; font-weight: 500;"">Assigned To</td>
        <td style=""padding: 9px 14px; color: #0f172a;"">{safeAssignedTo}</td>
    </tr>
</table>";

        return $@"<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
</head>
<body style=""margin: 0; padding: 24px; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; background-color: #f8fafc; color: #0f172a;"">
    <div style=""max-width: 560px; margin: 0 auto; background: #ffffff; border-radius: 8px; border: 1px solid #e2e8f0; padding: 24px; box-shadow: 0 1px 3px rgba(0,0,0,0.05);"">
        <div style=""border-bottom: 2px solid #2563eb; padding-bottom: 14px; margin-bottom: 20px;"">
            {logoHtml}
            <div style=""font-size: 20px; font-weight: 700; color: #1e3a8a; letter-spacing: -0.02em; margin: 0;"">ResolveAI</div>
            <div style=""font-size: 13px; color: #64748b; margin-top: 2px;"">Enterprise Incident Management</div>
        </div>

        <p style=""font-size: 15px; margin: 0 0 16px 0;"">Hello {safeRecipient},</p>

        <h2 style=""font-size: 17px; font-weight: 600; margin: 0 0 8px 0; color: #1e293b;"">{safeEventTitle}</h2>
        <p style=""font-size: 14px; line-height: 1.5; color: #334155; margin: 0 0 16px 0;"">{safeExplanation}</p>

        {detailsTable}

        {calloutMessage}

        <div style=""margin-top: 24px;"">
            <a href=""{safeLink}"" style=""display: inline-block; background-color: #2563eb; color: #ffffff; font-size: 14px; font-weight: 500; text-decoration: none; padding: 10px 22px; border-radius: 6px;"">View Incident</a>
        </div>

        <div style=""margin-top: 32px; border-top: 1px solid #e2e8f0; padding-top: 12px; font-size: 12px; color: #94a3b8;"">
            This is an automated notification from ResolveAI Enterprise Incident Management.
        </div>
    </div>
</body>
</html>";
    }
}
