using System.ComponentModel.DataAnnotations;

namespace ResolveAI.Api.DTOs;

public class ResetUserPasswordRequest
{
    [Required]
    [MinLength(8)]
    [MaxLength(128)]
    public string TemporaryPassword { get; set; } = string.Empty;
}
