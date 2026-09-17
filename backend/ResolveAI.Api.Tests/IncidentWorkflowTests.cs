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

        Test_Open_To_Triaged_Allowed_For_Manager();
        Test_Open_To_Assigned_Directly_Rejected();
        Test_Open_To_Resolved_Directly_Rejected();
        Test_Triaged_To_Assigned_Requires_Assignment_Flow();
        Test_Assigned_To_InProgress_Allowed_For_Assigned_Technician();
        Test_Assigned_To_InProgress_Rejected_For_Unrelated_Technician();
        Test_InProgress_To_WaitingForUser_Allowed();
        Test_WaitingForUser_To_InProgress_Allowed();
        Test_InProgress_To_Resolved_Allowed_For_Assigned_Technician();
        Test_InProgress_To_Resolved_Forbidden_For_Employee();
        Test_Resolved_To_Closed_Allowed_For_Reporter_Employee();
        Test_Closed_Is_Terminal_No_Transitions();
        Test_Assignment_Requires_Manager_Or_Admin();
        Test_Assignment_On_Open_Incident_Rejected();
        Test_Assignment_On_Triaged_Incident_Allowed();
    }

    private void Test_Open_To_Triaged_Allowed_For_Manager()
    {
        var incident = new Incident { Status = IncidentStatus.Open };
        var decision = _service.ValidateStatusChange(incident, IncidentStatus.Triaged, Guid.NewGuid(), "Manager");
        Assert.True(decision.IsAllowed, "Manager should be allowed to triage Open incident");
        Console.WriteLine("  ✓ Open -> Triaged allowed for Manager");
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

    private void Test_Assigned_To_InProgress_Allowed_For_Assigned_Technician()
    {
        var technicianId = Guid.NewGuid();
        var incident = new Incident { Status = IncidentStatus.Assigned, AssignedToId = technicianId };
        var decision = _service.ValidateStatusChange(incident, IncidentStatus.InProgress, technicianId, "Technician");
        Assert.True(decision.IsAllowed, "Assigned technician should be allowed to start work");
        Console.WriteLine("  ✓ Assigned -> InProgress allowed for assigned technician");
    }

    private void Test_Assigned_To_InProgress_Rejected_For_Unrelated_Technician()
    {
        var technicianId = Guid.NewGuid();
        var otherTechId = Guid.NewGuid();
        var incident = new Incident { Status = IncidentStatus.Assigned, AssignedToId = technicianId };
        var decision = _service.ValidateStatusChange(incident, IncidentStatus.InProgress, otherTechId, "Technician");
        Assert.False(decision.IsAllowed, "Unrelated technician should not modify incident");
        Console.WriteLine("  ✓ Assigned -> InProgress rejected for unrelated technician");
    }

    private void Test_InProgress_To_WaitingForUser_Allowed()
    {
        var technicianId = Guid.NewGuid();
        var incident = new Incident { Status = IncidentStatus.InProgress, AssignedToId = technicianId };
        var decision = _service.ValidateStatusChange(incident, IncidentStatus.WaitingForUser, technicianId, "Technician");
        Assert.True(decision.IsAllowed, "Technician can transition to WaitingForUser");
        Console.WriteLine("  ✓ InProgress -> WaitingForUser allowed for assigned technician");
    }

    private void Test_WaitingForUser_To_InProgress_Allowed()
    {
        var technicianId = Guid.NewGuid();
        var incident = new Incident { Status = IncidentStatus.WaitingForUser, AssignedToId = technicianId };
        var decision = _service.ValidateStatusChange(incident, IncidentStatus.InProgress, technicianId, "Technician");
        Assert.True(decision.IsAllowed, "Technician can resume work to InProgress");
        Console.WriteLine("  ✓ WaitingForUser -> InProgress allowed for assigned technician");
    }

    private void Test_InProgress_To_Resolved_Allowed_For_Assigned_Technician()
    {
        var technicianId = Guid.NewGuid();
        var incident = new Incident { Status = IncidentStatus.InProgress, AssignedToId = technicianId };
        var decision = _service.ValidateStatusChange(incident, IncidentStatus.Resolved, technicianId, "Technician");
        Assert.True(decision.IsAllowed, "Assigned technician can resolve incident");
        Console.WriteLine("  ✓ InProgress -> Resolved allowed for assigned technician");
    }

    private void Test_InProgress_To_Resolved_Forbidden_For_Employee()
    {
        var reporterId = Guid.NewGuid();
        var incident = new Incident { Status = IncidentStatus.InProgress, ReporterId = reporterId, AssignedToId = Guid.NewGuid() };
        var decision = _service.ValidateStatusChange(incident, IncidentStatus.Resolved, reporterId, "Employee");
        Assert.False(decision.IsAllowed, "Employee cannot mark incident as resolved");
        Console.WriteLine("  ✓ InProgress -> Resolved forbidden for Employee");
    }

    private void Test_Resolved_To_Closed_Allowed_For_Reporter_Employee()
    {
        var reporterId = Guid.NewGuid();
        var incident = new Incident { Status = IncidentStatus.Resolved, ReporterId = reporterId };
        var decision = _service.ValidateStatusChange(incident, IncidentStatus.Closed, reporterId, "Employee");
        Assert.True(decision.IsAllowed, "Reporter should be allowed to close resolved incident");
        Console.WriteLine("  ✓ Resolved -> Closed allowed for Reporter Employee");
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
