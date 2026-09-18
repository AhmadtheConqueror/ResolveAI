import { describe, it } from "node:test";
import assert from "node:assert";
import type {
  CurrentUser,
  IncidentAIResolutionAnalysis,
  IncidentDetail,
  IncidentStatus,
  SuggestedResolutionStep,
} from "../api/api";

describe("Frontend AI Resolution Assistant Logic (AI Phase 2)", () => {
  function canAccessResolutionAssistant(
    user: CurrentUser,
    incident: IncidentDetail
  ) {
    if (user.role === "Employee") {
      return false;
    }

    if (user.role === "Manager" || user.role === "Admin") {
      return true;
    }

    return (
      user.role === "Technician" &&
      incident.assignedTo?.id === user.id
    );
  }

  function canGenerateResolutionAssistant(
    user: CurrentUser,
    incident: IncidentDetail
  ) {
    if (!canAccessResolutionAssistant(user, incident)) {
      return false;
    }

    const activeStatuses: IncidentStatus[] = [
      "Open",
      "Triaged",
      "Assigned",
      "InProgress",
      "WaitingForUser",
    ];

    return activeStatuses.includes(incident.status);
  }

  function formatStepsForClipboard(steps: SuggestedResolutionStep[]): string {
    return steps
      .map(
        (s) =>
          `${s.stepNumber}. ${s.action}\n   Reason: ${s.reason}${
            s.evidenceIncidentNumbers && s.evidenceIncidentNumbers.length > 0
              ? `\n   Evidence: ${s.evidenceIncidentNumbers.join(", ")}`
              : ""
          }`
      )
      .join("\n\n");
  }

  const assignedTech: CurrentUser = {
    id: "tech-123",
    firstName: "Bob",
    lastName: "Technician",
    email: "bob@resolveai.internal",
    role: "Technician",
  };

  const unrelatedTech: CurrentUser = {
    id: "tech-999",
    firstName: "Other",
    lastName: "Technician",
    email: "other@resolveai.internal",
    role: "Technician",
  };

  const employee: CurrentUser = {
    id: "emp-100",
    firstName: "Alice",
    lastName: "Employee",
    email: "alice@resolveai.internal",
    role: "Employee",
  };

  const manager: CurrentUser = {
    id: "mgr-200",
    firstName: "Carol",
    lastName: "Manager",
    email: "carol@resolveai.internal",
    role: "Manager",
  };

  const admin: CurrentUser = {
    id: "adm-300",
    firstName: "David",
    lastName: "Admin",
    email: "david@resolveai.internal",
    role: "Admin",
  };

  const baseIncident: IncidentDetail = {
    id: "inc-1",
    incidentNumber: "INC-2026-0001",
    title: "VPN connection dropping",
    description: "Tunnel disconnects after cert renewal",
    category: "Network",
    priority: { name: "High", level: 3 },
    status: "InProgress",
    reporter: { id: "emp-100", name: "Alice Employee", email: "alice@resolveai.internal" },
    assignedTo: { id: "tech-123", name: "Bob Technician", email: "bob@resolveai.internal" },
    resolution: null,
    firstRespondedAt: null,
    resolvedAt: null,
    closedAt: null,
    createdAt: "2026-09-17T10:00:00Z",
    updatedAt: "2026-09-17T10:30:00Z",
  };

  it("1. employee cannot see or access AI Resolution Assistant controls", () => {
    assert.strictEqual(
      canAccessResolutionAssistant(employee, baseIncident),
      false,
      "Employee must NOT have access to resolution assistant"
    );
    assert.strictEqual(
      canGenerateResolutionAssistant(employee, baseIncident),
      false,
      "Employee must NOT be able to generate resolution assistance"
    );
  });

  it("2. assigned technician CAN access and generate AI Resolution Assistant", () => {
    assert.strictEqual(
      canAccessResolutionAssistant(assignedTech, baseIncident),
      true,
      "Assigned technician should have access"
    );
    assert.strictEqual(
      canGenerateResolutionAssistant(assignedTech, baseIncident),
      true,
      "Assigned technician should be able to generate on InProgress incident"
    );
  });

  it("3. unrelated technician CANNOT access AI Resolution Assistant on unassigned incident", () => {
    assert.strictEqual(
      canAccessResolutionAssistant(unrelatedTech, baseIncident),
      false,
      "Unrelated technician must NOT have access"
    );
    assert.strictEqual(
      canGenerateResolutionAssistant(unrelatedTech, baseIncident),
      false,
      "Unrelated technician must NOT be able to generate"
    );
  });

  it("4. manager and admin CAN access and generate AI Resolution Assistant", () => {
    assert.strictEqual(canAccessResolutionAssistant(manager, baseIncident), true);
    assert.strictEqual(canGenerateResolutionAssistant(manager, baseIncident), true);

    assert.strictEqual(canAccessResolutionAssistant(admin, baseIncident), true);
    assert.strictEqual(canGenerateResolutionAssistant(admin, baseIncident), true);
  });

  it("5. generation is restricted to active incidents and disabled for terminal statuses", () => {
    const activeStatuses: IncidentStatus[] = [
      "Open",
      "Triaged",
      "Assigned",
      "InProgress",
      "WaitingForUser",
    ];

    for (const status of activeStatuses) {
      const activeInc = { ...baseIncident, status };
      assert.strictEqual(
        canGenerateResolutionAssistant(assignedTech, activeInc),
        true,
        `Status ${status} should allow generation`
      );
    }

    const resolvedInc: IncidentDetail = { ...baseIncident, status: "Resolved" };
    const closedInc: IncidentDetail = { ...baseIncident, status: "Closed" };

    assert.strictEqual(
      canGenerateResolutionAssistant(assignedTech, resolvedInc),
      false,
      "Resolved incident must NOT allow generating new analysis"
    );
    assert.strictEqual(
      canGenerateResolutionAssistant(assignedTech, closedInc),
      false,
      "Closed incident must NOT allow generating new analysis"
    );
  });

  it("6. formats suggested troubleshooting steps accurately for clipboard copy", () => {
    const steps: SuggestedResolutionStep[] = [
      {
        stepNumber: 1,
        action: "Verify the active certificate expiration date.",
        reason: "Two similar incidents had expired client certs.",
        evidenceIncidentNumbers: ["INC-2026-A1", "INC-2026-B2"],
      },
      {
        stepNumber: 2,
        action: "Clear local credential cache and restart VPN agent.",
        reason: "Resolves stale token re-auth loop.",
        evidenceIncidentNumbers: ["INC-2026-A1"],
      },
    ];

    const formatted = formatStepsForClipboard(steps);

    assert.match(formatted, /1\. Verify the active certificate expiration date\./);
    assert.match(formatted, /Evidence: INC-2026-A1, INC-2026-B2/);
    assert.match(formatted, /2\. Clear local credential cache and restart VPN agent\./);
  });

  it("7. correctly identifies insufficient evidence state and flags general guidance", () => {
    const analysisWithNoEvidence: IncidentAIResolutionAnalysis = {
      id: "res-1",
      incidentId: "inc-1",
      summary: "Rare obscure kernel panic",
      likelyIssue: "Driver defect",
      confidence: "Low",
      suggestedSteps: [
        {
          stepNumber: 1,
          action: "Collect crash dump via WinDbg",
          reason: "General debugging",
          evidenceIncidentNumbers: [],
        },
      ],
      evidence: [],
      caveats: "General AI guidance — not derived from ResolveAI historical incidents.",
      hasSufficientEvidence: false,
      candidateCount: 0,
      provider: "Gemini",
      model: "gemini-2.5-flash",
      promptVersion: "resolution-assistant-v1",
      createdAt: "2026-09-17T11:00:00Z",
      requestedByUserId: "tech-123",
    };

    assert.strictEqual(analysisWithNoEvidence.hasSufficientEvidence, false);
    assert.strictEqual(analysisWithNoEvidence.evidence.length, 0);
    assert.strictEqual(analysisWithNoEvidence.confidence, "Low");
    assert.match(analysisWithNoEvidence.caveats, /General AI guidance/);
  });
});
