using System.ComponentModel.DataAnnotations;

namespace ResolveAI.Api.DTOs;

public class UpdateUserStatusRequest
{
    [Required]
    public bool IsActive { get; set; }
}
