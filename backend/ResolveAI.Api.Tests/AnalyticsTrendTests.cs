using ResolveAI.Api.Controllers;
using ResolveAI.Api.Enums;

namespace ResolveAI.Api.Tests;

public class AnalyticsTrendTests
{
    public void RunAllTests()
    {
        Console.WriteLine("\n--- AnalyticsTrendTests ---");

        Test_BuildTrend_Daily_For_7_And_30_Days();
        Test_BuildTrend_Weekly_For_90_Days();
        Test_BuildTrend_AllTime_Single_Month_Falls_Back_To_Daily();
        Test_BuildTrend_AllTime_MultiMonth_Uses_Monthly();
        Test_BuildTrend_Mathematical_Reconciliation();
    }

    private void Test_BuildTrend_Daily_For_7_And_30_Days()
    {
        var now = new DateTime(2026, 9, 18, 12, 0, 0, DateTimeKind.Utc);
        var sevenDayPeriod = new AnalyticsController.AnalyticsPeriod(now.Date.AddDays(-6), now, "7", now);
        var incidents7d = new List<AnalyticsController.AnalyticsIncidentRow>
        {
            new() { Id = Guid.NewGuid(), CreatedAt = now.Date.AddDays(-5), Status = IncidentStatus.Open },
            new() { Id = Guid.NewGuid(), CreatedAt = now.Date.AddDays(-2), Status = IncidentStatus.InProgress },
            new() { Id = Guid.NewGuid(), CreatedAt = now.Date, Status = IncidentStatus.Resolved }
        };

        var trend7d = AnalyticsController.BuildTrend(incidents7d, sevenDayPeriod);
        Assert.Equal(7, trend7d.Count);
        Assert.True(trend7d.All(p => p.granularity == "daily"));
        Assert.Equal(3, trend7d.Sum(p => p.count));

        var thirtyDayPeriod = new AnalyticsController.AnalyticsPeriod(now.Date.AddDays(-29), now, "30", now);
        var trend30d = AnalyticsController.BuildTrend(incidents7d, thirtyDayPeriod);
        Assert.Equal(30, trend30d.Count);
        Assert.True(trend30d.All(p => p.granularity == "daily"));
        Assert.Equal(3, trend30d.Sum(p => p.count));

        Console.WriteLine("  ✓ 1. 7-day and 30-day ranges produce daily buckets with exact counts");
    }

    private void Test_BuildTrend_Weekly_For_90_Days()
    {
        var now = new DateTime(2026, 9, 18, 12, 0, 0, DateTimeKind.Utc);
        var ninetyDayPeriod = new AnalyticsController.AnalyticsPeriod(now.Date.AddDays(-89), now, "90", now);
        var incidents90d = new List<AnalyticsController.AnalyticsIncidentRow>
        {
            new() { Id = Guid.NewGuid(), CreatedAt = now.Date.AddDays(-85), Status = IncidentStatus.Closed },
            new() { Id = Guid.NewGuid(), CreatedAt = now.Date.AddDays(-40), Status = IncidentStatus.InProgress },
            new() { Id = Guid.NewGuid(), CreatedAt = now.Date.AddDays(-5), Status = IncidentStatus.Open }
        };

        var trend90d = AnalyticsController.BuildTrend(incidents90d, ninetyDayPeriod);
        Assert.True(trend90d.Count >= 12 && trend90d.Count <= 14);
        Assert.True(trend90d.All(p => p.granularity == "weekly"));
        Assert.True(trend90d.All(p => !string.IsNullOrWhiteSpace(p.dateEnd)));
        Assert.Equal(3, trend90d.Sum(p => p.count));

        Console.WriteLine("  ✓ 2. 90-day range produces weekly buckets with human-readable week endpoints");
    }

