using System.ComponentModel.DataAnnotations;

namespace ResolveAI.Api.DTOs;

public class CreateIncidentCommentRequest
{
    [Required]
    [MaxLength(4000)]
    public string Comment { get; set; } = string.Empty;
}
