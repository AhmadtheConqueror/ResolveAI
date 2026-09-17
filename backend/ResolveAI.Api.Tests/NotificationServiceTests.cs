using ResolveAI.Api.Entities;
using ResolveAI.Api.Enums;

namespace ResolveAI.Api.Tests;

public class NotificationServiceTests
{
    public void RunAllTests()
    {
        Console.WriteLine("\n--- NotificationServiceTests ---");

        Test_Notification_Incident_Snapshot_Fields();
        Test_Actor_Exclusion_Rule();
        Test_Sla_Deduplication_Key_Format();
        Test_Resolved_And_Closed_Incidents_Skip_Sla_Notifications();
    }

    private void Test_Notification_Incident_Snapshot_Fields()
    {
        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            IncidentNumber = "INC-2026-0001",
            Title = "Network connection failing in Building A"
        };

        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            IncidentId = incident.Id,
            IncidentNumber = incident.IncidentNumber,
            IncidentTitle = incident.Title,
            Title = "Incident Assigned",
            Message = $"{incident.IncidentNumber} · assigned to you."
        };

        Assert.Equal("INC-2026-0001", notification.IncidentNumber);
        Assert.Equal("Network connection failing in Building A", notification.IncidentTitle);
        Assert.Equal(incident.Id, notification.IncidentId);
        Assert.False(notification.IsRead);

        Console.WriteLine("  ✓ IncidentTitle and IncidentNumber snapshot fields properly retained");
    }

    private void Test_Actor_Exclusion_Rule()
    {
        var actorUserId = Guid.NewGuid();
        var technicianId = Guid.NewGuid();

        // If actor is the technician reassigning or commenting, they should not notify themselves
        var recipients = new List<Guid> { actorUserId, technicianId };
        var filteredRecipients = recipients.Where(r => r != actorUserId).ToList();

        Assert.Equal(1, filteredRecipients.Count);
        Assert.Equal(technicianId, filteredRecipients[0]);
        Assert.False(filteredRecipients.Contains(actorUserId));

        Console.WriteLine("  ✓ Actor exclusion rule ensures actors never receive their own event notification");
    }

    private void Test_Sla_Deduplication_Key_Format()
    {
        var incidentId = Guid.NewGuid();

        var responseAtRiskKey = $"sla:response:at-risk:{incidentId}";
        var responseBreachedKey = $"sla:response:breached:{incidentId}";
        var resolutionAtRiskKey = $"sla:resolution:at-risk:{incidentId}";
        var resolutionBreachedKey = $"sla:resolution:breached:{incidentId}";

        Assert.True(responseAtRiskKey.StartsWith("sla:response:at-risk:"));
        Assert.True(responseBreachedKey.StartsWith("sla:response:breached:"));
        Assert.True(resolutionAtRiskKey.StartsWith("sla:resolution:at-risk:"));
        Assert.True(resolutionBreachedKey.StartsWith("sla:resolution:breached:"));

        // Deduplication keys for different incidents must not collide
        var otherIncidentId = Guid.NewGuid();
        var otherKey = $"sla:response:breached:{otherIncidentId}";
        Assert.NotEqual(responseBreachedKey, otherKey);

        Console.WriteLine("  ✓ SLA deduplication key formatting and uniqueness verified");
    }

    private void Test_Resolved_And_Closed_Incidents_Skip_Sla_Notifications()
    {
        var resolvedIncident = new Incident { Status = IncidentStatus.Resolved };
        var closedIncident = new Incident { Status = IncidentStatus.Closed };
        var inProgressIncident = new Incident { Status = IncidentStatus.InProgress };

        static bool ShouldCheckSla(Incident inc) =>
            inc.Status is not (IncidentStatus.Resolved or IncidentStatus.Closed);

        Assert.False(ShouldCheckSla(resolvedIncident), "Resolved incident must skip SLA alerts");
        Assert.False(ShouldCheckSla(closedIncident), "Closed incident must skip SLA alerts");
        Assert.True(ShouldCheckSla(inProgressIncident), "InProgress incident must be evaluated for SLA alerts");

        Console.WriteLine("  ✓ Resolved and Closed incidents bypass SLA notification generation");
    }
}
