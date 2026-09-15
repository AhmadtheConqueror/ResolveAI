using ResolveAI.Api.Entities;
using ResolveAI.Api.Enums;

namespace ResolveAI.Api.Services;

public class IncidentWorkflowService
{
    private static readonly Dictionary<IncidentStatus, IncidentStatus[]> AllowedTransitions = new()
    {
        [IncidentStatus.Open] = new[] { IncidentStatus.Triaged },
        [IncidentStatus.Triaged] = new[] { IncidentStatus.Assigned },
        [IncidentStatus.Assigned] = new[] { IncidentStatus.InProgress },
        [IncidentStatus.InProgress] = new[]
        {
            IncidentStatus.WaitingForUser,
            IncidentStatus.Resolved
        },
        [IncidentStatus.WaitingForUser] = new[] { IncidentStatus.InProgress },
        [IncidentStatus.Resolved] = new[] { IncidentStatus.Closed },
        [IncidentStatus.Closed] = Array.Empty<IncidentStatus>()
    };

    public IncidentWorkflowDecision ValidateAssignment(
        Incident incident,
        string? role)
    {
        if (role is not ("Manager" or "Admin"))
        {
            return IncidentWorkflowDecision.Forbid(
                "Only managers and admins can assign technicians."
            );
        }

        if (incident.Status == IncidentStatus.Open)
        {
            return IncidentWorkflowDecision.Reject(
                "Mark the incident as Triaged before assigning a technician."
            );
        }

        if (incident.Status is IncidentStatus.Triaged or IncidentStatus.Assigned)
        {
            return IncidentWorkflowDecision.Allow();
        }

        return IncidentWorkflowDecision.Reject(
            "Only Triaged or Assigned incidents can be assigned or reassigned."
        );
    }

    public IncidentWorkflowDecision ValidateStatusChange(
        Incident incident,
        IncidentStatus requestedStatus,
        Guid userId,
        string? role)
    {
        if (incident.Status == requestedStatus)
        {
            return IncidentWorkflowDecision.Reject(
                $"Incident is already {requestedStatus}."
            );
        }

        if (!IsAllowedTransition(incident.Status, requestedStatus))
        {
            return IncidentWorkflowDecision.Reject(
                $"Cannot change incident status from {incident.Status} to {requestedStatus}."
            );
        }

        if (incident.Status == IncidentStatus.Triaged &&
            requestedStatus == IncidentStatus.Assigned)
        {
            return IncidentWorkflowDecision.Reject(
                "Assign a technician to move this incident to Assigned."
            );
        }

        if (RequiresAssignedTechnician(requestedStatus) &&
            incident.AssignedToId is null)
        {
            return IncidentWorkflowDecision.Reject(
                "Incident must be assigned to a technician before this status change."
            );
        }

        return role switch
        {
            "Employee" => ValidateEmployeeStatusChange(
                incident,
                requestedStatus,
                userId
            ),
            "Technician" => ValidateTechnicianStatusChange(
                incident,
                requestedStatus,
                userId
            ),
            "Manager" or "Admin" => IncidentWorkflowDecision.Allow(),
            _ => IncidentWorkflowDecision.Forbid(
                "You do not have permission to update this incident."
            )
        };
    }

    public bool CanComment(
        Incident incident,
        Guid userId,
        string? role)
    {
        return role switch
        {
            "Employee" => incident.ReporterId == userId,
            "Technician" => incident.AssignedToId == userId,
            "Manager" or "Admin" => true,
            _ => false
        };
    }

    public void ApplyStatus(
        Incident incident,
        IncidentStatus requestedStatus,
        DateTime timestamp)
    {
        incident.Status = requestedStatus;
        incident.UpdatedAt = timestamp;

        if (requestedStatus == IncidentStatus.Resolved &&
            incident.ResolvedAt is null)
        {
            incident.ResolvedAt = timestamp;
        }

        if (requestedStatus == IncidentStatus.Closed &&
            incident.ClosedAt is null)
        {
            incident.ClosedAt = timestamp;
        }
    }

    private static bool IsAllowedTransition(
        IncidentStatus currentStatus,
        IncidentStatus requestedStatus)
    {
        return AllowedTransitions.TryGetValue(
                currentStatus,
                out var nextStatuses
            ) &&
            nextStatuses.Contains(requestedStatus);
    }

    private static bool RequiresAssignedTechnician(
        IncidentStatus requestedStatus)
    {
        return requestedStatus is
            IncidentStatus.InProgress or
            IncidentStatus.WaitingForUser or
            IncidentStatus.Resolved;
    }

    private static IncidentWorkflowDecision ValidateEmployeeStatusChange(
        Incident incident,
        IncidentStatus requestedStatus,
        Guid userId)
    {
        if (incident.ReporterId == userId &&
            incident.Status == IncidentStatus.Resolved &&
            requestedStatus == IncidentStatus.Closed)
        {
            return IncidentWorkflowDecision.Allow();
        }

        return IncidentWorkflowDecision.Forbid(
            "Employees can only close their own resolved incidents."
        );
    }

    private static IncidentWorkflowDecision ValidateTechnicianStatusChange(
        Incident incident,
        IncidentStatus requestedStatus,
        Guid userId)
    {
        if (incident.AssignedToId != userId)
        {
            return IncidentWorkflowDecision.Forbid(
                "Technicians can only update incidents assigned to them."
            );
        }

        return requestedStatus switch
        {
            IncidentStatus.InProgress or
            IncidentStatus.WaitingForUser or
            IncidentStatus.Resolved => IncidentWorkflowDecision.Allow(),
            _ => IncidentWorkflowDecision.Forbid(
                "Technicians cannot perform this status change."
            )
        };
    }
}
