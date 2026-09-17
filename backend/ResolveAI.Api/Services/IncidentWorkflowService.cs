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
        string? role,
        string? reason = null)
    {
        if (incident.Status == requestedStatus)
        {
            return IncidentWorkflowDecision.Reject(
                $"Incident is already {requestedStatus}."
            );
        }

        // Administrative closure: Manager or Admin closing an incident outside Resolved -> Closed
        if (requestedStatus == IncidentStatus.Closed && incident.Status != IncidentStatus.Resolved)
        {
            if (incident.Status == IncidentStatus.Closed)
            {
                return IncidentWorkflowDecision.Reject("Cannot change incident status from Closed to Closed.");
            }

            if (role is not ("Manager" or "Admin"))
            {
                return IncidentWorkflowDecision.Forbid(
                    "Only managers and admins can administratively close an incident."
                );
            }

            if (string.IsNullOrWhiteSpace(reason))
            {
                return IncidentWorkflowDecision.Reject(
                    "A non-empty reason is required for administrative closure."
                );
            }

            return IncidentWorkflowDecision.Allow();
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

        // Technical transitions: strictly restricted to currently assigned technician
        if (IsTechnicalTransition(incident.Status, requestedStatus))
        {
            if (role != "Technician")
            {
                return IncidentWorkflowDecision.Forbid(
                    "Only the assigned technician can perform technical lifecycle actions."
                );
            }

            if (incident.AssignedToId != userId)
            {
                return IncidentWorkflowDecision.Forbid(
                    "Technicians can only update incidents assigned to them."
                );
            }

            return IncidentWorkflowDecision.Allow();
        }

        // Triage: Open -> Triaged
        if (incident.Status == IncidentStatus.Open && requestedStatus == IncidentStatus.Triaged)
        {
            if (role is ("Manager" or "Admin"))
            {
                return IncidentWorkflowDecision.Allow();
            }

            return IncidentWorkflowDecision.Forbid(
                "Only managers and admins can triage incidents."
            );
        }

        // Normal closure: Resolved -> Closed
        if (incident.Status == IncidentStatus.Resolved && requestedStatus == IncidentStatus.Closed)
        {
            return role switch
            {
                "Employee" => incident.ReporterId == userId
                    ? IncidentWorkflowDecision.Allow()
                    : IncidentWorkflowDecision.Forbid("Employees can only close their own resolved incidents."),
                "Manager" or "Admin" => IncidentWorkflowDecision.Allow(),
                "Technician" => IncidentWorkflowDecision.Forbid("Technicians cannot close resolved incidents."),
                _ => IncidentWorkflowDecision.Forbid("You do not have permission to update this incident.")
            };
        }

        return IncidentWorkflowDecision.Forbid(
            "You do not have permission to update this incident."
        );
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
        DateTime timestamp,
        string? resolution = null)
    {
        var isAdministrativeClosure = requestedStatus == IncidentStatus.Closed &&
            incident.Status != IncidentStatus.Resolved;

        incident.Status = requestedStatus;
        incident.UpdatedAt = timestamp;

        if (!isAdministrativeClosure &&
            requestedStatus != IncidentStatus.Open &&
            incident.FirstRespondedAt is null)
        {
            incident.FirstRespondedAt = timestamp;
        }

        if (requestedStatus == IncidentStatus.Resolved)
        {
            if (incident.ResolvedAt is null)
            {
                incident.ResolvedAt = timestamp;
            }

            if (!string.IsNullOrWhiteSpace(resolution))
            {
                incident.Resolution = resolution.Trim();
            }
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

    private static bool IsTechnicalTransition(
        IncidentStatus currentStatus,
        IncidentStatus requestedStatus)
    {
        return (currentStatus, requestedStatus) switch
        {
            (IncidentStatus.Assigned, IncidentStatus.InProgress) => true,
            (IncidentStatus.InProgress, IncidentStatus.WaitingForUser) => true,
            (IncidentStatus.WaitingForUser, IncidentStatus.InProgress) => true,
            (IncidentStatus.InProgress, IncidentStatus.Resolved) => true,
            _ => false
        };
    }

    private static bool RequiresAssignedTechnician(
        IncidentStatus requestedStatus)
    {
        return requestedStatus is
            IncidentStatus.InProgress or
            IncidentStatus.WaitingForUser or
            IncidentStatus.Resolved;
    }
}
