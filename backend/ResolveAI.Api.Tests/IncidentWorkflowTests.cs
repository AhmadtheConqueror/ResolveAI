using ResolveAI.Api.Entities;
using ResolveAI.Api.Enums;
using ResolveAI.Api.Services;

namespace ResolveAI.Api.Tests;

public class IncidentWorkflowTests
{
    private readonly IncidentWorkflowService _service = new();

    public void RunAllTests()
    {
        Console.WriteLine("\n--- IncidentWorkflowTests ---");

        Test_Assigned_To_InProgress_Allowed_For_Assigned_Technician();
        Test_InProgress_To_WaitingForUser_Allowed();
        Test_WaitingForUser_To_InProgress_Allowed();
        Test_InProgress_To_Resolved_Allowed_For_Assigned_Technician();
        Test_Unrelated_Technician_Cannot_Execute_Technical_Transitions();
        Test_Manager_Cannot_Start_Work_Or_Resolve_Technician_Incident();
        Test_Admin_Cannot_Impersonate_Assigned_Technician_Technical_Workflow();
        Test_Resolved_To_Closed_Allowed_For_Reporter_Employee();
        Test_Resolved_To_Closed_Allowed_For_Manager_And_Admin();
        Test_Administrative_Closure_Allowed_For_Manager_And_Admin_With_Reason();
        Test_Administrative_Closure_Requires_Non_Empty_Reason();
        Test_Administrative_Closure_Forbidden_For_Technician_And_Employee();
        Test_ApplyStatus_Administrative_Closure_Does_Not_Record_Technician_Resolution();
        Test_ApplyStatus_Technical_Resolution_Records_Resolution();
        Test_Open_To_Triaged_Allowed_For_Manager_And_Admin();
        Test_Open_To_Triaged_Forbidden_For_Technician_And_Employee();
        Test_Open_To_Assigned_Directly_Rejected();
        Test_Open_To_Resolved_Directly_Rejected();
        Test_Triaged_To_Assigned_Requires_Assignment_Flow();
        Test_Closed_Is_Terminal_No_Transitions();
        Test_Assignment_Requires_Manager_Or_Admin();
        Test_Assignment_On_Open_Incident_Rejected();
        Test_Assignment_On_Triaged_Incident_Allowed();
    }

    private void Test_Assigned_To_InProgress_Allowed_For_Assigned_Technician()
    {
        var technicianId = Guid.NewGuid();
        var incident = new Incident { Status = IncidentStatus.Assigned, AssignedToId = technicianId };
        var decision = _service.ValidateStatusChange(incident, IncidentStatus.InProgress, technicianId, "Technician");
        Assert.True(decision.IsAllowed, "Assigned technician should be allowed to start work");
        Console.WriteLine("  ✓ 1. Assigned -> InProgress allowed for assigned technician");
    }

    private void Test_InProgress_To_WaitingForUser_Allowed()
    {
        var technicianId = Guid.NewGuid();
        var incident = new Incident { Status = IncidentStatus.InProgress, AssignedToId = technicianId };
        var decision = _service.ValidateStatusChange(incident, IncidentStatus.WaitingForUser, technicianId, "Technician");
        Assert.True(decision.IsAllowed, "Assigned technician can transition to WaitingForUser");
        Console.WriteLine("  ✓ 2. InProgress -> WaitingForUser allowed for assigned technician");
    }

    private void Test_WaitingForUser_To_InProgress_Allowed()
    {
        var technicianId = Guid.NewGuid();
        var incident = new Incident { Status = IncidentStatus.WaitingForUser, AssignedToId = technicianId };
        var decision = _service.ValidateStatusChange(incident, IncidentStatus.InProgress, technicianId, "Technician");
        Assert.True(decision.IsAllowed, "Assigned technician can resume work to InProgress");
        Console.WriteLine("  ✓ 3. WaitingForUser -> InProgress allowed for assigned technician");
    }

    private void Test_InProgress_To_Resolved_Allowed_For_Assigned_Technician()
    {
        var technicianId = Guid.NewGuid();
        var incident = new Incident { Status = IncidentStatus.InProgress, AssignedToId = technicianId };
        var decision = _service.ValidateStatusChange(incident, IncidentStatus.Resolved, technicianId, "Technician");
        Assert.True(decision.IsAllowed, "Assigned technician can resolve incident");
        Console.WriteLine("  ✓ 4. InProgress -> Resolved allowed for assigned technician");
    }

