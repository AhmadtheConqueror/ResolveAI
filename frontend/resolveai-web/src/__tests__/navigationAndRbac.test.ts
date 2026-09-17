import { describe, it } from "node:test";
import assert from "node:assert";

describe("Frontend Navigation & RBAC Logic", () => {
  function computeNavigationVisibility(role: string) {
    const isEmployee = role === "Employee";
    const isAdmin = role === "Admin";
    const canViewAnalytics = role === "Technician" || role === "Manager" || role === "Admin";
    const canViewMyWork = !isEmployee;
    const canViewUsers = isAdmin;

    return {
      dashboard: true,
      incidents: true,
      myWork: canViewMyWork,
      analytics: canViewAnalytics,
      users: canViewUsers,
    };
  }

  it("hides Analytics, My Work, and Users from Employee", () => {
    const nav = computeNavigationVisibility("Employee");

    assert.strictEqual(nav.dashboard, true);
    assert.strictEqual(nav.incidents, true);
    assert.strictEqual(nav.myWork, false, "My Work should be hidden from Employee");
    assert.strictEqual(nav.analytics, false, "Analytics should be hidden from Employee");
    assert.strictEqual(nav.users, false, "Users should be hidden from Employee");
  });

  it("shows My Work and Analytics to Technician, but hides Users", () => {
    const nav = computeNavigationVisibility("Technician");

    assert.strictEqual(nav.dashboard, true);
    assert.strictEqual(nav.incidents, true);
    assert.strictEqual(nav.myWork, true, "My Work should be visible to Technician");
    assert.strictEqual(nav.analytics, true, "Analytics should be visible to Technician");
    assert.strictEqual(nav.users, false, "Users should be hidden from Technician");
  });

  it("shows My Work and Analytics to Manager, but hides Users", () => {
    const nav = computeNavigationVisibility("Manager");

    assert.strictEqual(nav.dashboard, true);
    assert.strictEqual(nav.incidents, true);
    assert.strictEqual(nav.myWork, true);
    assert.strictEqual(nav.analytics, true);
    assert.strictEqual(nav.users, false);
  });

  it("shows all navigation links including Users to Admin", () => {
    const nav = computeNavigationVisibility("Admin");

    assert.strictEqual(nav.dashboard, true);
    assert.strictEqual(nav.incidents, true);
    assert.strictEqual(nav.myWork, true);
    assert.strictEqual(nav.analytics, true);
    assert.strictEqual(nav.users, true, "Users link must be visible to Admin");
  });
});
