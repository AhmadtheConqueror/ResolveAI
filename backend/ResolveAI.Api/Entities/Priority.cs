namespace ResolveAI.Api.Entities;

public class Priority
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public int Level { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}