    private void Test_Unrelated_Technician_Cannot_Execute_Technical_Transitions()
    {
        var assignedTechId = Guid.NewGuid();
        var unrelatedTechId = Guid.NewGuid();

        var assignedIncident = new Incident { Status = IncidentStatus.Assigned, AssignedToId = assignedTechId };
        var inProgressIncident = new Incident { Status = IncidentStatus.InProgress, AssignedToId = assignedTechId };
        var waitingIncident = new Incident { Status = IncidentStatus.WaitingForUser, AssignedToId = assignedTechId };

        var d1 = _service.ValidateStatusChange(assignedIncident, IncidentStatus.InProgress, unrelatedTechId, "Technician");
        var d2 = _service.ValidateStatusChange(inProgressIncident, IncidentStatus.WaitingForUser, unrelatedTechId, "Technician");
        var d3 = _service.ValidateStatusChange(waitingIncident, IncidentStatus.InProgress, unrelatedTechId, "Technician");
        var d4 = _service.ValidateStatusChange(inProgressIncident, IncidentStatus.Resolved, unrelatedTechId, "Technician");

        Assert.False(d1.IsAllowed, "Unrelated technician cannot start work");
        Assert.True(d1.IsForbidden, "Unrelated technician start work must be 403 Forbidden");

        Assert.False(d2.IsAllowed, "Unrelated technician cannot mark waiting for user");
        Assert.True(d2.IsForbidden, "Unrelated technician waiting for user must be 403 Forbidden");

        Assert.False(d3.IsAllowed, "Unrelated technician cannot resume work");
        Assert.True(d3.IsForbidden, "Unrelated technician resume work must be 403 Forbidden");

        Assert.False(d4.IsAllowed, "Unrelated technician cannot resolve incident");
        Assert.True(d4.IsForbidden, "Unrelated technician resolve must be 403 Forbidden");

        Console.WriteLine("  ✓ 5. Unrelated technician cannot execute technical transitions (403 Forbidden)");
    }

    private void Test_Manager_Cannot_Start_Work_Or_Resolve_Technician_Incident()
    {
        var assignedTechId = Guid.NewGuid();
        var managerId = Guid.NewGuid();

        var assignedIncident = new Incident { Status = IncidentStatus.Assigned, AssignedToId = assignedTechId };
        var inProgressIncident = new Incident { Status = IncidentStatus.InProgress, AssignedToId = assignedTechId };

        var startWorkDecision = _service.ValidateStatusChange(assignedIncident, IncidentStatus.InProgress, managerId, "Manager");
        var resolveDecision = _service.ValidateStatusChange(inProgressIncident, IncidentStatus.Resolved, managerId, "Manager");

        Assert.False(startWorkDecision.IsAllowed, "Manager cannot start work on technician incident");
        Assert.True(startWorkDecision.IsForbidden, "Manager start work must be 403 Forbidden");

        Assert.False(resolveDecision.IsAllowed, "Manager cannot mark technician incident as resolved");
        Assert.True(resolveDecision.IsForbidden, "Manager resolve must be 403 Forbidden");

        Console.WriteLine("  ✓ 6 & 7. Manager cannot Start Work or Resolve technician-owned incident (403 Forbidden)");
    }

    private void Test_Admin_Cannot_Impersonate_Assigned_Technician_Technical_Workflow()
    {
        var assignedTechId = Guid.NewGuid();
        var adminId = Guid.NewGuid();

        var assignedIncident = new Incident { Status = IncidentStatus.Assigned, AssignedToId = assignedTechId };
        var inProgressIncident = new Incident { Status = IncidentStatus.InProgress, AssignedToId = assignedTechId };

        var startWorkDecision = _service.ValidateStatusChange(assignedIncident, IncidentStatus.InProgress, adminId, "Admin");
        var resolveDecision = _service.ValidateStatusChange(inProgressIncident, IncidentStatus.Resolved, adminId, "Admin");

        Assert.False(startWorkDecision.IsAllowed, "Admin cannot execute ordinary technical start work");
        Assert.True(startWorkDecision.IsForbidden, "Admin technical start work must be 403 Forbidden");

        Assert.False(resolveDecision.IsAllowed, "Admin cannot execute ordinary technical resolution");
        Assert.True(resolveDecision.IsForbidden, "Admin technical resolution must be 403 Forbidden");

        Console.WriteLine("  ✓ 8. Admin cannot execute technical workflow to impersonate technician (403 Forbidden)");
    }

