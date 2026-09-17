using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ResolveAI.Api.Data;
using ResolveAI.Api.DTOs;
using ResolveAI.Api.Entities;
using ResolveAI.Api.Enums;
using ResolveAI.Api.Models.Sla;
using ResolveAI.Api.Services;

namespace ResolveAI.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class IncidentsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IncidentWorkflowService _workflow;
    private readonly IAIIncidentService _aiIncidentService;
    private readonly ISlaService _slaService;
    private readonly INotificationService _notificationService;
    private readonly IIncidentAuditService _auditService;

    public IncidentsController(
        AppDbContext context,
        IncidentWorkflowService workflow,
        IAIIncidentService aiIncidentService,
        ISlaService slaService,
        INotificationService notificationService,
        IIncidentAuditService auditService)
    {
        _context = context;
        _workflow = workflow;
        _aiIncidentService = aiIncidentService;
        _slaService = slaService;
        _notificationService = notificationService;
        _auditService = auditService;
    }

    [HttpGet("options")]
    public async Task<IActionResult> GetIncidentOptions()
    {
        var categories = await _context.Categories
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new
            {
                id = c.Id,
                name = c.Name
            })
            .ToListAsync();

        var priorities = await _context.Priorities
            .AsNoTracking()
            .OrderBy(p => p.Level)
            .Select(p => new
            {
                id = p.Id,
                name = p.Name,
                level = p.Level
            })
            .ToListAsync();

        return Ok(new
        {
            categories,
            priorities
        });
    }

    [HttpGet]
    public async Task<IActionResult> GetIncidents([FromQuery] IncidentQueryParams queryParams)
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var role = User.FindFirstValue(ClaimTypes.Role);

        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized(new
            {
                message = "Invalid authenticated user."
            });
        }

        var query = _context.Incidents
            .AsNoTracking()
            .Include(i => i.Reporter)
            .Include(i => i.AssignedTo)
            .Include(i => i.Category)
            .Include(i => i.Priority)
            .AsQueryable();

        // 1. Role-based scoping BEFORE user filters
        if (role == "Employee")
        {
            query = query.Where(i => i.ReporterId == userId);
        }
        else if (role == "Technician")
        {
            // If Technician specifically queries assignedToId, verify it's themselves
            if (queryParams.AssignedToId.HasValue && queryParams.AssignedToId.Value != userId)
            {
                return Forbid();
            }

            query = query.Where(i =>
                i.AssignedToId == userId ||
                i.AssignedToId == null
            );
        }
        else if (role != "Manager" && role != "Admin")
        {
            return Forbid();
        }

        // 2. Search filter (incident number, title, reporter name, reporter email)
        if (!string.IsNullOrWhiteSpace(queryParams.Search))
        {
            var s = queryParams.Search.Trim().ToLower();
            query = query.Where(i =>
                i.IncidentNumber.ToLower().Contains(s) ||
                i.Title.ToLower().Contains(s) ||
                i.Reporter.FirstName.ToLower().Contains(s) ||
                i.Reporter.LastName.ToLower().Contains(s) ||
                (i.Reporter.FirstName + " " + i.Reporter.LastName).ToLower().Contains(s) ||
                i.Reporter.Email.ToLower().Contains(s)
            );
        }

        // 3. Status filter (single, comma-separated, or "active")
        if (!string.IsNullOrWhiteSpace(queryParams.Status) &&
            !queryParams.Status.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            if (queryParams.Status.Equals("active", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(i =>
                    i.Status != IncidentStatus.Resolved &&
                    i.Status != IncidentStatus.Closed
                );
            }
            else
            {
                var tokens = queryParams.Status.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var validStatuses = new List<IncidentStatus>();
                foreach (var token in tokens)
                {
                    if (Enum.TryParse<IncidentStatus>(token, ignoreCase: true, out var st) &&
                        Enum.IsDefined(typeof(IncidentStatus), st))
                    {
                        validStatuses.Add(st);
                    }
                }
                if (validStatuses.Count > 0)
                {
                    query = query.Where(i => validStatuses.Contains(i.Status));
                }
            }
        }

        // 4. Priority filter
        if (!string.IsNullOrWhiteSpace(queryParams.Priority) &&
            !queryParams.Priority.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            var p = queryParams.Priority.Trim();
            if (Guid.TryParse(p, out var prioId))
            {
                query = query.Where(i => i.PriorityId == prioId);
            }
            else
            {
                query = query.Where(i => i.Priority.Name.ToLower() == p.ToLower());
            }
        }

        // 5. Category filter
        if (!string.IsNullOrWhiteSpace(queryParams.Category) &&
            !queryParams.Category.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            var c = queryParams.Category.Trim();
            if (Guid.TryParse(c, out var catId))
            {
                query = query.Where(i => i.CategoryId == catId);
            }
            else
            {
                query = query.Where(i => i.Category.Name.ToLower() == c.ToLower());
            }
        }

        // 6. Assignment filter
        if (!string.IsNullOrWhiteSpace(queryParams.Assignment) &&
            !queryParams.Assignment.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            if (queryParams.Assignment.Equals("unassigned", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(i => i.AssignedToId == null);
            }
            else if (queryParams.Assignment.Equals("assigned", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(i => i.AssignedToId != null);
            }
        }

        // 7. Specific AssignedToId filter
        if (queryParams.AssignedToId.HasValue)
        {
            query = query.Where(i => i.AssignedToId == queryParams.AssignedToId.Value);
        }

        var rawIncidents = await query
            .Select(i => new
            {
                id = i.Id,
                incidentNumber = i.IncidentNumber,
                title = i.Title,
                status = i.Status.ToString(),
                rawStatus = i.Status,
                category = i.Category.Name,
                priority = i.Priority.Name,
                priorityLevel = i.Priority.Level,
                reporter = new
                {
                    id = i.Reporter.Id,
                    name = i.Reporter.FirstName + " " + i.Reporter.LastName,
                    email = i.Reporter.Email
                },
                assignedTo = i.AssignedTo == null
                    ? null
                    : new
                    {
                        id = i.AssignedTo.Id,
                        name = i.AssignedTo.FirstName + " " + i.AssignedTo.LastName,
                        email = i.AssignedTo.Email
                    },
                resolution = i.Resolution,
                createdAt = i.CreatedAt,
                updatedAt = i.UpdatedAt,
                firstRespondedAt = i.FirstRespondedAt,
                resolvedAt = i.ResolvedAt,
                closedAt = i.ClosedAt
            })
            .ToListAsync();

        var now = DateTime.UtcNow;
        var withSla = rawIncidents.Select(i =>
        {
            var sla = _slaService.CalculateDetail(
                i.createdAt,
                i.priority,
                i.firstRespondedAt,
                i.resolvedAt,
                now
            );

            return new
            {
                i.id,
                i.incidentNumber,
                i.title,
                i.status,
                i.rawStatus,
                i.category,
                i.priority,
                i.priorityLevel,
                i.reporter,
                i.assignedTo,
                i.resolution,
                i.createdAt,
                i.updatedAt,
                i.firstRespondedAt,
                i.resolvedAt,
                i.closedAt,
                sla = new
                {
                    overallStatus = sla.OverallStatus,
                    responseStatus = sla.ResponseStatus,
                    resolutionStatus = sla.ResolutionStatus,
                    responseDueAt = sla.ResponseDueAt,
                    resolutionDueAt = sla.ResolutionDueAt,
                    requiresEscalation = sla.RequiresEscalation,
                    responseRemainingMinutes = sla.ResponseRemainingMinutes,
                    responseOverdueMinutes = sla.ResponseOverdueMinutes,
                    resolutionRemainingMinutes = sla.ResolutionRemainingMinutes,
                    resolutionOverdueMinutes = sla.ResolutionOverdueMinutes
                }
            };
        });

        // 8. SLA filter
        if (!string.IsNullOrWhiteSpace(queryParams.SlaStatus) &&
            !queryParams.SlaStatus.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            var slaTarget = queryParams.SlaStatus.Trim().Replace(" ", "");
            if (slaTarget.Equals("AtRiskBreached", StringComparison.OrdinalIgnoreCase) ||
                slaTarget.Equals("Attention", StringComparison.OrdinalIgnoreCase))
            {
                withSla = withSla.Where(x =>
                    x.sla.overallStatus.Equals("Breached", StringComparison.OrdinalIgnoreCase) ||
                    x.sla.overallStatus.Equals("AtRisk", StringComparison.OrdinalIgnoreCase)
                );
            }
            else
            {
                withSla = withSla.Where(x =>
                    x.sla.overallStatus.Equals(slaTarget, StringComparison.OrdinalIgnoreCase)
                );
            }
        }

        // 9. Sorting
        var sortBy = (queryParams.SortBy ?? "updatedAt").Trim().ToLower();
        var isAsc = (queryParams.SortDirection ?? "desc").Trim().Equals("asc", StringComparison.OrdinalIgnoreCase);

        var sorted = sortBy switch
        {
            "urgency" => withSla.OrderBy(x =>
                x.sla.overallStatus == "Breached" ? 0 :
                x.sla.overallStatus == "AtRisk" ? 1 : 2
            ).ThenByDescending(x => x.priorityLevel)
             .ThenBy(x => x.createdAt),

            "priority" => isAsc
                ? withSla.OrderBy(x => x.priorityLevel).ThenBy(x => x.createdAt)
                : withSla.OrderByDescending(x => x.priorityLevel).ThenByDescending(x => x.createdAt),

            "createdat" => isAsc
                ? withSla.OrderBy(x => x.createdAt)
                : withSla.OrderByDescending(x => x.createdAt),

            "incidentnumber" => isAsc
                ? withSla.OrderBy(x => x.incidentNumber)
                : withSla.OrderByDescending(x => x.incidentNumber),

            _ => isAsc // default: "updatedAt"
                ? withSla.OrderBy(x => x.updatedAt)
                : withSla.OrderByDescending(x => x.updatedAt)
        };

        var finalItems = sorted.Select(x => new
        {
            id = x.id,
            incidentNumber = x.incidentNumber,
            title = x.title,
            status = x.status,
            category = x.category,
            priority = x.priority,
            reporter = x.reporter,
            assignedTo = x.assignedTo,
            resolution = x.resolution,
            createdAt = x.createdAt,
            updatedAt = x.updatedAt,
            firstRespondedAt = x.firstRespondedAt,
            resolvedAt = x.resolvedAt,
            closedAt = x.closedAt,
            sla = x.sla
        }).ToList();

        // 10. Server-side Pagination
        var hasPagingParam = queryParams.Page.HasValue ||
                             queryParams.PageSize.HasValue ||
                             Request.Query.ContainsKey("page") ||
                             Request.Query.ContainsKey("pageSize");

        if (hasPagingParam)
        {
            var page = Math.Max(1, queryParams.Page ?? 1);
            var pageSize = Math.Clamp(queryParams.PageSize ?? 25, 1, 100);
            var totalCount = finalItems.Count;
            var pagedItems = finalItems.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            return Ok(new PagedResult<object>(pagedItems, totalCount, page, pageSize));
        }

        return Ok(finalItems);
    }

    [HttpGet("queue-summary")]
    public async Task<IActionResult> GetQueueSummary()
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var role = User.FindFirstValue(ClaimTypes.Role);

        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized(new
            {
                message = "Invalid authenticated user."
            });
        }

        var now = DateTime.UtcNow;

        if (role is "Manager" or "Admin")
        {
            var operationalIncidents = await _context.Incidents
                .AsNoTracking()
                .Include(i => i.Priority)
                .Select(i => new
                {
                    status = i.Status,
                    assignedToId = i.AssignedToId,
                    createdAt = i.CreatedAt,
                    priority = i.Priority.Name,
                    firstRespondedAt = i.FirstRespondedAt,
                    resolvedAt = i.ResolvedAt
                })
                .ToListAsync();

            var triageCount = operationalIncidents.Count(i => i.status == IncidentStatus.Open);
            var unassignedCount = operationalIncidents.Count(i =>
                i.assignedToId == null &&
                (i.status == IncidentStatus.Triaged || i.status == IncidentStatus.Assigned)
            );
            var activeCount = operationalIncidents.Count(i =>
                i.status != IncidentStatus.Resolved &&
                i.status != IncidentStatus.Closed
            );
            var resolvedCount = operationalIncidents.Count(i => i.status == IncidentStatus.Resolved);

            var activeList = operationalIncidents
                .Where(i => i.status != IncidentStatus.Resolved && i.status != IncidentStatus.Closed)
                .ToList();

            var slaAttentionCount = activeList.Count(i =>
            {
                var sla = _slaService.CalculateDetail(
                    i.createdAt,
                    i.priority,
                    i.firstRespondedAt,
                    i.resolvedAt,
                    now
                );
                return sla.OverallStatus == SlaStatus.Breached.ToString() ||
                       sla.OverallStatus == SlaStatus.AtRisk.ToString();
            });

            return Ok(new
            {
                role,
                triageCount,
                unassignedCount,
                slaAttentionCount,
                activeCount,
                resolvedCount
            });
        }
        else if (role == "Technician")
        {
            var myIncidents = await _context.Incidents
                .AsNoTracking()
                .Include(i => i.Priority)
                .Where(i => i.AssignedToId == userId)
                .Select(i => new
                {
                    status = i.Status,
                    createdAt = i.CreatedAt,
                    priority = i.Priority.Name,
                    firstRespondedAt = i.FirstRespondedAt,
                    resolvedAt = i.ResolvedAt
                })
                .ToListAsync();

            var activeMyList = myIncidents
                .Where(i => i.status != IncidentStatus.Resolved && i.status != IncidentStatus.Closed)
                .ToList();

            var allAssignedCount = activeMyList.Count;
            var assignedCount = activeMyList.Count(i => i.status == IncidentStatus.Assigned);
            var inProgressCount = activeMyList.Count(i => i.status == IncidentStatus.InProgress);
            var waitingForUserCount = activeMyList.Count(i => i.status == IncidentStatus.WaitingForUser);

            var slaAttentionCount = activeMyList.Count(i =>
            {
                var sla = _slaService.CalculateDetail(
                    i.createdAt,
                    i.priority,
                    i.firstRespondedAt,
                    i.resolvedAt,
                    now
                );
                return sla.OverallStatus == SlaStatus.Breached.ToString() ||
                       sla.OverallStatus == SlaStatus.AtRisk.ToString();
            });

            return Ok(new
            {
                role,
                allAssignedCount,
                assignedCount,
                inProgressCount,
                waitingForUserCount,
                slaAttentionCount
            });
        }
        else
        {
            return Forbid();
        }
    }

    [HttpGet("workload")]
    [Authorize(Roles = "Manager,Admin")]
    public async Task<IActionResult> GetTechnicianWorkload()
    {
        var technicians = await _context.Users
            .AsNoTracking()
            .Include(u => u.Role)
            .Where(u => u.IsActive && u.Role.Name == "Technician")
            .OrderBy(u => u.LastName)
            .ThenBy(u => u.FirstName)
            .Select(u => new
            {
                id = u.Id,
                name = u.FirstName + " " + u.LastName,
                email = u.Email
            })
            .ToListAsync();

        var activeIncidents = await _context.Incidents
            .AsNoTracking()
            .Where(i => i.AssignedToId != null &&
                        i.Status != IncidentStatus.Resolved &&
                        i.Status != IncidentStatus.Closed)
            .Select(i => new
            {
                assignedToId = i.AssignedToId!.Value,
                status = i.Status
            })
            .ToListAsync();

        var workload = technicians.Select(t =>
        {
            var techIncidents = activeIncidents.Where(i => i.assignedToId == t.id).ToList();
            return new
            {
                technician = t,
                activeCount = techIncidents.Count,
                assignedCount = techIncidents.Count(i => i.status == IncidentStatus.Assigned),
                inProgressCount = techIncidents.Count(i => i.status == IncidentStatus.InProgress),
                waitingForUserCount = techIncidents.Count(i => i.status == IncidentStatus.WaitingForUser)
            };
        }).OrderByDescending(w => w.activeCount).ToList();

        return Ok(workload);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetIncident(Guid id)
    {
        var userIdClaim = User.FindFirstValue(
            ClaimTypes.NameIdentifier
        );
        var role = User.FindFirstValue(ClaimTypes.Role);

        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized(new
            {
                message = "Invalid authenticated user."
            });
        }

        var incident = await _context.Incidents
            .AsNoTracking()
            .Include(i => i.Reporter)
            .Include(i => i.AssignedTo)
            .Include(i => i.Category)
            .Include(i => i.Priority)
            .SingleOrDefaultAsync(i => i.Id == id);

        if (incident is null)
        {
            return NotFound(new
            {
                message = "Incident not found."
            });
        }

        if (!CanAccessIncident(role, userId, incident))
        {
            return Forbid();
        }

        return Ok(new
        {
            id = incident.Id,
            incidentNumber = incident.IncidentNumber,
            title = incident.Title,
            description = incident.Description,
            status = incident.Status.ToString(),
            category = incident.Category.Name,
            priority = new
            {
                name = incident.Priority.Name,
                level = incident.Priority.Level
            },
            reporter = new
            {
                id = incident.Reporter.Id,
                name = incident.Reporter.FirstName + " " + incident.Reporter.LastName,
                email = incident.Reporter.Email
            },
            assignedTo = incident.AssignedTo == null
                ? null
                : new
                {
                    id = incident.AssignedTo.Id,
                    name = incident.AssignedTo.FirstName + " " + incident.AssignedTo.LastName,
                    email = incident.AssignedTo.Email
                },
            resolution = incident.Resolution,
            createdAt = incident.CreatedAt,
            updatedAt = incident.UpdatedAt,
            firstRespondedAt = incident.FirstRespondedAt,
            resolvedAt = incident.ResolvedAt,
            closedAt = incident.ClosedAt,
            sla = _slaService.CalculateDetail(incident)
        });
    }

    [HttpGet("{id:guid}/activity")]
    public async Task<IActionResult> GetActivity(
        Guid id,
        [FromQuery] IncidentActivityQueryParams queryParams)
    {
        if (!TryGetAuthenticatedUser(out var userId, out var role))
        {
            return Unauthorized(new
            {
                message = "Invalid authenticated user."
            });
        }

        var incident = await _context.Incidents
            .AsNoTracking()
            .SingleOrDefaultAsync(i => i.Id == id);

        if (incident is null)
        {
            return NotFound(new
            {
                message = "Incident not found."
            });
        }

        if (!CanAccessIncident(role, userId, incident))
        {
            return Forbid();
        }

        var page = Math.Max(1, queryParams.Page);
        var pageSize = Math.Clamp(queryParams.PageSize, 1, 100);

        var query = _context.IncidentAuditEvents
            .AsNoTracking()
            .Where(a => a.IncidentId == id);

        var totalCount = await query.CountAsync();

        var auditEvents = await query
            .OrderByDescending(a => a.CreatedAt)
            .ThenByDescending(a => a.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var items = auditEvents.Select(a => new
        {
            id = a.Id,
            eventType = a.EventType.ToString(),
            summary = a.Summary,
            oldValue = a.OldValue,
            newValue = a.NewValue,
            actor = new
            {
                id = a.ActorUserId,
                name = a.ActorDisplayName
            },
            actorType = a.ActorType.ToString(),
            metadata = ParseAuditMetadata(a.Metadata),
            createdAt = a.CreatedAt
        }).ToList<object>();

        return Ok(new PagedResult<object>(
            items,
            totalCount,
            page,
            pageSize));
    }

    [HttpPatch("{id:guid}/assign")]
    [Authorize(Roles = "Manager,Admin")]
    public async Task<IActionResult> AssignTechnician(
        Guid id,
        AssignIncidentRequest request)
    {
        if (!TryGetAuthenticatedUser(out var userId, out var role))
        {
            return Unauthorized(new
            {
                message = "Invalid authenticated user."
            });
        }

        if (request.TechnicianId == Guid.Empty)
        {
            return BadRequest(new
            {
                message = "A technician is required."
            });
        }

        var incident = await _context.Incidents
            .Include(i => i.AssignedTo)
            .SingleOrDefaultAsync(i => i.Id == id);

        if (incident is null)
        {
            return NotFound(new
            {
                message = "Incident not found."
            });
        }

        var assignmentDecision = _workflow.ValidateAssignment(
            incident,
            role
        );

        if (!assignmentDecision.IsAllowed)
        {
            return WorkflowProblem(assignmentDecision);
        }

        var technician = await _context.Users
            .Include(u => u.Role)
            .SingleOrDefaultAsync(u =>
                u.Id == request.TechnicianId &&
                u.IsActive
            );

        if (technician is null ||
            technician.Role.Name != "Technician")
        {
            return BadRequest(new
            {
                message = "Selected technician is not available."
            });
        }

        var oldStatus = incident.Status;
        var oldAssigneeId = incident.AssignedToId;
        var oldAssigneeName = incident.AssignedTo == null
            ? null
            : $"{incident.AssignedTo.FirstName} {incident.AssignedTo.LastName}".Trim();
        var wasReassignment =
            incident.AssignedToId.HasValue &&
            incident.AssignedToId != technician.Id;
        var assignmentChanged = incident.AssignedToId != technician.Id;

        var timestamp = DateTime.UtcNow;

        incident.AssignedToId = technician.Id;

        if (incident.Status == IncidentStatus.Triaged)
        {
            incident.Status = IncidentStatus.Assigned;
        }

        incident.UpdatedAt = timestamp;

        if (assignmentChanged)
        {
            await _notificationService.QueueIncidentAssignedAsync(
                incident,
                technician,
                userId,
                wasReassignment,
                oldAssigneeName,
                HttpContext.RequestAborted);

            await _auditService.RecordAssignmentChangedAsync(
                incident,
                userId,
                oldAssigneeId,
                oldAssigneeName,
                technician.Id,
                $"{technician.FirstName} {technician.LastName}".Trim(),
                HttpContext.RequestAborted);
        }

        if (oldStatus != incident.Status)
        {
            await _auditService.RecordStatusChangedAsync(
                incident,
                oldStatus,
                incident.Status,
                userId,
                HttpContext.RequestAborted);
        }

        await _context.SaveChangesAsync();

        await _notificationService.StagePendingExternalDeliveriesAsync(HttpContext.RequestAborted);

        return Ok(new
        {
            id = incident.Id,
            status = incident.Status.ToString(),
            assignedTo = new
            {
                id = technician.Id,
                name = technician.FirstName + " " + technician.LastName,
                email = technician.Email
            },
            updatedAt = incident.UpdatedAt,
            assignmentType = wasReassignment
                ? "Reassigned"
                : "Assigned"
        });
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> UpdateStatus(
        Guid id,
        UpdateIncidentStatusRequest request)
    {
        if (!TryGetAuthenticatedUser(out var userId, out var role))
        {
            return Unauthorized(new
            {
                message = "Invalid authenticated user."
            });
        }

        var requestedStatusText = request.Status.Trim();

        if (!Enum.TryParse<IncidentStatus>(
                requestedStatusText,
                ignoreCase: true,
                out var requestedStatus
            ) ||
            !Enum.IsDefined(typeof(IncidentStatus), requestedStatus))
        {
            return BadRequest(new
            {
                message = "Invalid incident status."
            });
        }

        var incident = await _context.Incidents
            .SingleOrDefaultAsync(i => i.Id == id);

        if (incident is null)
        {
            return NotFound(new
            {
                message = "Incident not found."
            });
        }

        if (!CanAccessIncident(role, userId, incident))
        {
            return Forbid();
        }

        var statusDecision = _workflow.ValidateStatusChange(
            incident,
            requestedStatus,
            userId,
            role,
            request.Reason
        );

        if (!statusDecision.IsAllowed)
        {
            return WorkflowProblem(statusDecision);
        }

        if (requestedStatus == IncidentStatus.Resolved)
        {
            if (string.IsNullOrWhiteSpace(request.Resolution) || request.Resolution.Trim().Length < 5)
            {
                return BadRequest(new
                {
                    message = "A written resolution description (minimum 5 characters) is required when marking an incident as Resolved."
                });
            }
        }

        var oldStatus = incident.Status;
        var hadFirstResponse = incident.FirstRespondedAt.HasValue;
        var previousResolution = incident.Resolution;

        _workflow.ApplyStatus(
            incident,
            requestedStatus,
            DateTime.UtcNow,
            request.Resolution
        );

        await _notificationService.QueueIncidentStatusChangedAsync(
            incident,
            oldStatus,
            requestedStatus,
            userId,
            HttpContext.RequestAborted);

        await _auditService.RecordStatusChangedAsync(
            incident,
            oldStatus,
            requestedStatus,
            userId,
            request.Reason,
            HttpContext.RequestAborted);

        if (!hadFirstResponse && incident.FirstRespondedAt.HasValue)
        {
            await _auditService.RecordFirstResponseRecordedAsync(
                incident,
                userId,
                HttpContext.RequestAborted);
        }

        if (requestedStatus == IncidentStatus.Resolved &&
            !string.IsNullOrWhiteSpace(incident.Resolution) &&
            !string.Equals(
                previousResolution?.Trim(),
                incident.Resolution.Trim(),
                StringComparison.Ordinal))
        {
            await _auditService.RecordResolutionRecordedAsync(
                incident,
                userId,
                HttpContext.RequestAborted);
        }

        await _context.SaveChangesAsync();

        await _notificationService.StagePendingExternalDeliveriesAsync(HttpContext.RequestAborted);

        return Ok(new
        {
            id = incident.Id,
            status = incident.Status.ToString(),
            resolution = incident.Resolution,
            updatedAt = incident.UpdatedAt,
            firstRespondedAt = incident.FirstRespondedAt,
            resolvedAt = incident.ResolvedAt,
            closedAt = incident.ClosedAt
        });
    }

    [HttpGet("{id:guid}/comments")]
    public async Task<IActionResult> GetComments(Guid id)
    {
        if (!TryGetAuthenticatedUser(out var userId, out var role))
        {
            return Unauthorized(new
            {
                message = "Invalid authenticated user."
            });
        }

        var incident = await _context.Incidents
            .AsNoTracking()
            .SingleOrDefaultAsync(i => i.Id == id);

        if (incident is null)
        {
            return NotFound(new
            {
                message = "Incident not found."
            });
        }

        if (!CanAccessIncident(role, userId, incident))
        {
            return Forbid();
        }

        var comments = await _context.IncidentComments
            .AsNoTracking()
            .Where(c => c.IncidentId == id)
            .OrderBy(c => c.CreatedAt)
            .Select(c => new
            {
                id = c.Id,
                comment = c.Comment,
                createdAt = c.CreatedAt,
                author = new
                {
                    id = c.User.Id,
                    name = c.User.FirstName + " " + c.User.LastName,
                    role = c.User.Role.Name
                }
            })
            .ToListAsync();

        return Ok(comments);
    }

    [HttpPost("{id:guid}/comments")]
    public async Task<IActionResult> AddComment(
        Guid id,
        CreateIncidentCommentRequest request)
    {
        if (!TryGetAuthenticatedUser(out var userId, out var role))
        {
            return Unauthorized(new
            {
                message = "Invalid authenticated user."
            });
        }

        var commentText = request.Comment.Trim();

        if (string.IsNullOrWhiteSpace(commentText))
        {
            return BadRequest(new
            {
                message = "Comment cannot be empty."
            });
        }

        var incident = await _context.Incidents
            .SingleOrDefaultAsync(i => i.Id == id);

        if (incident is null)
        {
            return NotFound(new
            {
                message = "Incident not found."
            });
        }

        if (!CanAccessIncident(role, userId, incident) ||
            !_workflow.CanComment(incident, userId, role))
        {
            return Forbid();
        }

        var author = await _context.Users
            .AsNoTracking()
            .Include(u => u.Role)
            .SingleOrDefaultAsync(u =>
                u.Id == userId &&
                u.IsActive
            );

        if (author is null)
        {
            return Unauthorized(new
            {
                message = "User account is unavailable."
            });
        }

        var timestamp = DateTime.UtcNow;
        var comment = new IncidentComment
        {
            IncidentId = incident.Id,
            UserId = author.Id,
            Comment = commentText,
            CreatedAt = timestamp
        };

        incident.UpdatedAt = timestamp;
        _context.IncidentComments.Add(comment);

        await _notificationService.QueueIncidentCommentAddedAsync(
            incident,
            author,
            commentText,
            HttpContext.RequestAborted);

        await _auditService.RecordCommentAddedAsync(
            incident,
            author,
            HttpContext.RequestAborted);

        await _context.SaveChangesAsync();

        await _notificationService.StagePendingExternalDeliveriesAsync(HttpContext.RequestAborted);

        return Created(
            $"/api/incidents/{incident.Id}/comments/{comment.Id}",
            new
            {
                id = comment.Id,
                comment = comment.Comment,
                createdAt = comment.CreatedAt,
                author = new
                {
                    id = author.Id,
                    name = author.FirstName + " " + author.LastName,
                    role = author.Role.Name
                }
            }
        );
    }

    [HttpPost("{id:guid}/ai-analysis")]
    [EnableRateLimiting("ai-limiter")]
    public async Task<IActionResult> RunAIAnalysis(Guid id)
    {
        if (!TryGetAuthenticatedUser(out var userId, out var role))
        {
            return Unauthorized(new
            {
                message = "Invalid authenticated user."
            });
        }

        var incident = await _context.Incidents
            .Include(i => i.Category)
            .Include(i => i.Priority)
            .SingleOrDefaultAsync(i => i.Id == id);

        if (incident is null)
        {
            return NotFound(new
            {
                message = "Incident not found."
            });
        }

        if (!CanRunAIAnalysis(role, userId, incident))
        {
            return Forbid();
        }

        try
        {
            var result = await _aiIncidentService.AnalyzeIncidentAsync(
                incident,
                HttpContext.RequestAborted
            );

            var analysis = new IncidentAIAnalysis
            {
                IncidentId = incident.Id,
                RequestedByUserId = userId,
                Provider = result.Provider,
                Model = result.Model,
                CategoryRecommendation = result.CategoryRecommendation,
                PriorityRecommendation = result.PriorityRecommendation,
                Urgency = result.Urgency,
                Confidence = result.Confidence,
                PossibleCause = result.PossibleCause,
                ReasoningSummary = result.ReasoningSummary,
                SuggestedActionsJson = JsonSerializer.Serialize(
                    result.SuggestedActions
                ),
                CreatedAt = DateTime.UtcNow,
                PromptVersion = result.PromptVersion
            };

            _context.IncidentAIAnalyses.Add(analysis);

            await _auditService.RecordAIAnalysisGeneratedAsync(
                incident,
                analysis,
                userId,
                HttpContext.RequestAborted);

            await _context.SaveChangesAsync();

            return Created(
                $"/api/incidents/{incident.Id}/ai-analysis/{analysis.Id}",
                ToAIAnalysisResponse(analysis)
            );
        }
        catch (AIIncidentAnalysisException exception)
        {
            var message = exception.IsConfigurationError
                ? "AI analysis is not configured."
                : "AI analysis is temporarily unavailable. Please try again.";

            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new
                {
                    message
                }
            );
        }
    }

    [HttpGet("{id:guid}/ai-analysis")]
    public async Task<IActionResult> GetAIAnalyses(Guid id)
    {
        if (!TryGetAuthenticatedUser(out var userId, out var role))
        {
            return Unauthorized(new
            {
                message = "Invalid authenticated user."
            });
        }

        var incident = await _context.Incidents
            .AsNoTracking()
            .SingleOrDefaultAsync(i => i.Id == id);

        if (incident is null)
        {
            return NotFound(new
            {
                message = "Incident not found."
            });
        }

        if (!CanAccessIncident(role, userId, incident))
        {
            return Forbid();
        }

        var analyses = await _context.IncidentAIAnalyses
            .AsNoTracking()
            .Include(a => a.AppliedByUser)
            .Where(a => a.IncidentId == id)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();

        return Ok(analyses.Select(ToAIAnalysisResponse));
    }

    [HttpPatch("{id:guid}/ai-analysis/{analysisId:guid}/apply")]
    public async Task<IActionResult> ApplyAIRecommendation(
        Guid id,
        Guid analysisId,
        ApplyAIRecommendationRequest request)
    {
        if (!TryGetAuthenticatedUser(out var userId, out var role))
        {
            return Unauthorized(new
            {
                message = "Invalid authenticated user."
            });
        }

        if (role is not ("Manager" or "Admin"))
        {
            return Forbid();
        }

        if (!request.ApplyCategory && !request.ApplyPriority)
        {
            return BadRequest(new
            {
                message = "At least one recommendation option must be selected."
            });
        }

        var incident = await _context.Incidents
            .Include(i => i.Category)
            .Include(i => i.Priority)
            .SingleOrDefaultAsync(i => i.Id == id);

        if (incident is null)
        {
            return NotFound(new
            {
                message = "Incident not found."
            });
        }

        var analysis = await _context.IncidentAIAnalyses
            .SingleOrDefaultAsync(a => a.Id == analysisId);

        if (analysis is null || analysis.IncidentId != id)
        {
            return NotFound(new
            {
                message = "AI analysis not found for this incident."
            });
        }

        var previousCategoryName = incident.Category.Name;
        var previousPriorityName = incident.Priority.Name;
        var priorityChanged = false;

        if (request.ApplyCategory)
        {
            var targetCategoryName = analysis.CategoryRecommendation?.Trim();
            if (string.IsNullOrEmpty(targetCategoryName))
            {
                return BadRequest(new
                {
                    message = "Analysis does not contain a category recommendation."
                });
            }

            var matchedCategory = await _context.Categories
                .SingleOrDefaultAsync(c => EF.Functions.ILike(c.Name, targetCategoryName));

            if (matchedCategory is null)
            {
                return BadRequest(new
                {
                    message = $"Recommended category '{targetCategoryName}' is not a valid category."
                });
            }

            if (incident.CategoryId != matchedCategory.Id)
            {
                incident.CategoryId = matchedCategory.Id;
                incident.Category = matchedCategory;
            }

            analysis.CategoryApplied = true;
        }

        if (request.ApplyPriority)
        {
            var targetPriorityName = analysis.PriorityRecommendation?.Trim();
            if (string.IsNullOrEmpty(targetPriorityName))
            {
                return BadRequest(new
                {
                    message = "Analysis does not contain a priority recommendation."
                });
            }

            var matchedPriority = await _context.Priorities
                .SingleOrDefaultAsync(p => EF.Functions.ILike(p.Name, targetPriorityName));

            if (matchedPriority is null)
            {
                return BadRequest(new
                {
                    message = $"Recommended priority '{targetPriorityName}' is not a valid priority."
                });
            }

            if (incident.PriorityId != matchedPriority.Id)
            {
                incident.PriorityId = matchedPriority.Id;
                incident.Priority = matchedPriority;
                priorityChanged = true;
            }

            analysis.PriorityApplied = true;
        }

        var appliedAt = DateTime.UtcNow;

        analysis.AppliedAt = appliedAt;
        analysis.AppliedByUserId = userId;
        incident.UpdatedAt = appliedAt;

        if (priorityChanged)
        {
            await _notificationService.QueuePriorityChangedAsync(
                incident,
                previousPriorityName,
                incident.Priority.Name,
                userId,
                HttpContext.RequestAborted);
        }

        if (request.ApplyCategory || request.ApplyPriority)
        {
            await _auditService.RecordAIRecommendationAppliedAsync(
                incident,
                analysis,
                userId,
                request.ApplyCategory ? previousCategoryName : null,
                request.ApplyCategory ? incident.Category.Name : null,
                request.ApplyPriority ? previousPriorityName : null,
                request.ApplyPriority ? incident.Priority.Name : null,
                HttpContext.RequestAborted);
        }

        await _context.SaveChangesAsync();

        await _notificationService.StagePendingExternalDeliveriesAsync(HttpContext.RequestAborted);

        return Ok(new
        {
            incidentId = incident.Id,
            category = incident.Category.Name,
            priority = incident.Priority.Name,
            updatedAt = incident.UpdatedAt,
            applied = new
            {
                category = analysis.CategoryApplied,
                priority = analysis.PriorityApplied
            }
        });
    }

    [HttpPost]
    public async Task<IActionResult> CreateIncident(
        CreateIncidentRequest request)
    {
        var userIdClaim = User.FindFirstValue(
            ClaimTypes.NameIdentifier
        );

        if (!Guid.TryParse(userIdClaim, out var reporterId))
        {
            return Unauthorized(new
            {
                message = "Invalid authenticated user."
            });
        }

        var reporter = await _context.Users
            .SingleOrDefaultAsync(u =>
                u.Id == reporterId &&
                u.IsActive
            );

        if (reporter is null)
        {
            return Unauthorized(new
            {
                message = "User account is unavailable."
            });
        }

        var category = await _context.Categories
            .SingleOrDefaultAsync(c =>
                c.Id == request.CategoryId
            );

        if (category is null)
        {
            return BadRequest(new
            {
                message = "Invalid category."
            });
        }

        var priority = await _context.Priorities
            .SingleOrDefaultAsync(p =>
                p.Id == request.PriorityId
            );

        if (priority is null)
        {
            return BadRequest(new
            {
                message = "Invalid priority."
            });
        }

        var incidentNumber =
            $"INC-{DateTime.UtcNow:yyyyMMdd}-" +
            $"{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";

        var incident = new Incident
        {
            IncidentNumber = incidentNumber,
            Title = request.Title.Trim(),
            Description = request.Description.Trim(),
            ReporterId = reporter.Id,
            CategoryId = category.Id,
            PriorityId = priority.Id,
            Status = IncidentStatus.Open
        };

        _context.Incidents.Add(incident);

        await _notificationService.QueueIncidentCreatedAsync(
            incident,
            reporter.Id,
            HttpContext.RequestAborted);

        await _auditService.RecordIncidentCreatedAsync(
            incident,
            reporter.Id,
            HttpContext.RequestAborted);

        await _context.SaveChangesAsync();

        await _notificationService.StagePendingExternalDeliveriesAsync(HttpContext.RequestAborted);

        return Created(
            $"/api/incidents/{incident.Id}",
            new
            {
                id = incident.Id,
                incidentNumber = incident.IncidentNumber,
                title = incident.Title,
                description = incident.Description,
                status = incident.Status.ToString(),

                reporter = new
                {
                    id = reporter.Id,
                    name = $"{reporter.FirstName} {reporter.LastName}"
                },

                category = category.Name,
                priority = priority.Name,
                createdAt = incident.CreatedAt
            }
        );
    }

    private bool TryGetAuthenticatedUser(
        out Guid userId,
        out string? role)
    {
        role = User.FindFirstValue(ClaimTypes.Role);

        return Guid.TryParse(
            User.FindFirstValue(ClaimTypes.NameIdentifier),
            out userId
        );
    }

    private IActionResult WorkflowProblem(
        IncidentWorkflowDecision decision)
    {
        if (decision.IsForbidden)
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new
                {
                    message = decision.Message
                }
            );
        }

        return BadRequest(new
        {
            message = decision.Message
        });
    }

    private static object? ParseAuditMetadata(string? metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<JsonElement>(metadata);
        }
        catch (JsonException)
        {
            return metadata;
        }
    }

    private static object ToAIAnalysisResponse(
        IncidentAIAnalysis analysis)
    {
        var suggestedActions =
            JsonSerializer.Deserialize<string[]>(
                analysis.SuggestedActionsJson
            ) ??
            Array.Empty<string>();

        return new
        {
            id = analysis.Id,
            provider = analysis.Provider,
            model = analysis.Model,
            categoryRecommendation = analysis.CategoryRecommendation,
            priorityRecommendation = analysis.PriorityRecommendation,
            urgency = analysis.Urgency,
            confidence = analysis.Confidence,
            possibleCause = analysis.PossibleCause,
            reasoningSummary = analysis.ReasoningSummary,
            suggestedActions,
            createdAt = analysis.CreatedAt,
            promptVersion = analysis.PromptVersion,
            categoryApplied = analysis.CategoryApplied,
            priorityApplied = analysis.PriorityApplied,
            appliedAt = analysis.AppliedAt,
            appliedByUserId = analysis.AppliedByUserId,
            appliedByUser = analysis.AppliedByUser == null
                ? null
                : new
                {
                    id = analysis.AppliedByUser.Id,
                    name = $"{analysis.AppliedByUser.FirstName} {analysis.AppliedByUser.LastName}".Trim(),
                    email = analysis.AppliedByUser.Email
                }
        };
    }

    private static bool CanRunAIAnalysis(
        string? role,
        Guid userId,
        Incident incident)
    {
        return role switch
        {
            "Technician" => incident.AssignedToId == userId,
            "Manager" or "Admin" => true,
            _ => false
        };
    }

    private static bool CanAccessIncident(
        string? role,
        Guid userId,
        Incident incident)
    {
        return role switch
        {
            "Employee" => incident.ReporterId == userId,
            "Technician" =>
                incident.AssignedToId == userId ||
                incident.AssignedToId == null,
            "Manager" or "Admin" => true,
            _ => false
        };
    }
}
