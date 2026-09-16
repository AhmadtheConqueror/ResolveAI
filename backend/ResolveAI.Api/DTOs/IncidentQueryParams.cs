namespace ResolveAI.Api.DTOs;

public class IncidentQueryParams
{
    public string? Search { get; set; }
    public string? Status { get; set; }
    public string? Priority { get; set; }
    public string? Category { get; set; }
    public string? SlaStatus { get; set; }
    public string? Assignment { get; set; } // "assigned", "unassigned"
    public Guid? AssignedToId { get; set; }
    public int? Page { get; set; }
    public int? PageSize { get; set; }
    public string? SortBy { get; set; } // "createdAt", "updatedAt", "priority", "incidentNumber", "urgency"
    public string? SortDirection { get; set; } // "asc", "desc"
}