    private void Test_Resolved_To_Closed_Allowed_For_Reporter_Employee()
    {
        var reporterId = Guid.NewGuid();
        var otherEmployeeId = Guid.NewGuid();
        var incident = new Incident { Status = IncidentStatus.Resolved, ReporterId = reporterId };

        var reporterDecision = _service.ValidateStatusChange(incident, IncidentStatus.Closed, reporterId, "Employee");
        var otherDecision = _service.ValidateStatusChange(incident, IncidentStatus.Closed, otherEmployeeId, "Employee");

        Assert.True(reporterDecision.IsAllowed, "Reporter should be allowed to close resolved incident");
        Assert.False(otherDecision.IsAllowed, "Non-reporter employee cannot close resolved incident");
        Assert.True(otherDecision.IsForbidden, "Non-reporter employee closure must be 403 Forbidden");

        Console.WriteLine("  ✓ 9. Reporter can close own Resolved incident; other employee forbidden");
    }

    private void Test_Resolved_To_Closed_Allowed_For_Manager_And_Admin()
    {
        var incident = new Incident { Status = IncidentStatus.Resolved, ReporterId = Guid.NewGuid(), AssignedToId = Guid.NewGuid() };
        var mgrDecision = _service.ValidateStatusChange(incident, IncidentStatus.Closed, Guid.NewGuid(), "Manager");
        var adminDecision = _service.ValidateStatusChange(incident, IncidentStatus.Closed, Guid.NewGuid(), "Admin");
        var techDecision = _service.ValidateStatusChange(incident, IncidentStatus.Closed, incident.AssignedToId.Value, "Technician");

        Assert.True(mgrDecision.IsAllowed, "Manager can close resolved incident for operational oversight");
        Assert.True(adminDecision.IsAllowed, "Admin can close resolved incident for governance");
        Assert.False(techDecision.IsAllowed, "Technician cannot close resolved incident");
        Assert.True(techDecision.IsForbidden, "Technician closure must be 403 Forbidden");

        Console.WriteLine("  ✓ 10 & 11. Manager and Admin can close Resolved incident; Technician forbidden");
    }

    private void Test_Administrative_Closure_Allowed_For_Manager_And_Admin_With_Reason()
    {
        var statuses = new[]
        {
            IncidentStatus.Open,
            IncidentStatus.Triaged,
            IncidentStatus.Assigned,
            IncidentStatus.InProgress,
            IncidentStatus.WaitingForUser
        };

        foreach (var status in statuses)
        {
            var incident = new Incident { Status = status, AssignedToId = Guid.NewGuid() };
            var mgrDecision = _service.ValidateStatusChange(incident, IncidentStatus.Closed, Guid.NewGuid(), "Manager", "Duplicate of INC-2026-0001");
            var adminDecision = _service.ValidateStatusChange(incident, IncidentStatus.Closed, Guid.NewGuid(), "Admin", "Incident created in error");

            Assert.True(mgrDecision.IsAllowed, $"Manager should be allowed administrative closure from {status}");
            Assert.True(adminDecision.IsAllowed, $"Admin should be allowed administrative closure from {status}");
        }

        Console.WriteLine("  ✓ 12. Administrative closure allowed for Manager and Admin across non-resolved statuses with reason");
    }

    private void Test_Administrative_Closure_Requires_Non_Empty_Reason()
    {
        var incident = new Incident { Status = IncidentStatus.InProgress, AssignedToId = Guid.NewGuid() };

        var nullReason = _service.ValidateStatusChange(incident, IncidentStatus.Closed, Guid.NewGuid(), "Manager", null);
        var emptyReason = _service.ValidateStatusChange(incident, IncidentStatus.Closed, Guid.NewGuid(), "Manager", "");
        var whitespaceReason = _service.ValidateStatusChange(incident, IncidentStatus.Closed, Guid.NewGuid(), "Admin", "   ");

        Assert.False(nullReason.IsAllowed, "Null reason must be rejected");
        Assert.False(nullReason.IsForbidden, "Missing reason is 400 Bad Request, not 403 Forbidden");

        Assert.False(emptyReason.IsAllowed, "Empty reason must be rejected");
        Assert.False(emptyReason.IsForbidden, "Empty reason is 400 Bad Request");

        Assert.False(whitespaceReason.IsAllowed, "Whitespace reason must be rejected");
        Assert.False(whitespaceReason.IsForbidden, "Whitespace reason is 400 Bad Request");

        Console.WriteLine("  ✓ 12b. Administrative closure strictly requires non-empty reason (400 Bad Request if missing)");
    }

