import { describe, it } from "node:test";
import assert from "node:assert";

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
});
