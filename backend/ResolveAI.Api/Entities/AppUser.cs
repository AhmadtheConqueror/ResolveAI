namespace ResolveAI.Api.Entities;

public class AppUser
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public Guid RoleId { get; set; }

    public Role Role { get; set; } = null!;

    public Guid? DepartmentId { get; set; }

    public Department? Department { get; set; }

    public bool IsActive { get; set; } = true;

    public bool EmailNotificationsEnabled { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<IncidentComment> IncidentComments { get; set; } =
        new List<IncidentComment>();

    public ICollection<IncidentAIAnalysis> RequestedAIAnalyses { get; set; } =
        new List<IncidentAIAnalysis>();

    public ICollection<IncidentAIResolutionAnalysis> RequestedAIResolutionAnalyses { get; set; } =
        new List<IncidentAIResolutionAnalysis>();
}