    private void Test_Administrative_Closure_Forbidden_For_Technician_And_Employee()
    {
        var incident = new Incident { Status = IncidentStatus.InProgress, AssignedToId = Guid.NewGuid(), ReporterId = Guid.NewGuid() };

        var techDecision = _service.ValidateStatusChange(incident, IncidentStatus.Closed, incident.AssignedToId.Value, "Technician", "Duplicate request");
        var empDecision = _service.ValidateStatusChange(incident, IncidentStatus.Closed, incident.ReporterId, "Employee", "Changed mind");

        Assert.False(techDecision.IsAllowed, "Technician cannot administratively close incident");
        Assert.True(techDecision.IsForbidden, "Technician administrative closure must be 403 Forbidden");

        Assert.False(empDecision.IsAllowed, "Employee cannot administratively close incident");
        Assert.True(empDecision.IsForbidden, "Employee administrative closure must be 403 Forbidden");

        Console.WriteLine("  ✓ 12c. Administrative closure forbidden for Technician and Employee (403 Forbidden)");
    }

    private void Test_ApplyStatus_Administrative_Closure_Does_Not_Record_Technician_Resolution()
    {
        var now = DateTime.UtcNow;
        var incident = new Incident
        {
            Status = IncidentStatus.Assigned,
            AssignedToId = Guid.NewGuid(),
            FirstRespondedAt = null,
            ResolvedAt = null,
            Resolution = null
        };

        _service.ApplyStatus(incident, IncidentStatus.Closed, now);

        Assert.Equal(IncidentStatus.Closed, incident.Status);
        Assert.Equal(now, incident.ClosedAt);
        Assert.Null(incident.ResolvedAt, "Administrative closure must NEVER set ResolvedAt");
        Assert.Null(incident.Resolution, "Administrative closure must NEVER set Resolution");
        Assert.Null(incident.FirstRespondedAt, "Administrative closure must NOT fake a technician FirstResponse");

        Console.WriteLine("  ✓ 13 & 14. Administrative closure sets ClosedAt without faking ResolvedAt, Resolution, or FirstResponse");
    }

    private void Test_ApplyStatus_Technical_Resolution_Records_Resolution()
    {
        var now = DateTime.UtcNow;
        var incident = new Incident
        {
            Status = IncidentStatus.InProgress,
            AssignedToId = Guid.NewGuid(),
            FirstRespondedAt = null,
            ResolvedAt = null,
            Resolution = null
        };

        _service.ApplyStatus(incident, IncidentStatus.Resolved, now, "Replaced faulty switch port");

        Assert.Equal(IncidentStatus.Resolved, incident.Status);
        Assert.Equal(now, incident.ResolvedAt);
        Assert.Equal("Replaced faulty switch port", incident.Resolution);
        Assert.Equal(now, incident.FirstRespondedAt);

        Console.WriteLine("  ✓ 14b. Technical resolution properly populates ResolvedAt, Resolution, and FirstRespondedAt");
    }

    private void Test_Open_To_Triaged_Allowed_For_Manager_And_Admin()
    {
        var incident = new Incident { Status = IncidentStatus.Open };
        var mgrDecision = _service.ValidateStatusChange(incident, IncidentStatus.Triaged, Guid.NewGuid(), "Manager");
        var adminDecision = _service.ValidateStatusChange(incident, IncidentStatus.Triaged, Guid.NewGuid(), "Admin");

        Assert.True(mgrDecision.IsAllowed, "Manager should be allowed to triage Open incident");
        Assert.True(adminDecision.IsAllowed, "Admin should be allowed to triage Open incident");
        Console.WriteLine("  ✓ Open -> Triaged allowed for Manager and Admin");
    }

