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

describe("Frontend Incident Action Governance Logic", () => {
  type CurrentUser = { id: string; role: string };
  type StatusAction = { label: string; status: string; tone?: string };

  function getStatusActions(
    incident: { status: string; reporter: { id: string }; assignedTo?: { id: string } | null },
    user: CurrentUser
  ): StatusAction[] {
    const assignedToCurrentUser = incident.assignedTo?.id === user.id;

    if (user.role === "Employee") {
      return incident.status === "Resolved" && incident.reporter.id === user.id
        ? [{ label: "Confirm & Close", status: "Closed", tone: "secondary" }]
        : [];
    }

    if (user.role === "Technician") {
      if (!assignedToCurrentUser) {
        return [];
      }

      if (incident.status === "Assigned") {
        return [{ label: "Start Work", status: "InProgress", tone: "primary" }];
      }

      if (incident.status === "InProgress") {
        return [
          { label: "Waiting for User", status: "WaitingForUser", tone: "secondary" },
          { label: "Resolve Incident", status: "Resolved", tone: "success" },
        ];
      }

      if (incident.status === "WaitingForUser") {
        return [{ label: "Resume Work", status: "InProgress", tone: "primary" }];
      }

      return [];
    }

    const isManagerOrAdmin = user.role === "Manager" || user.role === "Admin";
    if (!isManagerOrAdmin) {
      return [];
    }

    switch (incident.status) {
      case "Open":
        return [
          { label: "Mark as Triaged", status: "Triaged", tone: "primary" },
          { label: "Administrative Close", status: "Closed", tone: "danger" },
        ];
      case "Triaged":
      case "Assigned":
      case "InProgress":
      case "WaitingForUser":
        return [
          { label: "Administrative Close", status: "Closed", tone: "danger" },
        ];
      case "Resolved":
        return [
          { label: "Close Incident", status: "Closed", tone: "secondary" },
        ];
      default:
        return [];
    }
  }

  const techUser = { id: "tech-1", role: "Technician" };
  const otherTech = { id: "tech-2", role: "Technician" };
  const managerUser = { id: "mgr-1", role: "Manager" };
  const adminUser = { id: "adm-1", role: "Admin" };
  const employeeUser = { id: "emp-1", role: "Employee" };

  it("assigned technician sees Start Work on Assigned incident", () => {
    const incident = { status: "Assigned", reporter: { id: "emp-1" }, assignedTo: { id: "tech-1" } };
    const actions = getStatusActions(incident, techUser);
    assert.strictEqual(actions.length, 1);
    assert.strictEqual(actions[0].label, "Start Work");
  });

  it("manager viewing technician incident does NOT see Start Work or Resolve", () => {
    const assignedIncident = { status: "Assigned", reporter: { id: "emp-1" }, assignedTo: { id: "tech-1" } };
    const mgrAssignedActions = getStatusActions(assignedIncident, managerUser);
    assert.strictEqual(mgrAssignedActions.some((a) => a.label === "Start Work"), false);
    assert.strictEqual(mgrAssignedActions.some((a) => a.label === "Administrative Close"), true);

    const inProgressIncident = { status: "InProgress", reporter: { id: "emp-1" }, assignedTo: { id: "tech-1" } };
    const mgrInProgressActions = getStatusActions(inProgressIncident, managerUser);
    assert.strictEqual(mgrInProgressActions.some((a) => a.label === "Resolve Incident"), false);
    assert.strictEqual(mgrInProgressActions.some((a) => a.label === "Waiting for User"), false);
    assert.strictEqual(mgrInProgressActions.some((a) => a.label === "Administrative Close"), true);
  });

  it("unrelated technician sees zero actions on another technician's incident", () => {
    const incident = { status: "InProgress", reporter: { id: "emp-1" }, assignedTo: { id: "tech-1" } };
    const actions = getStatusActions(incident, otherTech);
    assert.strictEqual(actions.length, 0);
  });

  it("manager and admin see Close Incident on Resolved incident", () => {
    const resolvedIncident = { status: "Resolved", reporter: { id: "emp-1" }, assignedTo: { id: "tech-1" } };
    const mgrActions = getStatusActions(resolvedIncident, managerUser);
    const adminActions = getStatusActions(resolvedIncident, adminUser);
    assert.strictEqual(mgrActions[0].label, "Close Incident");
    assert.strictEqual(adminActions[0].label, "Close Incident");
  });

  it("reporter employee sees Confirm & Close on own resolved incident only", () => {
    const ownResolved = { status: "Resolved", reporter: { id: "emp-1" }, assignedTo: { id: "tech-1" } };
    const otherResolved = { status: "Resolved", reporter: { id: "emp-2" }, assignedTo: { id: "tech-1" } };
    assert.strictEqual(getStatusActions(ownResolved, employeeUser)[0]?.label, "Confirm & Close");
    assert.strictEqual(getStatusActions(otherResolved, employeeUser).length, 0);
  });
});
