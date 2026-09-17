using System.ComponentModel.DataAnnotations;

namespace ResolveAI.Api.DTOs;

public class CreateUserRequest
{
    [Required]
    [MaxLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string LastName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [MaxLength(254)]
    public string Email { get; set; } = string.Empty;

    [Required]
    public Guid RoleId { get; set; }

    public Guid? DepartmentId { get; set; }

    public bool? EmailNotificationsEnabled { get; set; } = true;

    [Required]
    [MinLength(8)]
    [MaxLength(128)]
    public string TemporaryPassword { get; set; } = string.Empty;
}