    private void Test_Open_To_Triaged_Forbidden_For_Technician_And_Employee()
    {
        var incident = new Incident { Status = IncidentStatus.Open };
        var techDecision = _service.ValidateStatusChange(incident, IncidentStatus.Triaged, Guid.NewGuid(), "Technician");
        var empDecision = _service.ValidateStatusChange(incident, IncidentStatus.Triaged, Guid.NewGuid(), "Employee");

        Assert.False(techDecision.IsAllowed, "Technician cannot triage");
        Assert.True(techDecision.IsForbidden, "Technician triage must be 403 Forbidden");

        Assert.False(empDecision.IsAllowed, "Employee cannot triage");
        Assert.True(empDecision.IsForbidden, "Employee triage must be 403 Forbidden");
        Console.WriteLine("  ✓ Open -> Triaged forbidden for Technician and Employee");
    }

    private void Test_Open_To_Assigned_Directly_Rejected()
    {
        var incident = new Incident { Status = IncidentStatus.Open };
        var decision = _service.ValidateStatusChange(incident, IncidentStatus.Assigned, Guid.NewGuid(), "Manager");
        Assert.False(decision.IsAllowed, "Direct transition from Open to Assigned should be rejected");
        Console.WriteLine("  ✓ Open -> Assigned directly rejected");
    }

    private void Test_Open_To_Resolved_Directly_Rejected()
    {
        var incident = new Incident { Status = IncidentStatus.Open };
        var decision = _service.ValidateStatusChange(incident, IncidentStatus.Resolved, Guid.NewGuid(), "Manager");
        Assert.False(decision.IsAllowed, "Direct transition from Open to Resolved should be rejected");
        Console.WriteLine("  ✓ Open -> Resolved directly rejected");
    }

    private void Test_Triaged_To_Assigned_Requires_Assignment_Flow()
    {
        var incident = new Incident { Status = IncidentStatus.Triaged };
        var decision = _service.ValidateStatusChange(incident, IncidentStatus.Assigned, Guid.NewGuid(), "Manager");
        Assert.False(decision.IsAllowed, "Status change to Assigned should require technician assignment");
        Console.WriteLine("  ✓ Triaged -> Assigned rejected via direct status patch");
    }

    private void Test_Closed_Is_Terminal_No_Transitions()
    {
        var incident = new Incident { Status = IncidentStatus.Closed };
        var decision = _service.ValidateStatusChange(incident, IncidentStatus.InProgress, Guid.NewGuid(), "Admin");
        Assert.False(decision.IsAllowed, "Closed incident should not transition to InProgress");
        Console.WriteLine("  ✓ Closed is terminal; transitions rejected");
    }

    private void Test_Assignment_Requires_Manager_Or_Admin()
    {
        var incident = new Incident { Status = IncidentStatus.Triaged };
        var empDecision = _service.ValidateAssignment(incident, "Employee");
        var techDecision = _service.ValidateAssignment(incident, "Technician");
        var mgrDecision = _service.ValidateAssignment(incident, "Manager");
        var adminDecision = _service.ValidateAssignment(incident, "Admin");

        Assert.False(empDecision.IsAllowed, "Employee cannot assign");
        Assert.False(techDecision.IsAllowed, "Technician cannot assign");
        Assert.True(mgrDecision.IsAllowed, "Manager can assign");
        Assert.True(adminDecision.IsAllowed, "Admin can assign");
        Console.WriteLine("  ✓ Assignment restricted to Manager and Admin");
    }

    private void Test_Assignment_On_Open_Incident_Rejected()
    {
        var incident = new Incident { Status = IncidentStatus.Open };
        var decision = _service.ValidateAssignment(incident, "Manager");
        Assert.False(decision.IsAllowed, "Open incident must be Triaged before assignment");
        Console.WriteLine("  ✓ Assignment on Open incident rejected");
    }

    private void Test_Assignment_On_Triaged_Incident_Allowed()
    {
        var incident = new Incident { Status = IncidentStatus.Triaged };
        var decision = _service.ValidateAssignment(incident, "Manager");
        Assert.True(decision.IsAllowed, "Triaged incident can be assigned");
        Console.WriteLine("  ✓ Assignment on Triaged incident allowed");
    }
}
