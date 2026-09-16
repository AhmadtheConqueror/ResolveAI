using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ResolveAI.Api.Data;
using ResolveAI.Api.Enums;
using ResolveAI.Api.Models.Sla;
using ResolveAI.Api.Services;

namespace ResolveAI.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AnalyticsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ISlaService _slaService;

    public AnalyticsController(
        AppDbContext context,
        ISlaService slaService)
    {
        _context = context;
        _slaService = slaService;
    }

    [HttpGet("overview")]
    public async Task<IActionResult> GetOverview(
        [FromQuery] string? range,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] Guid? departmentId,
        [FromQuery] Guid? technicianId)
    {
        if (!TryGetAuthenticatedUser(out var userId, out var role))
        {
            return Unauthorized(new
            {
                message = "Invalid authenticated user."
            });
        }

        if (role == "Employee")
        {
            return Forbid();
        }

        var period = ResolvePeriod(range, from, to);

        var query = _context.Incidents
            .AsNoTracking()
            .AsQueryable();

        if (role == "Technician")
        {
            if (technicianId.HasValue && technicianId.Value != userId)
            {
                return Forbid();
            }

            query = query.Where(i => i.AssignedToId == userId);
            technicianId = userId;
        }
        else if (role is "Manager" or "Admin")
        {
            if (technicianId.HasValue)
            {
                query = query.Where(i => i.AssignedToId == technicianId.Value);
            }
        }
        else
        {
            return Forbid();
        }

        if (departmentId.HasValue)
        {
            query = query.Where(i => i.Reporter.DepartmentId == departmentId.Value);
        }

        var rows = await query
            .Select(i => new AnalyticsIncidentRow
            {
                Id = i.Id,
                IncidentNumber = i.IncidentNumber,
                Status = i.Status,
                CreatedAt = i.CreatedAt,
                FirstRespondedAt = i.FirstRespondedAt,
                ResolvedAt = i.ResolvedAt,
                AssignedToId = i.AssignedToId,
                AssignedToName = i.AssignedTo == null
                    ? null
                    : i.AssignedTo.FirstName + " " + i.AssignedTo.LastName,
                Category = i.Category.Name,
                Priority = i.Priority.Name,
                ReporterDepartmentId = i.Reporter.DepartmentId,
                ReporterDepartmentName = i.Reporter.Department == null
                    ? null
                    : i.Reporter.Department.Name
            })
            .ToListAsync();

        var createdInPeriod = rows
            .Where(row => IsWithinPeriod(row.CreatedAt, period))
            .ToList();

        var resolvedInPeriod = rows
            .Where(row =>
                row.ResolvedAt.HasValue &&
                IsWithinPeriod(row.ResolvedAt.Value, period))
            .ToList();

        var activeInPeriod = createdInPeriod
            .Where(row => IsActive(row.Status))
            .ToList();

        var slaStats = CalculateSlaStats(
            resolvedInPeriod,
            activeInPeriod,
            period.GeneratedAt);

        var priorities = await _context.Priorities
            .AsNoTracking()
            .OrderBy(p => p.Level)
            .Select(p => p.Name)
            .ToListAsync();

        var categories = await _context.Categories
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => c.Name)
            .ToListAsync();

        var averageFirstResponseMinutes = AverageOrNull(
            createdInPeriod
                .Where(row => row.FirstRespondedAt.HasValue)
                .Select(row => (row.FirstRespondedAt!.Value - row.CreatedAt).TotalMinutes));

        var averageResolutionMinutes = AverageOrNull(
            resolvedInPeriod
                .Where(row => row.ResolvedAt.HasValue)
                .Select(row => (row.ResolvedAt!.Value - row.CreatedAt).TotalMinutes));

        var technicianPerformance = role == "Technician"
            ? new[]
            {
                await BuildCurrentTechnicianMetricsAsync(
                    userId,
                    createdInPeriod,
                    resolvedInPeriod)
            }.ToList()
            : BuildTechnicianMetrics(
                createdInPeriod,
                resolvedInPeriod,
                technicianId);

        var departmentPerformance = role is "Manager" or "Admin"
            ? BuildDepartmentMetrics(createdInPeriod, resolvedInPeriod)
            : new List<object>();

        return Ok(new
        {
            period = new
            {
                from = period.From,
                to = period.To,
                range = period.Range
            },
            kpis = new
            {
                totalIncidents = createdInPeriod.Count,
                activeIncidents = activeInPeriod.Count,
                resolvedIncidents = resolvedInPeriod.Count,
                slaSuccessPercent = slaStats.SuccessPercent,
                averageFirstResponseMinutes,
                averageResolutionMinutes
            },
            incidentTrend = BuildTrend(createdInPeriod, period),
            byStatus = Enum.GetValues<IncidentStatus>()
                .Select(status => new
                {
                    status = status.ToString(),
                    count = createdInPeriod.Count(row => row.Status == status)
                })
                .ToList(),
            byPriority = priorities
                .Select(priority => new
                {
                    priority,
                    count = createdInPeriod.Count(row =>
                        row.Priority.Equals(priority, StringComparison.OrdinalIgnoreCase))
                })
                .ToList(),
            byCategory = categories
                .Select(category => new
                {
                    category,
                    count = createdInPeriod.Count(row =>
                        row.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
                })
                .ToList(),
            slaPerformance = new
            {
                met = slaStats.Met,
                breached = slaStats.Breached,
                activeAtRisk = slaStats.ActiveAtRisk,
                activeBreached = slaStats.ActiveBreached
            },
            technicianPerformance,
            departmentPerformance
        });
    }

    private async Task<object> BuildCurrentTechnicianMetricsAsync(
        Guid userId,
        IReadOnlyList<AnalyticsIncidentRow> createdInPeriod,
        IReadOnlyList<AnalyticsIncidentRow> resolvedInPeriod)
    {
        var technician = await _context.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new
            {
                name = u.FirstName + " " + u.LastName
            })
            .SingleOrDefaultAsync();

        return BuildTechnicianMetric(
            userId,
            technician?.name?.Trim() ?? "Me",
            createdInPeriod,
            resolvedInPeriod);
    }

    private List<object> BuildTechnicianMetrics(
        IReadOnlyList<AnalyticsIncidentRow> createdInPeriod,
        IReadOnlyList<AnalyticsIncidentRow> resolvedInPeriod,
        Guid? technicianFilter)
    {
        var technicianIds = createdInPeriod
            .Concat(resolvedInPeriod)
            .Where(row => row.AssignedToId.HasValue)
            .Select(row => row.AssignedToId!.Value)
            .Distinct()
            .ToList();

        if (technicianFilter.HasValue &&
            !technicianIds.Contains(technicianFilter.Value))
        {
            technicianIds.Add(technicianFilter.Value);
        }

        return technicianIds
            .Select(id =>
            {
                var name = createdInPeriod
                    .Concat(resolvedInPeriod)
                    .FirstOrDefault(row => row.AssignedToId == id)
                    ?.AssignedToName;

                return BuildTechnicianMetric(
                    id,
                    string.IsNullOrWhiteSpace(name) ? "Unassigned Technician" : name,
                    createdInPeriod,
                    resolvedInPeriod);
            })
            .OrderBy(item => ((dynamic)item).technicianName)
            .ToList();
    }

    private object BuildTechnicianMetric(
        Guid technicianId,
        string technicianName,
        IReadOnlyList<AnalyticsIncidentRow> createdInPeriod,
        IReadOnlyList<AnalyticsIncidentRow> resolvedInPeriod)
    {
        var active = createdInPeriod
            .Count(row =>
                row.AssignedToId == technicianId &&
                IsActive(row.Status));

        var resolved = resolvedInPeriod
            .Where(row => row.AssignedToId == technicianId)
            .ToList();

        var slaStats = CalculateResolvedSlaStats(resolved);

        var avgResolution = AverageOrNull(
            resolved
                .Where(row => row.ResolvedAt.HasValue)
                .Select(row => (row.ResolvedAt!.Value - row.CreatedAt).TotalMinutes));

        return new
        {
            technicianId,
            technicianName,
            active,
            resolved = resolved.Count,
            slaSuccessPercent = slaStats.SuccessPercent,
            averageResolutionMinutes = avgResolution
        };
    }

    private List<object> BuildDepartmentMetrics(
        IReadOnlyList<AnalyticsIncidentRow> createdInPeriod,
        IReadOnlyList<AnalyticsIncidentRow> resolvedInPeriod)
    {
        var departmentKeys = createdInPeriod
            .Concat(resolvedInPeriod)
            .Select(row => new
            {
                row.ReporterDepartmentId,
                DepartmentName = row.ReporterDepartmentName
            })
            .Distinct()
            .ToList();

        var rows = departmentKeys
            .Select(department =>
            {
                var created = createdInPeriod
                    .Where(row =>
                        row.ReporterDepartmentId == department.ReporterDepartmentId)
                    .ToList();

                var resolved = resolvedInPeriod
                    .Where(row =>
                        row.ReporterDepartmentId == department.ReporterDepartmentId)
                    .ToList();

                var slaStats = CalculateResolvedSlaStats(resolved);

                return new
                {
                    departmentId = department.ReporterDepartmentId,
                    departmentName = string.IsNullOrWhiteSpace(department.DepartmentName)
                        ? "Unassigned / No Department"
                        : department.DepartmentName,
                    incidents = created.Count,
                    active = created.Count(row => IsActive(row.Status)),
                    resolved = resolved.Count,
                    slaSuccessPercent = slaStats.SuccessPercent
                };
            })
            .OrderBy(item => ((dynamic)item).departmentName)
            .ToList();

        return rows.Cast<object>().ToList();
    }

    private SlaAggregate CalculateSlaStats(
        IReadOnlyList<AnalyticsIncidentRow> resolvedRows,
        IReadOnlyList<AnalyticsIncidentRow> activeRows,
        DateTime timestamp)
    {
        var resolvedStats = CalculateResolvedSlaStats(resolvedRows);

        var activeDetails = activeRows
            .Select(row => _slaService.CalculateDetail(
                row.CreatedAt,
                row.Priority,
                row.FirstRespondedAt,
                row.ResolvedAt,
                timestamp))
            .ToList();

        return new SlaAggregate
        {
            Met = resolvedStats.Met,
            Breached = resolvedStats.Breached,
            SuccessPercent = resolvedStats.SuccessPercent,
            ActiveAtRisk = activeDetails.Count(detail =>
                detail.OverallStatus == SlaStatus.AtRisk.ToString()),
            ActiveBreached = activeDetails.Count(detail =>
                detail.OverallStatus == SlaStatus.Breached.ToString())
        };
    }

    private SlaAggregate CalculateResolvedSlaStats(
        IReadOnlyList<AnalyticsIncidentRow> resolvedRows)
    {
        var details = resolvedRows
            .Where(row => row.ResolvedAt.HasValue)
            .Select(row => _slaService.CalculateDetail(
                row.CreatedAt,
                row.Priority,
                row.FirstRespondedAt,
                row.ResolvedAt,
                row.ResolvedAt))
            .Where(detail =>
                detail.ResolutionStatus != SlaStatus.NotApplicable.ToString())
            .ToList();

        var met = details.Count(detail =>
            detail.ResolutionStatus == SlaStatus.Met.ToString());
        var breached = details.Count(detail =>
            detail.ResolutionStatus == SlaStatus.Breached.ToString());

        return new SlaAggregate
        {
            Met = met,
            Breached = breached,
            SuccessPercent = PercentOrNull(met, details.Count)
        };
    }

    private static List<object> BuildTrend(
        IReadOnlyList<AnalyticsIncidentRow> createdInPeriod,
        AnalyticsPeriod period)
    {
        if (period.From.HasValue &&
            (period.To - period.From.Value).TotalDays <= 120)
        {
            var counts = createdInPeriod
                .GroupBy(row => row.CreatedAt.Date)
                .ToDictionary(group => group.Key, group => group.Count());

            var items = new List<object>();
            var cursor = period.From.Value.Date;
            var endDate = period.To.Date;

            while (cursor <= endDate)
            {
                items.Add(new
                {
                    date = cursor.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    count = counts.GetValueOrDefault(cursor, 0)
                });

                cursor = cursor.AddDays(1);
            }

            return items;
        }

        if (createdInPeriod.Count == 0)
        {
            return new List<object>();
        }

        var monthlyCounts = createdInPeriod
            .GroupBy(row => new DateTime(row.CreatedAt.Year, row.CreatedAt.Month, 1))
            .ToDictionary(group => group.Key, group => group.Count());

        var start = period.From?.Date ??
            new DateTime(
                createdInPeriod.Min(row => row.CreatedAt).Year,
                createdInPeriod.Min(row => row.CreatedAt).Month,
                1);
        var monthCursor = new DateTime(start.Year, start.Month, 1);
        var lastMonth = new DateTime(period.To.Year, period.To.Month, 1);
        var monthly = new List<object>();

        while (monthCursor <= lastMonth)
        {
            monthly.Add(new
            {
                date = monthCursor.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                count = monthlyCounts.GetValueOrDefault(monthCursor, 0)
            });

            monthCursor = monthCursor.AddMonths(1);
        }

        return monthly;
    }

    private static AnalyticsPeriod ResolvePeriod(
        string? range,
        DateTime? from,
        DateTime? to)
    {
        var now = DateTime.UtcNow;
        var normalizedRange = range?.Trim().ToLowerInvariant();

        if (from.HasValue || to.HasValue)
        {
            var start = from.HasValue
                ? NormalizeUtc(from.Value).Date
                : now.Date.AddDays(-29);
            var end = to.HasValue
                ? NormalizeUtc(to.Value).Date.AddDays(1).AddTicks(-1)
                : now;

            return new AnalyticsPeriod(start, end, "custom", now);
        }

        return normalizedRange switch
        {
            "7" or "7d" or "7days" => new AnalyticsPeriod(
                now.Date.AddDays(-6),
                now,
                "7",
                now),
            "90" or "90d" or "90days" => new AnalyticsPeriod(
                now.Date.AddDays(-89),
                now,
                "90",
                now),
            "all" or "alltime" => new AnalyticsPeriod(
                null,
                now,
                "all",
                now),
            _ => new AnalyticsPeriod(
                now.Date.AddDays(-29),
                now,
                "30",
                now)
        };
    }

    private static DateTime NormalizeUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }

    private static bool IsWithinPeriod(
        DateTime value,
        AnalyticsPeriod period)
    {
        return (!period.From.HasValue || value >= period.From.Value) &&
            value <= period.To;
    }

    private static bool IsActive(IncidentStatus status)
    {
        return status is not IncidentStatus.Resolved and not IncidentStatus.Closed;
    }

    private static double? AverageOrNull(IEnumerable<double> values)
    {
        var list = values.ToList();

        return list.Count == 0
            ? null
            : Math.Round(list.Average(), 1);
    }

    private static double? PercentOrNull(int numerator, int denominator)
    {
        return denominator == 0
            ? null
            : Math.Round((double)numerator / denominator * 100, 1);
    }

    private bool TryGetAuthenticatedUser(
        out Guid userId,
        out string? role)
    {
        role = User.FindFirstValue(ClaimTypes.Role);

        return Guid.TryParse(
            User.FindFirstValue(ClaimTypes.NameIdentifier),
            out userId);
    }

    private sealed class AnalyticsIncidentRow
    {
        public Guid Id { get; set; }
        public string IncidentNumber { get; set; } = string.Empty;
        public IncidentStatus Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? FirstRespondedAt { get; set; }
        public DateTime? ResolvedAt { get; set; }
        public Guid? AssignedToId { get; set; }
        public string? AssignedToName { get; set; }
        public string Category { get; set; } = string.Empty;
        public string Priority { get; set; } = string.Empty;
        public Guid? ReporterDepartmentId { get; set; }
        public string? ReporterDepartmentName { get; set; }
    }

    private sealed class SlaAggregate
    {
        public int Met { get; set; }
        public int Breached { get; set; }
        public int ActiveAtRisk { get; set; }
        public int ActiveBreached { get; set; }
        public double? SuccessPercent { get; set; }
    }

    private sealed record AnalyticsPeriod(
        DateTime? From,
        DateTime To,
        string Range,
        DateTime GeneratedAt);
}
