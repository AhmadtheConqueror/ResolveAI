using Microsoft.EntityFrameworkCore;
using ResolveAI.Api.Entities;

namespace ResolveAI.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    // Database tables
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Priority> Priorities => Set<Priority>();
    public DbSet<Incident> Incidents => Set<Incident>();
    public DbSet<IncidentComment> IncidentComments => Set<IncidentComment>();
    public DbSet<IncidentAIAnalysis> IncidentAIAnalyses => Set<IncidentAIAnalysis>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // -------------------------
        // Roles
        // -------------------------

        modelBuilder.Entity<Role>()
            .HasIndex(r => r.Name)
            .IsUnique();

        // -------------------------
        // Users
        // -------------------------

        modelBuilder.Entity<AppUser>()
            .HasIndex(u => u.Email)
            .IsUnique();

        modelBuilder.Entity<AppUser>()
            .HasOne(u => u.Role)
            .WithMany(r => r.Users)
            .HasForeignKey(u => u.RoleId);

        modelBuilder.Entity<AppUser>()
            .HasOne(u => u.Department)
            .WithMany(d => d.Users)
            .HasForeignKey(u => u.DepartmentId);

        // -------------------------
        // Categories
        // -------------------------

        modelBuilder.Entity<Category>()
            .HasIndex(c => c.Name)
            .IsUnique();

        // -------------------------
        // Priorities
        // -------------------------

        modelBuilder.Entity<Priority>()
            .HasIndex(p => p.Name)
            .IsUnique();

        // -------------------------
        // Incidents
        // -------------------------

        modelBuilder.Entity<Incident>()
            .HasIndex(i => i.IncidentNumber)
            .IsUnique();

        modelBuilder.Entity<Incident>()
            .HasOne(i => i.Reporter)
            .WithMany()
            .HasForeignKey(i => i.ReporterId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Incident>()
            .HasOne(i => i.AssignedTo)
            .WithMany()
            .HasForeignKey(i => i.AssignedToId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Incident>()
            .HasOne(i => i.Category)
            .WithMany()
            .HasForeignKey(i => i.CategoryId);

        modelBuilder.Entity<Incident>()
            .HasOne(i => i.Priority)
            .WithMany()
            .HasForeignKey(i => i.PriorityId);

        // -------------------------
        // Incident Comments
        // -------------------------

        modelBuilder.Entity<IncidentComment>()
            .Property(c => c.Comment)
            .HasMaxLength(4000);

        modelBuilder.Entity<IncidentComment>()
            .HasOne(c => c.Incident)
            .WithMany(i => i.Comments)
            .HasForeignKey(c => c.IncidentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<IncidentComment>()
            .HasOne(c => c.User)
            .WithMany(u => u.IncidentComments)
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<IncidentComment>()
            .HasIndex(c => new
            {
                c.IncidentId,
                c.CreatedAt
            });

        // -------------------------
        // Incident AI Analyses
        // -------------------------

        modelBuilder.Entity<IncidentAIAnalysis>()
            .Property(a => a.Provider)
            .HasMaxLength(50);

        modelBuilder.Entity<IncidentAIAnalysis>()
            .Property(a => a.Model)
            .HasMaxLength(120);

        modelBuilder.Entity<IncidentAIAnalysis>()
            .Property(a => a.CategoryRecommendation)
            .HasMaxLength(40);

        modelBuilder.Entity<IncidentAIAnalysis>()
            .Property(a => a.PriorityRecommendation)
            .HasMaxLength(40);

        modelBuilder.Entity<IncidentAIAnalysis>()
            .Property(a => a.Urgency)
            .HasMaxLength(40);

        modelBuilder.Entity<IncidentAIAnalysis>()
            .Property(a => a.PossibleCause)
            .HasMaxLength(1000);

        modelBuilder.Entity<IncidentAIAnalysis>()
            .Property(a => a.ReasoningSummary)
            .HasMaxLength(1200);

        modelBuilder.Entity<IncidentAIAnalysis>()
            .Property(a => a.PromptVersion)
            .HasMaxLength(80);

        modelBuilder.Entity<IncidentAIAnalysis>()
            .HasOne(a => a.Incident)
            .WithMany(i => i.AIAnalyses)
            .HasForeignKey(a => a.IncidentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<IncidentAIAnalysis>()
            .HasOne(a => a.RequestedByUser)
            .WithMany(u => u.RequestedAIAnalyses)
            .HasForeignKey(a => a.RequestedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<IncidentAIAnalysis>()
            .HasOne(a => a.AppliedByUser)
            .WithMany()
            .HasForeignKey(a => a.AppliedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<IncidentAIAnalysis>()
            .HasIndex(a => new
            {
                a.IncidentId,
                a.CreatedAt
            });

        // -------------------------
        // Seed Roles
        // -------------------------

        modelBuilder.Entity<Role>().HasData(
            new Role
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Name = "Employee",
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new Role
            {
                Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Name = "Technician",
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new Role
            {
                Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                Name = "Manager",
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new Role
            {
                Id = Guid.Parse("44444444-4444-4444-4444-444444444444"),
                Name = "Admin",
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            }
        );

        // -------------------------
        // Seed Departments
        // -------------------------

        modelBuilder.Entity<Department>().HasData(
            new Department
            {
                Id = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccc01"),
                Name = "IT",
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new Department
            {
                Id = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccc02"),
                Name = "Finance",
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new Department
            {
                Id = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccc03"),
                Name = "HR",
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new Department
            {
                Id = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccc04"),
                Name = "Operations",
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new Department
            {
                Id = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccc05"),
                Name = "Administration",
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            }
        );

        // -------------------------
        // Seed Categories
        // -------------------------

        modelBuilder.Entity<Category>().HasData(
            new Category
            {
                Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1"),
                Name = "Hardware",
                Description = "Physical computer and device issues",
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new Category
            {
                Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2"),
                Name = "Software",
                Description = "Application and software issues",
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new Category
            {
                Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa3"),
                Name = "Network",
                Description = "Network and connectivity issues",
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new Category
            {
                Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa4"),
                Name = "Access",
                Description = "Authentication and access issues",
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new Category
            {
                Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa5"),
                Name = "Other",
                Description = "Other incident types",
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            }
        );

        // -------------------------
        // Seed Priorities
        // -------------------------

        modelBuilder.Entity<Priority>().HasData(
            new Priority
            {
                Id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb1"),
                Name = "Low",
                Level = 1,
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new Priority
            {
                Id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb2"),
                Name = "Medium",
                Level = 2,
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new Priority
            {
                Id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb3"),
                Name = "High",
                Level = 3,
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new Priority
            {
                Id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb4"),
                Name = "Critical",
                Level = 4,
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            }
        );
    }
}
