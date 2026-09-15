using System.ComponentModel.DataAnnotations;

namespace ResolveAI.Api.DTOs;

public class UpdateIncidentStatusRequest
{
    [Required]
    public string Status { get; set; } = string.Empty;
}
