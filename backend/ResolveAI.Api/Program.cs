using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ResolveAI.Api.Data;
using ResolveAI.Api.Diagnostics;
using ResolveAI.Api.Entities;
using ResolveAI.Api.Middleware;
using ResolveAI.Api.Models.Email;
using ResolveAI.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddControllers();
builder.Services.AddOpenApi();

// PostgreSQL + Entity Framework Core
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection")
    ));

builder.Services.AddScoped<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IncidentWorkflowService>();
builder.Services.AddScoped<ISlaService, SlaService>();
builder.Services.AddScoped<IIncidentAuditService, IncidentAuditService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddHostedService<SlaNotificationBackgroundService>();

// External Email Notifications
builder.Services.Configure<ResendOptions>(
    builder.Configuration.GetSection(ResendOptions.SectionName));
builder.Services.Configure<ExternalNotificationOptions>(
    builder.Configuration.GetSection(ExternalNotificationOptions.SectionName));
builder.Services.Configure<BrandingOptions>(
    builder.Configuration.GetSection(BrandingOptions.SectionName));

builder.Services.AddSingleton<IEmailTemplateService, EmailTemplateService>();
builder.Services.AddScoped<IExternalNotificationDeliveryProcessor, ExternalNotificationDeliveryProcessor>();
builder.Services.AddHttpClient<IEmailSender, ResendEmailSender>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddHostedService<ExternalNotificationDeliveryWorker>();

builder.Services.AddScoped<IResolutionEvidenceService, ResolutionEvidenceService>();

builder.Services
    .AddHttpClient<IAIIncidentService, GeminiIncidentService>(client =>
    {
        client.BaseAddress = new Uri("https://generativelanguage.googleapis.com");
        client.Timeout = TimeSpan.FromSeconds(90);
    });

builder.Services
    .AddHttpClient<IAIResolutionAssistantService, GeminiResolutionAssistantService>(client =>
    {
        client.BaseAddress = new Uri("https://generativelanguage.googleapis.com");
        client.Timeout = TimeSpan.FromSeconds(90);
    });

// Health checks
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: new[] { "ready" });

// JWT configuration & validation
var jwtKey = builder.Configuration["Jwt:Key"];
var jwtIssuer = builder.Configuration["Jwt:Issuer"];
var jwtAudience = builder.Configuration["Jwt:Audience"];

if (string.IsNullOrWhiteSpace(jwtKey))
{
    throw new InvalidOperationException("JWT key is not configured.");
}

// Production security validation
if (!builder.Environment.IsDevelopment())
{
    if (jwtKey.Length < 32)
    {
        throw new InvalidOperationException("JWT key must be at least 256 bits (32 characters) for production use.");
    }

    if (string.IsNullOrWhiteSpace(jwtIssuer) || string.IsNullOrWhiteSpace(jwtAudience))
    {
        throw new InvalidOperationException("JWT Issuer and Audience must be configured for production use.");
    }
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,

            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,

            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtKey)
            ),

            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();

// Rate limiting
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.ContentType = "application/problem+json";

        var problemDetails = new
        {
            type = "https://tools.ietf.org/html/rfc6585#section-4",
            title = "Too Many Requests",
            status = StatusCodes.Status429TooManyRequests,
            detail = "Too many requests. Please try again shortly.",
            traceId = context.HttpContext.TraceIdentifier
        };

        await context.HttpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);
    };

    // Rate limiter for login: 10 requests per minute per IP
    options.AddPolicy("login-limiter", httpContext =>
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown-client";
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ip,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            });
    });

    // Rate limiter for AI analysis: 5 requests per minute per user / client
    options.AddPolicy("ai-limiter", httpContext =>
    {
        var partitionKey = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? httpContext.Connection.RemoteIpAddress?.ToString()
            ?? "anonymous";

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: partitionKey,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            });
    });
});

// Configurable CORS
var configuredOrigins = builder.Configuration.GetSection("Frontend:AllowedOrigins").Get<string[]>()
    ?? (builder.Configuration["Frontend:AllowedOrigins"]?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    ?? new[] { "http://localhost:5173" };

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy
            .WithOrigins(configuredOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

// Global Exception Handler (first in pipeline)
app.UseMiddleware<GlobalExceptionMiddleware>();

// Security headers
app.UseMiddleware<SecurityHeadersMiddleware>();

app.UseCors("Frontend");
app.UseRateLimiter();

// Configure HTTP pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthentication();
app.UseMiddleware<UserValidationMiddleware>();
app.UseAuthorization();

// Health checks
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = _ => false
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.MapControllers();

app.Run();

// Make Program accessible to WebApplicationFactory for integration tests
public partial class Program { }
