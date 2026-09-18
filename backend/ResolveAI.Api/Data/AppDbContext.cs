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
    public DbSet<IncidentAIResolutionAnalysis> IncidentAIResolutionAnalyses => Set<IncidentAIResolutionAnalysis>();
    public DbSet<IncidentAuditEvent> IncidentAuditEvents => Set<IncidentAuditEvent>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<ExternalNotificationDelivery> ExternalNotificationDeliveries => Set<ExternalNotificationDelivery>();

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

        modelBuilder.Entity<AppUser>()
            .Property(u => u.EmailNotificationsEnabled)
            .HasDefaultValue(true);

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
        // Incident AI Resolution Analyses (Phase 2)
        // -------------------------

        modelBuilder.Entity<IncidentAIResolutionAnalysis>()
            .Property(a => a.Provider)
            .HasMaxLength(50);

        modelBuilder.Entity<IncidentAIResolutionAnalysis>()
            .Property(a => a.Model)
            .HasMaxLength(120);

        modelBuilder.Entity<IncidentAIResolutionAnalysis>()
            .Property(a => a.Summary)
            .HasMaxLength(2000);

        modelBuilder.Entity<IncidentAIResolutionAnalysis>()
            .Property(a => a.LikelyIssue)
            .HasMaxLength(500);

        modelBuilder.Entity<IncidentAIResolutionAnalysis>()
            .Property(a => a.Confidence)
            .HasMaxLength(30);

        modelBuilder.Entity<IncidentAIResolutionAnalysis>()
            .Property(a => a.Caveats)
            .HasMaxLength(1000);

        modelBuilder.Entity<IncidentAIResolutionAnalysis>()
            .Property(a => a.PromptVersion)
            .HasMaxLength(80);

        modelBuilder.Entity<IncidentAIResolutionAnalysis>()
            .HasOne(a => a.Incident)
            .WithMany(i => i.AIResolutionAnalyses)
            .HasForeignKey(a => a.IncidentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<IncidentAIResolutionAnalysis>()
            .HasOne(a => a.RequestedByUser)
            .WithMany(u => u.RequestedAIResolutionAnalyses)
            .HasForeignKey(a => a.RequestedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<IncidentAIResolutionAnalysis>()
            .HasIndex(a => new
            {
                a.IncidentId,
                a.CreatedAt
            });

        modelBuilder.Entity<IncidentAIResolutionAnalysis>()
            .HasIndex(a => a.RequestedByUserId);

        // -------------------------
        // Incident Audit Events
        // -------------------------

        modelBuilder.Entity<IncidentAuditEvent>()
            .Property(a => a.EventType)
            .HasConversion<string>()
            .HasMaxLength(80);

        modelBuilder.Entity<IncidentAuditEvent>()
            .Property(a => a.ActorType)
            .HasConversion<string>()
            .HasMaxLength(40);

        modelBuilder.Entity<IncidentAuditEvent>()
            .Property(a => a.ActorDisplayName)
            .HasMaxLength(160);

        modelBuilder.Entity<IncidentAuditEvent>()
            .Property(a => a.Summary)
            .HasMaxLength(240);

        modelBuilder.Entity<IncidentAuditEvent>()
            .Property(a => a.OldValue)
            .HasMaxLength(240);

        modelBuilder.Entity<IncidentAuditEvent>()
            .Property(a => a.NewValue)
            .HasMaxLength(240);

        modelBuilder.Entity<IncidentAuditEvent>()
            .Property(a => a.DeduplicationKey)
            .HasMaxLength(220);

        modelBuilder.Entity<IncidentAuditEvent>()
            .HasOne(a => a.Incident)
            .WithMany(i => i.AuditEvents)
            .HasForeignKey(a => a.IncidentId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<IncidentAuditEvent>()
            .HasOne(a => a.ActorUser)
            .WithMany()
            .HasForeignKey(a => a.ActorUserId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<IncidentAuditEvent>()
            .HasIndex(a => new
            {
                a.IncidentId,
                a.CreatedAt
            });

        modelBuilder.Entity<IncidentAuditEvent>()
            .HasIndex(a => a.ActorUserId);

        modelBuilder.Entity<IncidentAuditEvent>()
            .HasIndex(a => a.DeduplicationKey)
            .IsUnique()
            .HasFilter("\"DeduplicationKey\" IS NOT NULL");

        // -------------------------
        // Notifications
        // -------------------------

        modelBuilder.Entity<Notification>()
            .Property(n => n.Type)
            .HasConversion<string>()
            .HasMaxLength(80);

        modelBuilder.Entity<Notification>()
            .Property(n => n.Title)
            .HasMaxLength(160);

        modelBuilder.Entity<Notification>()
            .Property(n => n.Message)
            .HasMaxLength(500);

        modelBuilder.Entity<Notification>()
            .Property(n => n.DeduplicationKey)
            .HasMaxLength(220);

        modelBuilder.Entity<Notification>()
            .HasOne(n => n.User)
            .WithMany()
            .HasForeignKey(n => n.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Notification>()
            .HasOne(n => n.Incident)
            .WithMany()
            .HasForeignKey(n => n.IncidentId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Notification>()
            .HasOne(n => n.ActorUser)
            .WithMany()
            .HasForeignKey(n => n.ActorUserId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Notification>()
            .HasIndex(n => new
            {
                n.UserId,
                n.IsRead,
                n.CreatedAt
            });

        modelBuilder.Entity<Notification>()
            .HasIndex(n => new
            {
                n.UserId,
                n.CreatedAt
            });

        modelBuilder.Entity<Notification>()
            .HasIndex(n => new
            {
                n.UserId,
                n.DeduplicationKey
            })
            .IsUnique()
            .HasFilter("\"DeduplicationKey\" IS NOT NULL");

        // -------------------------
        // External Notification Deliveries
        // -------------------------

        modelBuilder.Entity<ExternalNotificationDelivery>()
            .Property(d => d.Channel)
            .HasConversion<string>()
            .HasMaxLength(40);

        modelBuilder.Entity<ExternalNotificationDelivery>()
            .Property(d => d.Status)
            .HasConversion<string>()
            .HasMaxLength(40);

        modelBuilder.Entity<ExternalNotificationDelivery>()
            .Property(d => d.RecipientAddress)
            .HasMaxLength(256);

        modelBuilder.Entity<ExternalNotificationDelivery>()
            .Property(d => d.Provider)
            .HasMaxLength(60);

        modelBuilder.Entity<ExternalNotificationDelivery>()
            .Property(d => d.ProviderMessageId)
            .HasMaxLength(120);

        modelBuilder.Entity<ExternalNotificationDelivery>()
            .Property(d => d.LastError)
            .HasMaxLength(1000);

        modelBuilder.Entity<ExternalNotificationDelivery>()
            .Property(d => d.IdempotencyKey)
            .HasMaxLength(256);

        modelBuilder.Entity<ExternalNotificationDelivery>()
            .HasOne(d => d.Notification)
            .WithMany(n => n.ExternalDeliveries)
            .HasForeignKey(d => d.NotificationId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ExternalNotificationDelivery>()
            .HasIndex(d => new
            {
                d.Status,
                d.NextAttemptAt
            });

        modelBuilder.Entity<ExternalNotificationDelivery>()
            .HasIndex(d => d.IdempotencyKey)
            .IsUnique();

        modelBuilder.Entity<ExternalNotificationDelivery>()
            .HasIndex(d => d.NotificationId);

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
