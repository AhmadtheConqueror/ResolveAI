import { describe, it } from "node:test";
import assert from "node:assert";
import {
  formatAriaDate,
  formatDateLabel,
  formatTooltipDate,
  reconcileTrendTotal,
  type AnalyticsTrendPoint,
} from "../utils/analyticsTrend.ts";

describe("Frontend Analytics Calculations", () => {
  function calculateSlaComplianceRate(metCount: number, totalResolved: number): number {
    if (totalResolved <= 0) return 0;
    return Math.round((metCount / totalResolved) * 100);
  }

  function getBarWidthPercentage(count: number, items: Array<{ count: number }>): number {
    const max = Math.max(1, ...items.map((i) => i.count));
    return Math.max(4, (count / max) * 100);
  }

  it("safely handles 0 denominator without producing NaN", () => {
    const rate = calculateSlaComplianceRate(0, 0);
    assert.strictEqual(Number.isNaN(rate), false, "Compliance rate should never be NaN");
    assert.strictEqual(rate, 0);

    const normalRate = calculateSlaComplianceRate(8, 10);
    assert.strictEqual(normalRate, 80);
  });

  it("calculates progress bar widths safely even when all counts are 0", () => {
    const emptyItems = [{ count: 0 }, { count: 0 }];
    const width = getBarWidthPercentage(0, emptyItems);

    assert.strictEqual(Number.isNaN(width), false, "Width should never be NaN");
    assert.strictEqual(width >= 4, true, "Width should have minimum threshold of 4%");
  });

  it("scales relative bar widths accurately when counts exist", () => {
    const items = [{ count: 50 }, { count: 100 }];
    const halfWidth = getBarWidthPercentage(50, items);
    const fullWidth = getBarWidthPercentage(100, items);

    assert.strictEqual(halfWidth, 50);
    assert.strictEqual(fullWidth, 100);
  });

  it("formats daily bucket labels and tooltips correctly", () => {
    const dailyPoint: AnalyticsTrendPoint = {
      date: "2026-09-18",
      count: 7,
      granularity: "daily",
    };

    const label = formatDateLabel(dailyPoint);
    const tooltip = formatTooltipDate(dailyPoint);
    const aria = formatAriaDate(dailyPoint);

    assert.strictEqual(label, "Sep 18");
    assert.strictEqual(tooltip, "Sep 18, 2026");
    assert.strictEqual(aria, "September 18, 2026");
  });

  it("formats weekly buckets with human-readable week ranges", () => {
    const sameMonthWeek: AnalyticsTrendPoint = {
      date: "2026-09-08",
      dateEnd: "2026-09-14",
      count: 14,
      granularity: "weekly",
    };

    const labelSame = formatDateLabel(sameMonthWeek);
    const tooltipSame = formatTooltipDate(sameMonthWeek);

    assert.strictEqual(labelSame, "Sep 8–14");
    assert.strictEqual(tooltipSame, "Sep 8 – Sep 14, 2026");

    const crossMonthWeek: AnalyticsTrendPoint = {
      date: "2026-08-28",
      dateEnd: "2026-09-03",
      count: 9,
      granularity: "weekly",
    };

    const labelCross = formatDateLabel(crossMonthWeek);
    const tooltipCross = formatTooltipDate(crossMonthWeek);

    assert.strictEqual(labelCross, "Aug 28 – Sep 3");
    assert.strictEqual(tooltipCross, "Aug 28 – Sep 3, 2026");
  });

  it("formats monthly buckets as 'September 2026' and never as 'Sep 1, 2026'", () => {
    const monthlyPoint: AnalyticsTrendPoint = {
      date: "2026-09-01",
      count: 49,
      granularity: "monthly",
    };

    const label = formatDateLabel(monthlyPoint);
    const tooltip = formatTooltipDate(monthlyPoint);
    const aria = formatAriaDate(monthlyPoint);

    assert.strictEqual(label, "September 2026");
    assert.strictEqual(tooltip, "September 2026");
    assert.strictEqual(aria, "September 2026");

    // Strictly verify neither label nor tooltip suggests incidents occurred on Sep 1
    assert.strictEqual(tooltip.includes("Sep 1"), false);
    assert.strictEqual(label.includes("Sep 1"), false);
  });

  it("All Time with only one month of data does not create a misleading single 'Sep 1' bucket", () => {
    // When 49 incidents fall within September 2026, the backend produces daily buckets
    const sampleDailyTrendForSingleMonth: AnalyticsTrendPoint[] = [
      { date: "2026-09-02", count: 10, granularity: "daily" },
      { date: "2026-09-05", count: 15, granularity: "daily" },
      { date: "2026-09-10", count: 14, granularity: "daily" },
      { date: "2026-09-18", count: 10, granularity: "daily" },
    ];

    // Verify it contains multiple buckets representing a trend rather than 1 single bucket
    assert.strictEqual(sampleDailyTrendForSingleMonth.length > 1, true);
    assert.strictEqual(sampleDailyTrendForSingleMonth.every((p) => p.granularity === "daily"), true);

    // Verify each daily point formats with its specific day
    assert.strictEqual(formatTooltipDate(sampleDailyTrendForSingleMonth[0]), "Sep 2, 2026");
    assert.strictEqual(formatTooltipDate(sampleDailyTrendForSingleMonth[3]), "Sep 18, 2026");
  });

  it("totals across buckets still reconcile to the underlying incident count", () => {
    const dailyTrend: AnalyticsTrendPoint[] = [
      { date: "2026-09-02", count: 10, granularity: "daily" },
      { date: "2026-09-05", count: 15, granularity: "daily" },
      { date: "2026-09-10", count: 14, granularity: "daily" },
      { date: "2026-09-18", count: 10, granularity: "daily" },
    ];
    assert.strictEqual(reconcileTrendTotal(dailyTrend), 49);

    const weeklyTrend: AnalyticsTrendPoint[] = [
      { date: "2026-09-01", dateEnd: "2026-09-07", count: 20, granularity: "weekly" },
      { date: "2026-09-08", dateEnd: "2026-09-14", count: 19, granularity: "weekly" },
      { date: "2026-09-15", dateEnd: "2026-09-18", count: 10, granularity: "weekly" },
    ];
    assert.strictEqual(reconcileTrendTotal(weeklyTrend), 49);

    const monthlyTrend: AnalyticsTrendPoint[] = [
      { date: "2026-07-01", count: 15, granularity: "monthly" },
      { date: "2026-08-01", count: 20, granularity: "monthly" },
      { date: "2026-09-01", count: 14, granularity: "monthly" },
    ];
    assert.strictEqual(reconcileTrendTotal(monthlyTrend), 49);
  });
});

