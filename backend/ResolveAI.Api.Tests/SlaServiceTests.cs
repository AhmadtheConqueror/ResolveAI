using ResolveAI.Api.Entities;
using ResolveAI.Api.Enums;
using ResolveAI.Api.Services;

namespace ResolveAI.Api.Tests;

public class SlaServiceTests
{
    private readonly SlaService _slaService = new();

    public void RunAllTests()
    {
        Console.WriteLine("\n--- SlaServiceTests ---");

        Test_Priority_Targets();
        Test_Response_OnTrack_And_AtRisk_Threshold();
        Test_Response_Breach();
        Test_Response_Met();
        Test_Resolution_Breach();
        Test_Resolution_Met();
        Test_Priority_Change_Recalculation_Uses_Original_CreatedAt();
    }

    private void Test_Priority_Targets()
    {
        var now = DateTime.UtcNow;

        var low = _slaService.CalculateDetail(now, "Low", null, null, now);
        Assert.Equal(480, low.ResponseTargetMinutes);
        Assert.Equal(4320, low.ResolutionTargetMinutes);

        var med = _slaService.CalculateDetail(now, "Medium", null, null, now);
        Assert.Equal(240, med.ResponseTargetMinutes);
        Assert.Equal(2880, med.ResolutionTargetMinutes);

        var high = _slaService.CalculateDetail(now, "High", null, null, now);
        Assert.Equal(120, high.ResponseTargetMinutes);
        Assert.Equal(1440, high.ResolutionTargetMinutes);

        var crit = _slaService.CalculateDetail(now, "Critical", null, null, now);
        Assert.Equal(30, crit.ResponseTargetMinutes);
        Assert.Equal(480, crit.ResolutionTargetMinutes);

        Console.WriteLine("  ✓ Low, Medium, High, Critical target minutes verified");
    }

    private void Test_Response_OnTrack_And_AtRisk_Threshold()
    {
        var created = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        // High response target is 120m. 75% threshold is 90m.

        // At 60m: elapsed is 50% -> OnTrack
        var at60m = created.AddMinutes(60);
        var detail60 = _slaService.CalculateDetail(created, "High", null, null, at60m);
        Assert.Equal("OnTrack", detail60.ResponseStatus);

        // At 90m: elapsed is 75% -> AtRisk
        var at90m = created.AddMinutes(90);
        var detail90 = _slaService.CalculateDetail(created, "High", null, null, at90m);
        Assert.Equal("AtRisk", detail90.ResponseStatus);
        Assert.Equal("AtRisk", detail90.OverallStatus);

        Console.WriteLine("  ✓ OnTrack and AtRisk (75% threshold) verified");
    }

    private void Test_Response_Breach()
    {
        var created = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        // Critical response target is 30m
        var after35m = created.AddMinutes(35);
        var detail = _slaService.CalculateDetail(created, "Critical", null, null, after35m);

        Assert.Equal("Breached", detail.ResponseStatus);
        Assert.Equal("Breached", detail.OverallStatus);
        Assert.True(detail.ResponseBreached);
        Assert.True(detail.RequiresEscalation);

        Console.WriteLine("  ✓ Response breach and escalation flag verified");
    }

    private void Test_Response_Met()
    {
        var created = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var respondedAt = created.AddMinutes(20); // within 30m Critical target
        var asOf = created.AddMinutes(60);

        var detail = _slaService.CalculateDetail(created, "Critical", respondedAt, null, asOf);
        Assert.Equal("Met", detail.ResponseStatus);
        Assert.False(detail.ResponseBreached);

        Console.WriteLine("  ✓ Response Met status verified");
    }

    private void Test_Resolution_Breach()
    {
        var created = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        // Critical resolution target is 480m (8h)
        var after9h = created.AddHours(9);

        var detail = _slaService.CalculateDetail(created, "Critical", created.AddMinutes(15), null, after9h);
        Assert.Equal("Breached", detail.ResolutionStatus);
        Assert.True(detail.ResolutionBreached);
        Assert.True(detail.RequiresEscalation);

        Console.WriteLine("  ✓ Resolution breach and escalation flag verified");
    }

    private void Test_Resolution_Met()
    {
        var created = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var firstResponded = created.AddMinutes(10);
        var resolved = created.AddHours(4); // well within 8h Critical target

        var detail = _slaService.CalculateDetail(created, "Critical", firstResponded, resolved, created.AddHours(10));
        Assert.Equal("Met", detail.ResolutionStatus);
        Assert.Equal("Met", detail.OverallStatus);
        Assert.False(detail.RequiresEscalation);

        Console.WriteLine("  ✓ Resolution Met and Overall Met verified");
    }

    private void Test_Priority_Change_Recalculation_Uses_Original_CreatedAt()
    {
        var originalCreated = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);

        // Created as Low (480m response target -> due 18:00)
        var lowDetail = _slaService.CalculateDetail(originalCreated, "Low", null, null, originalCreated);
        Assert.Equal(originalCreated.AddMinutes(480), lowDetail.ResponseDueAt);

        // Reprioritized to Critical at 10:15 (target is 30m from CreatedAt -> due 10:30)
        var critDetail = _slaService.CalculateDetail(originalCreated, "Critical", null, null, originalCreated.AddMinutes(15));
        Assert.Equal(originalCreated.AddMinutes(30), critDetail.ResponseDueAt);
        Assert.Equal(originalCreated.AddMinutes(480), critDetail.ResolutionDueAt);

        Console.WriteLine("  ✓ Recalculation after priority change calculates from original CreatedAt");
    }
}
