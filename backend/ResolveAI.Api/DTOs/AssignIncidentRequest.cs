using System.ComponentModel.DataAnnotations;

namespace ResolveAI.Api.DTOs;

public class AssignIncidentRequest
{
    [Required]
    public Guid TechnicianId { get; set; }
}