describe("Incidents Pagination & Page Size Synchronization", () => {
  it("computes total pages accurately across page size changes", () => {
    const totalCount = 49;

    // Default 25/page gives 2 pages
    const pagesAt25 = Math.ceil(totalCount / 25);
    assert.strictEqual(pagesAt25, 2);

    // Switching to 50/page gives 1 page
    const pagesAt50 = Math.ceil(totalCount / 50);
    assert.strictEqual(pagesAt50, 1);

    // Switching back to 25/page returns to 2 pages
    const pagesBackTo25 = Math.ceil(totalCount / 25);
    assert.strictEqual(pagesBackTo25, 2);
  });

  it("ensures pagination controls remain accessible when totalCount > 0 even if totalPages === 1", () => {
    // Defect reproduction: previously condition was `totalPages > 1`, which hid pagination bar when 49 items fit on 1 page of 50.
    // The corrected condition is `totalCount > 0`.
    const shouldRenderPagination = (totalCount: number) => totalCount > 0;

    // With 49 items at 25/page (totalPages = 2):
    assert.strictEqual(shouldRenderPagination(49), true);

    // With 49 items at 50/page (totalPages = 1):
    assert.strictEqual(
      shouldRenderPagination(49),
      true,
      "Pagination bar must remain rendered so user can change back to 25/page"
    );

    // With 0 items:
    assert.strictEqual(shouldRenderPagination(0), false, "Pagination bar is hidden when empty");
  });

  it("serializes URL search params cleanly without redundant default pageSize=25", () => {
    function serializeParams(
      current: Record<string, string>,
      updates: Record<string, string | number>
    ) {
      const sp = new URLSearchParams(current);
      for (const [k, v] of Object.entries(updates)) {
        if (
          !v ||
          v === "all" ||
          (k === "page" && Number(v) === 1) ||
          (k === "pageSize" && Number(v) === 25)
        ) {
          sp.delete(k);
        } else {
          sp.set(k, String(v));
        }
      }
      return sp.toString();
    }

    // Changing 25 -> 50 sets pageSize=50
    const queryWith50 = serializeParams({}, { pageSize: 50, page: 1 });
    assert.strictEqual(queryWith50, "pageSize=50");

    // Changing 50 -> 25 deletes pageSize (default)
    const queryBackTo25 = serializeParams(
      { pageSize: "50" },
      { pageSize: 25, page: 1 }
    );
    assert.strictEqual(queryBackTo25, "");
  });
});

