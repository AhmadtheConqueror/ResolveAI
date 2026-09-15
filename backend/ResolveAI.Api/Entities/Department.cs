namespace ResolveAI.Api.Entities;

public class Department
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<AppUser> Users { get; set; } = new List<AppUser>();
}