    private void Test_BuildTrend_AllTime_Single_Month_Falls_Back_To_Daily()
    {
        var now = new DateTime(2026, 9, 18, 12, 0, 0, DateTimeKind.Utc);
        var allTimePeriod = new AnalyticsController.AnalyticsPeriod(null, now, "all", now);

        // 49 incidents created across September 2026
        var incidents = new List<AnalyticsController.AnalyticsIncidentRow>();
        for (var i = 1; i <= 49; i++)
        {
            var day = 1 + (i % 18); // days 1 to 18 of September 2026
            incidents.Add(new AnalyticsController.AnalyticsIncidentRow
            {
                Id = Guid.NewGuid(),
                CreatedAt = new DateTime(2026, 9, day, 10, 0, 0, DateTimeKind.Utc),
                Status = IncidentStatus.InProgress
            });
        }

        var trend = AnalyticsController.BuildTrend(incidents, allTimePeriod);

        // Must NOT be a single monthly bucket of 49 incidents on Sep 1
        Assert.False(trend.Count == 1 && trend[0].date == "2026-09-01");
        Assert.True(trend.Count > 1, "Must produce multiple buckets to show meaningful trend");
        Assert.True(trend.All(p => p.granularity == "daily"), "Must use daily granularity for single-month All Time");
        Assert.Equal(49, trend.Sum(p => p.count));

        Console.WriteLine("  ✓ 3. All Time with single-month data falls back to daily trend instead of misleading single Sep 1 bucket");
    }

    private void Test_BuildTrend_AllTime_MultiMonth_Uses_Monthly()
    {
        var now = new DateTime(2026, 9, 18, 12, 0, 0, DateTimeKind.Utc);
        var allTimePeriod = new AnalyticsController.AnalyticsPeriod(null, now, "all", now);

        var incidents = new List<AnalyticsController.AnalyticsIncidentRow>
        {
            new() { Id = Guid.NewGuid(), CreatedAt = new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc), Status = IncidentStatus.Closed },
            new() { Id = Guid.NewGuid(), CreatedAt = new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc), Status = IncidentStatus.Closed },
            new() { Id = Guid.NewGuid(), CreatedAt = new DateTime(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc), Status = IncidentStatus.Closed },
            new() { Id = Guid.NewGuid(), CreatedAt = new DateTime(2026, 9, 5, 0, 0, 0, DateTimeKind.Utc), Status = IncidentStatus.InProgress }
        };

        var trend = AnalyticsController.BuildTrend(incidents, allTimePeriod);
        Assert.Equal(4, trend.Count); // June, July, August, September
        Assert.True(trend.All(p => p.granularity == "monthly"));
        Assert.Equal(4, trend.Sum(p => p.count));

        Console.WriteLine("  ✓ 4. All Time spanning >= 3 months uses monthly buckets");
    }

    private void Test_BuildTrend_Mathematical_Reconciliation()
    {
        var now = new DateTime(2026, 9, 18, 12, 0, 0, DateTimeKind.Utc);
        var random = new Random(42);
        var incidents = new List<AnalyticsController.AnalyticsIncidentRow>();

        for (var i = 0; i < 73; i++)
        {
            var offset = random.Next(0, 90);
            incidents.Add(new AnalyticsController.AnalyticsIncidentRow
            {
                Id = Guid.NewGuid(),
                CreatedAt = now.Date.AddDays(-offset),
                Status = IncidentStatus.Resolved
            });
        }

        // Test 90-day weekly reconciliation
        var p90 = new AnalyticsController.AnalyticsPeriod(now.Date.AddDays(-89), now, "90", now);
        var trend90 = AnalyticsController.BuildTrend(incidents, p90);
        Assert.Equal(73, trend90.Sum(p => p.count));

        // Test All Time monthly reconciliation
        var pAll = new AnalyticsController.AnalyticsPeriod(null, now, "all", now);
        var trendAll = AnalyticsController.BuildTrend(incidents, pAll);
        Assert.Equal(73, trendAll.Sum(p => p.count));

        Console.WriteLine("  ✓ 5. Bucket totals strictly reconcile to underlying incident count across all granularities");
    }
}
