using System.ComponentModel.DataAnnotations;

namespace ResolveAI.Api.DTOs;

public class CreateIncidentRequest
{
    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [MaxLength(5000)]
    public string Description { get; set; } = string.Empty;

    [Required]
    public Guid CategoryId { get; set; }

    [Required]
    public Guid PriorityId { get; set; }
}