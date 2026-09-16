using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ResolveAI.Api.Data;
using ResolveAI.Api.DTOs;
using ResolveAI.Api.Entities;
using ResolveAI.Api.Enums;
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

    public IncidentsController(
        AppDbContext context,
        IncidentWorkflowService workflow,
        IAIIncidentService aiIncidentService)
    {
        _context = context;
        _workflow = workflow;
        _aiIncidentService = aiIncidentService;
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
    public async Task<IActionResult> GetIncidents()
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

        var query = _context.Incidents
            .AsNoTracking()
            .Include(i => i.Reporter)
            .Include(i => i.AssignedTo)
            .Include(i => i.Category)
            .Include(i => i.Priority)
            .AsQueryable();

        if (role == "Employee")
        {
            query = query.Where(i => i.ReporterId == userId);
        }
        else if (role == "Technician")
        {
            query = query.Where(i =>
                i.AssignedToId == userId ||
                i.AssignedToId == null
            );
        }
        else if (role != "Manager" && role != "Admin")
        {
            return Forbid();
        }

        var incidents = await query
            .OrderByDescending(i => i.CreatedAt)
            .Select(i => new
            {
                id = i.Id,
                incidentNumber = i.IncidentNumber,
                title = i.Title,
                status = i.Status.ToString(),

                category = i.Category.Name,
                priority = i.Priority.Name,

                reporter = new
                {
                    id = i.Reporter.Id,
                    name = i.Reporter.FirstName + " " + i.Reporter.LastName
                },

                assignedTo = i.AssignedTo == null
                    ? null
                    : new
                    {
                        id = i.AssignedTo.Id,
                        name = i.AssignedTo.FirstName + " " + i.AssignedTo.LastName
                    },

                createdAt = i.CreatedAt,
                updatedAt = i.UpdatedAt
            })
            .ToListAsync();

        return Ok(incidents);
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
            resolvedAt = incident.ResolvedAt,
            closedAt = incident.ClosedAt
        });
    }

    [HttpPatch("{id:guid}/assign")]
    [Authorize(Roles = "Manager,Admin")]
    public async Task<IActionResult> AssignTechnician(
        Guid id,
        AssignIncidentRequest request)
    {
        if (!TryGetAuthenticatedUser(out _, out var role))
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

        var wasReassignment =
            incident.AssignedToId.HasValue &&
            incident.AssignedToId != technician.Id;

        var timestamp = DateTime.UtcNow;

        incident.AssignedToId = technician.Id;

        if (incident.Status == IncidentStatus.Triaged)
        {
            incident.Status = IncidentStatus.Assigned;
        }

        incident.UpdatedAt = timestamp;

        await _context.SaveChangesAsync();

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
            role
        );

        if (!statusDecision.IsAllowed)
        {
            return WorkflowProblem(statusDecision);
        }

        _workflow.ApplyStatus(
            incident,
            requestedStatus,
            DateTime.UtcNow
        );

        await _context.SaveChangesAsync();

        return Ok(new
        {
            id = incident.Id,
            status = incident.Status.ToString(),
            updatedAt = incident.UpdatedAt,
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

        await _context.SaveChangesAsync();

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
            }

            analysis.PriorityApplied = true;
        }

        analysis.AppliedAt = DateTime.UtcNow;
        analysis.AppliedByUserId = userId;
        incident.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

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

        await _context.SaveChangesAsync();

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
            appliedByUserId = analysis.AppliedByUserId
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
