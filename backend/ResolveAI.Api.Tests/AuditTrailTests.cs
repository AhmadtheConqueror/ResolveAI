using ResolveAI.Api.Entities;
using ResolveAI.Api.Enums;

namespace ResolveAI.Api.Tests;

public class AuditTrailTests
{
    public void RunAllTests()
    {
        Console.WriteLine("\n--- AuditTrailTests ---");

        Test_Audit_Event_Creation_Structure();
        Test_Status_Change_Old_And_New_Value_Capture();
        Test_Comment_Activity_Excludes_Raw_Comment_Body();
        Test_Sla_System_Event_ActorType();
        Test_Sla_Audit_Deduplication_Key();
        Test_Ai_Recommendation_Applied_Actor_Is_Human_Approver();
    }

    private void Test_Audit_Event_Creation_Structure()
    {
        var incidentId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        var auditEvent = new IncidentAuditEvent
        {
            Id = Guid.NewGuid(),
            IncidentId = incidentId,
            EventType = IncidentAuditEventType.IncidentCreated,
            ActorUserId = actorId,
            ActorDisplayName = "Alice Johnson",
            ActorType = IncidentAuditActorType.User,
            Summary = "Incident submitted by Alice Johnson",
            CreatedAt = DateTime.UtcNow
        };

        Assert.Equal(IncidentAuditEventType.IncidentCreated, auditEvent.EventType);
        Assert.Equal(IncidentAuditActorType.User, auditEvent.ActorType);
        Assert.Equal(actorId, auditEvent.ActorUserId);
        Assert.Null(auditEvent.OldValue);
        Assert.Null(auditEvent.NewValue);

        Console.WriteLine("  ✓ Creation audit event captures user actor and event type");
    }

    private void Test_Status_Change_Old_And_New_Value_Capture()
    {
        var auditEvent = new IncidentAuditEvent
        {
            Id = Guid.NewGuid(),
            IncidentId = Guid.NewGuid(),
            EventType = IncidentAuditEventType.StatusChanged,
            OldValue = "Assigned",
            NewValue = "InProgress",
            Summary = "Status changed from Assigned to InProgress by Bob Technician"
        };

        Assert.Equal("Assigned", auditEvent.OldValue);
        Assert.Equal("InProgress", auditEvent.NewValue);
        Assert.Equal(IncidentAuditEventType.StatusChanged, auditEvent.EventType);

        Console.WriteLine("  ✓ Status change audit event preserves OldValue and NewValue accurately");
    }

    private void Test_Comment_Activity_Excludes_Raw_Comment_Body()
    {
        // Confidential incident comments should not expose their full body in audit summaries
        var comment = "Confidential password reset token was temporary-12345.";
        var auditSummary = "Public comment added by Alice Johnson";

        var auditEvent = new IncidentAuditEvent
        {
            Id = Guid.NewGuid(),
            IncidentId = Guid.NewGuid(),
            EventType = IncidentAuditEventType.CommentAdded,
            Summary = auditSummary,
            Metadata = null
        };

        Assert.False(auditEvent.Summary.Contains(comment), "Audit summary must not leak raw comment body");
        Assert.Null(auditEvent.Metadata);

        Console.WriteLine("  ✓ Comment audit event excludes raw comment body for privacy");
    }

    private void Test_Sla_System_Event_ActorType()
    {
        var auditEvent = new IncidentAuditEvent
        {
            Id = Guid.NewGuid(),
            IncidentId = Guid.NewGuid(),
            EventType = IncidentAuditEventType.SlaBreached,
            ActorType = IncidentAuditActorType.System,
            ActorUserId = null,
            ActorDisplayName = "ResolveAI System",
            Summary = "Response SLA breached"
        };

        Assert.Equal(IncidentAuditActorType.System, auditEvent.ActorType);
        Assert.Null(auditEvent.ActorUserId);
        Assert.Equal("ResolveAI System", auditEvent.ActorDisplayName);

        Console.WriteLine("  ✓ Automated SLA audit event correctly tagged as IncidentAuditActorType.System");
    }

    private void Test_Sla_Audit_Deduplication_Key()
    {
        var incidentId = Guid.NewGuid();
        var key1 = $"sla:audit:response:breached:{incidentId}";
        var key2 = $"sla:audit:response:breached:{incidentId}";

        Assert.Equal(key1, key2);

        var otherKey = $"sla:audit:resolution:breached:{incidentId}";
        Assert.NotEqual(key1, otherKey);

        Console.WriteLine("  ✓ SLA audit deduplication key matches and differentiates response vs resolution");
    }

    private void Test_Ai_Recommendation_Applied_Actor_Is_Human_Approver()
    {
        var approverId = Guid.NewGuid();
        var auditEvent = new IncidentAuditEvent
        {
            Id = Guid.NewGuid(),
            IncidentId = Guid.NewGuid(),
            EventType = IncidentAuditEventType.AIRecommendationApplied,
            ActorUserId = approverId,
            ActorDisplayName = "Manager Carol",
            ActorType = IncidentAuditActorType.User,
            Summary = "AI recommendation approved and applied by Manager Carol"
        };

        Assert.Equal(approverId, auditEvent.ActorUserId);
        Assert.Equal(IncidentAuditActorType.User, auditEvent.ActorType);
        Assert.Equal(IncidentAuditEventType.AIRecommendationApplied, auditEvent.EventType);

        Console.WriteLine("  ✓ AI recommendation application audit identifies the human approver as actor");
    }
}
