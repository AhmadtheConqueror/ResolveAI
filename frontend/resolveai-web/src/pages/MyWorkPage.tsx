import { useCallback, useEffect, useState } from "react";
import { Link, useNavigate } from "react-router-dom";

import {
  assignIncident,
  getIncidents,
  getQueueSummary,
  getTechnicians,
  getTechnicianWorkload,
  isUnauthorizedError,
  updateIncidentStatus,
} from "../api/api";
import type {
  CurrentUser,
  Incident,
  IncidentSlaCompact,
  QueueSummary,
  TechnicianUser,
  TechnicianWorkload,
} from "../api/api";
import Sidebar from "../components/Sidebar";

function readCurrentUser(): CurrentUser | null {
  const storedUser = sessionStorage.getItem("currentUser");
  if (!storedUser) return null;
  try {
    return JSON.parse(storedUser) as CurrentUser;
  } catch {
    return null;
  }
}

function formatDate(value?: string | null) {
  if (!value) return "—";
  return new Intl.DateTimeFormat("en", {
    month: "short",
    day: "numeric",
    year: "numeric",
  }).format(new Date(value));
}

function formatMinutes(minutes: number): string {
  if (minutes < 60) return `${minutes}m`;
  const h = Math.floor(minutes / 60);
  const m = minutes % 60;
  return m > 0 ? `${h}h ${m}m` : `${h}h`;
}

function formatSlaTiming(sla?: IncidentSlaCompact): string | null {
  if (!sla) return null;
  if (sla.overallStatus === "Met") return "Met target";
  if (sla.overallStatus === "NotApplicable") return null;

  if (sla.responseOverdueMinutes && sla.responseOverdueMinutes > 0) {
    return `${formatMinutes(sla.responseOverdueMinutes)} overdue`;
  }
  if (sla.resolutionOverdueMinutes && sla.resolutionOverdueMinutes > 0) {
    return `${formatMinutes(sla.resolutionOverdueMinutes)} overdue`;
  }
  if (
    sla.responseRemainingMinutes !== undefined &&
    sla.responseRemainingMinutes !== null &&
    sla.responseRemainingMinutes > 0
  ) {
    return `${formatMinutes(sla.responseRemainingMinutes)} remaining`;
  }
  if (
    sla.resolutionRemainingMinutes !== undefined &&
    sla.resolutionRemainingMinutes !== null &&
    sla.resolutionRemainingMinutes > 0
  ) {
    return `${formatMinutes(sla.resolutionRemainingMinutes)} remaining`;
  }
  return null;
}

function formatSlaStatus(status: string) {
  switch (status) {
    case "OnTrack":
      return "On Track";
    case "AtRisk":
      return "At Risk";
    case "Breached":
      return "Breached";
    case "Met":
      return "Met";
    case "NotApplicable":
      return "N/A";
    default:
      return status;
  }
}

function getSlaBadgeClass(status: string) {
  switch (status) {
    case "OnTrack":
      return "sla-on-track";
    case "AtRisk":
      return "sla-at-risk";
    case "Breached":
      return "sla-breached";
    case "Met":
      return "sla-met";
    default:
      return "sla-na";
  }
}

function getBadgeClass(prefix: string, value: string) {
  const slug = value
    .trim()
    .replace(/([a-z])([A-Z])/g, "$1-$2")
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/(^-|-$)/g, "");

  return `${prefix}-${slug || "default"}`;
}

type ManagerTab = "triage" | "unassigned" | "sla" | "active" | "resolved";
type TechnicianTab = "all" | "assigned" | "inprogress" | "waiting" | "sla";

export default function MyWorkPage() {
  const navigate = useNavigate();
  const [user] = useState<CurrentUser | null>(() => readCurrentUser());

  const role = user?.role ?? "";
  const isEmployee = role === "Employee";
  const isTechnician = role === "Technician";
  const isManager = role === "Manager";
  const isAdmin = role === "Admin";

  // Redirect employee to /incidents
  useEffect(() => {
    if (isEmployee) {
      navigate("/incidents", { replace: true });
    }
  }, [isEmployee, navigate]);

  // Active tab states
  const [managerTab, setManagerTab] = useState<ManagerTab>("triage");
  const [techTab, setTechTab] = useState<TechnicianTab>("all");

  // Data states
  const [queueSummary, setQueueSummary] = useState<QueueSummary | null>(null);
  const [workload, setWorkload] = useState<TechnicianWorkload[]>([]);
  const [technicians, setTechnicians] = useState<TechnicianUser[]>([]);
  const [incidents, setIncidents] = useState<Incident[]>([]);
  const [loading, setLoading] = useState(true);
  const [actionBusyId, setActionBusyId] = useState<string | null>(null);
  const [inlineAssignments, setInlineAssignments] = useState<
    Record<string, string>
  >({});
  const [error, setError] = useState("");
  const [actionNotice, setActionNotice] = useState<{
    tone: "success" | "error";
    message: string;
  } | null>(null);

  const handleUnauthorized = useCallback(() => {
    sessionStorage.clear();
    navigate("/", { replace: true });
  }, [navigate]);

  // Load Queue Summary & Workload
  const loadOverview = useCallback(async () => {
    try {
      const summary = await getQueueSummary();
      setQueueSummary(summary);

      if (isManager || isAdmin) {
        const [wl, techs] = await Promise.all([
          getTechnicianWorkload(),
          getTechnicians(),
        ]);
        setWorkload(wl);
        setTechnicians(techs);
      }
    } catch (err) {
      if (isUnauthorizedError(err)) {
        handleUnauthorized();
      }
    }
  }, [handleUnauthorized, isAdmin, isManager]);

  // Load Incidents for the selected tab
  const loadTabIncidents = useCallback(async () => {
    setLoading(true);
    setError("");

    try {
      let queryParams: Record<string, string | number> = {
        sortBy: "urgency",
        sortDirection: "desc",
      };

      if (isTechnician) {
        // Technician queue: incidents assigned to me
        queryParams = {
          ...queryParams,
          assignedToId: user?.id ?? "",
        };

        if (techTab === "assigned") {
          queryParams.status = "Assigned";
        } else if (techTab === "inprogress") {
          queryParams.status = "InProgress";
        } else if (techTab === "waiting") {
          queryParams.status = "WaitingForUser";
        } else if (techTab === "sla") {
          queryParams.status = "active";
          queryParams.slaStatus = "Attention";
        } else {
          // "all" active assigned to me
          queryParams.status = "active";
        }
      } else {
        // Manager / Admin operational queues
        if (managerTab === "triage") {
          queryParams.status = "Open";
        } else if (managerTab === "unassigned") {
          queryParams.assignment = "unassigned";
          queryParams.status = "Triaged,Assigned";
        } else if (managerTab === "sla") {
          queryParams.status = "active";
          queryParams.slaStatus = "Attention";
        } else if (managerTab === "resolved") {
          queryParams.status = "Resolved";
        } else {
          // "active"
          queryParams.status = "active";
        }
      }

      const items = await getIncidents(queryParams);
      setIncidents(items);
    } catch (err) {
      if (isUnauthorizedError(err)) {
        handleUnauthorized();
        return;
      }
      setError(
        err instanceof Error ? err.message : "Unable to load queue incidents."
      );
    } finally {
      setLoading(false);
    }
  }, [
    handleUnauthorized,
    isTechnician,
    managerTab,
    techTab,
    user,
  ]);

  useEffect(() => {
    if (!user || isEmployee) return;
    void Promise.resolve().then(loadOverview);
    void Promise.resolve().then(loadTabIncidents);
  }, [isEmployee, loadOverview, loadTabIncidents, user]);

  // Quick Action: Triage Open incident
  async function handleQuickTriage(incidentId: string) {
    setActionBusyId(incidentId);
    setActionNotice(null);

    try {
      await updateIncidentStatus(incidentId, "Triaged");
      setActionNotice({
        tone: "success",
        message: "Incident marked as Triaged and moved to Unassigned queue.",
      });
      await loadOverview();
      await loadTabIncidents();
    } catch (err) {
      setActionNotice({
        tone: "error",
        message:
          err instanceof Error ? err.message : "Unable to triage incident.",
      });
    } finally {
      setActionBusyId(null);
    }
  }

  // Quick Action: Assign technician
  async function handleQuickAssign(incidentId: string) {
    const techId = inlineAssignments[incidentId];
    if (!techId) return;

    setActionBusyId(incidentId);
    setActionNotice(null);

    try {
      await assignIncident(incidentId, techId);
      setActionNotice({
        tone: "success",
        message: "Technician assigned successfully.",
      });
      await loadOverview();
      await loadTabIncidents();
    } catch (err) {
      setActionNotice({
        tone: "error",
        message:
          err instanceof Error ? err.message : "Unable to assign technician.",
      });
    } finally {
      setActionBusyId(null);
    }
  }

  if (isEmployee) {
    return null;
  }

  return (
    <div className="app-shell">
      <Sidebar
        currentTab="my-work"
        user={user}
        onLogout={() => {
          sessionStorage.clear();
          navigate("/", { replace: true });
        }}
      />

      <main className="main-content">
        <div className="content-inner">
          <header className="topbar">
            <div>
              <span className="eyebrow">
                {isTechnician
                  ? "Field & Queue Operations"
                  : isAdmin
                    ? "Operational Oversight"
                    : "Operations Command"}
              </span>
              <h1>
                {isTechnician
                  ? "My Work"
                  : isAdmin
                    ? "Operational Oversight"
                    : "Operations Queue"}
              </h1>
              <p>
                {isTechnician
                  ? "Incidents currently assigned to you, ordered by SLA urgency."
                  : "Incidents requiring triage, assignment or management attention."}
              </p>
            </div>
          </header>

          {actionNotice && (
            <div
              className={`inline-notice ${actionNotice.tone}`}
              role="status"
            >
              {actionNotice.message}
            </div>
          )}

          {/* Manager / Admin Workload Overview Panel */}
          {(isManager || isAdmin) && workload.length > 0 && (
            <section className="detail-card detail-card-wide workload-card">
              <div className="workload-card-header">
                <div>
                  <h2>Technician Workload</h2>
                  <p className="subtext">
                    Active operational capacity across the support engineering team.
                  </p>
                </div>
              </div>

              <div className="workload-grid">
                {workload.map((wl) => (
                  <div key={wl.technician.id} className="workload-item">
                    <div className="workload-tech-info">
                      <strong>{wl.technician.name}</strong>
                      <span className="workload-tech-email">
                        {wl.technician.email}
                      </span>
                    </div>

                    <div className="workload-metrics">
                      <div className="workload-total-pill">
                        <strong>{wl.activeCount}</strong>
                        <span>Active</span>
                      </div>

                      <div className="workload-breakdown">
                        <span title="Assigned">
                          Assigned: <strong>{wl.assignedCount}</strong>
                        </span>
                        <span title="In Progress">
                          In Progress: <strong>{wl.inProgressCount}</strong>
                        </span>
                        <span title="Waiting for User">
                          Waiting: <strong>{wl.waitingForUserCount}</strong>
                        </span>
                      </div>
                    </div>
                  </div>
                ))}
              </div>
            </section>
          )}

          {/* Queue Tab Navigation */}
          <div className="queue-tabs-wrapper">
            {isTechnician ? (
              <nav className="queue-tabs" aria-label="Technician queues">
                <button
                  type="button"
                  id="tab-all-assigned"
                  className={`queue-tab-btn ${techTab === "all" ? "active" : ""}`}
                  onClick={() => setTechTab("all")}
                >
                  All Assigned
                  <span className="queue-tab-badge">
                    {queueSummary?.allAssignedCount ?? 0}
                  </span>
                </button>

                <button
                  type="button"
                  id="tab-assigned"
                  className={`queue-tab-btn ${
                    techTab === "assigned" ? "active" : ""
                  }`}
                  onClick={() => setTechTab("assigned")}
                >
                  Assigned
                  <span className="queue-tab-badge">
                    {queueSummary?.assignedCount ?? 0}
                  </span>
                </button>

                <button
                  type="button"
                  id="tab-in-progress"
                  className={`queue-tab-btn ${
                    techTab === "inprogress" ? "active" : ""
                  }`}
                  onClick={() => setTechTab("inprogress")}
                >
                  In Progress
                  <span className="queue-tab-badge">
                    {queueSummary?.inProgressCount ?? 0}
                  </span>
                </button>

                <button
                  type="button"
                  id="tab-waiting"
                  className={`queue-tab-btn ${
                    techTab === "waiting" ? "active" : ""
                  }`}
                  onClick={() => setTechTab("waiting")}
                >
                  Waiting for User
                  <span className="queue-tab-badge">
                    {queueSummary?.waitingForUserCount ?? 0}
                  </span>
                </button>

                <button
                  type="button"
                  id="tab-sla-attention"
                  className={`queue-tab-btn alert-tab ${
                    techTab === "sla" ? "active" : ""
                  }`}
                  onClick={() => setTechTab("sla")}
                >
                  SLA Attention
                  <span className="queue-tab-badge alert">
                    {queueSummary?.slaAttentionCount ?? 0}
                  </span>
                </button>
              </nav>
            ) : (
              <nav className="queue-tabs" aria-label="Operations queues">
                <button
                  type="button"
                  id="tab-triage"
                  className={`queue-tab-btn ${
                    managerTab === "triage" ? "active" : ""
                  }`}
                  onClick={() => setManagerTab("triage")}
                >
                  Triage
                  <span className="queue-tab-badge highlight">
                    {queueSummary?.triageCount ?? 0}
                  </span>
                </button>

                <button
                  type="button"
                  id="tab-unassigned"
                  className={`queue-tab-btn ${
                    managerTab === "unassigned" ? "active" : ""
                  }`}
                  onClick={() => setManagerTab("unassigned")}
                >
                  Unassigned
                  <span className="queue-tab-badge highlight">
                    {queueSummary?.unassignedCount ?? 0}
                  </span>
                </button>

                <button
                  type="button"
                  id="tab-sla-attention"
                  className={`queue-tab-btn alert-tab ${
                    managerTab === "sla" ? "active" : ""
                  }`}
                  onClick={() => setManagerTab("sla")}
                >
                  SLA Attention
                  <span className="queue-tab-badge alert">
                    {queueSummary?.slaAttentionCount ?? 0}
                  </span>
                </button>

                <button
                  type="button"
                  id="tab-active"
                  className={`queue-tab-btn ${
                    managerTab === "active" ? "active" : ""
                  }`}
                  onClick={() => setManagerTab("active")}
                >
                  Active
                  <span className="queue-tab-badge">
                    {queueSummary?.activeCount ?? 0}
                  </span>
                </button>

                <button
                  type="button"
                  id="tab-resolved"
                  className={`queue-tab-btn ${
                    managerTab === "resolved" ? "active" : ""
                  }`}
                  onClick={() => setManagerTab("resolved")}
                >
                  Resolved / Awaiting Closure
                  <span className="queue-tab-badge">
                    {queueSummary?.resolvedCount ?? 0}
                  </span>
                </button>
              </nav>
            )}
          </div>

          {/* Incident Queue Table */}
          <section className="detail-card detail-card-wide table-card">
            <div className="table-card-header">
              <h2>
                {isTechnician
                  ? techTab === "all"
                    ? "All Assigned Incidents"
                    : techTab === "assigned"
                      ? "Assigned (Pending Action)"
                      : techTab === "inprogress"
                        ? "Work In Progress"
                        : techTab === "waiting"
                          ? "Waiting For User Response"
                          : "SLA Urgent / At Risk"
                  : managerTab === "triage"
                    ? "Incoming Incidents Awaiting Triage"
                    : managerTab === "unassigned"
                      ? "Triaged Incidents Requiring Assignment"
                      : managerTab === "sla"
                        ? "At Risk or Breached Incidents"
                        : managerTab === "resolved"
                          ? "Resolved Incidents Awaiting Closure"
                          : "Active Incidents Operations Queue"}
                <span className="count-pill">{incidents.length}</span>
              </h2>

              <span className="table-header-meta">
                Sorted by SLA Urgency
              </span>
            </div>

            {error && (
              <div className="inline-notice error" role="alert">
                {error}
              </div>
            )}

            {loading ? (
              <div className="table-loading-state">
                <span className="spinner-indicator" aria-hidden="true" />
                Loading queue items…
              </div>
            ) : incidents.length === 0 ? (
              <div className="table-empty-state">
                <p>
                  {isTechnician
                    ? techTab === "sla"
                      ? "No incidents assigned to you currently require SLA attention."
                      : "No incidents are currently assigned to you in this queue."
                    : managerTab === "triage"
                      ? "No incidents are awaiting triage."
                      : managerTab === "unassigned"
                        ? "No operational incidents are currently unassigned."
                        : managerTab === "sla"
                          ? "No incidents currently require SLA attention."
                          : "No incidents in this operational queue."}
                </p>
              </div>
            ) : (
              <div className="table-responsive">
                <table
                  className="incident-table queue-table"
                  aria-label="Queue items"
                >
                  <thead>
                    <tr>
                      <th scope="col">Incident</th>
                      <th scope="col">Title</th>
                      <th scope="col">Priority</th>
                      <th scope="col">Status</th>
                      <th scope="col">SLA Status</th>
                      <th scope="col">Reporter</th>
                      <th scope="col">
                        {isTechnician || managerTab !== "unassigned"
                          ? "Assigned To"
                          : "Assign"}
                      </th>
                      <th scope="col">Updated</th>
                      {(managerTab === "triage" ||
                        managerTab === "unassigned") && (
                        <th scope="col">Quick Action</th>
                      )}
                    </tr>
                  </thead>
                  <tbody>
                    {incidents.map((incident) => {
                      const timing = formatSlaTiming(incident.sla);
                      const isBusy = actionBusyId === incident.id;

                      return (
                        <tr key={incident.id}>
                          <td>
                            <Link
                              to={`/incidents/${incident.id}`}
                              className="incident-number-link"
                            >
                              {incident.incidentNumber}
                            </Link>
                          </td>

                          <td className="incident-title-cell">
                            <Link
                              to={`/incidents/${incident.id}`}
                              className="incident-title-link"
                            >
                              {incident.title}
                            </Link>
                          </td>

                          <td>
                            <span
                              className={`badge ${getBadgeClass(
                                "priority",
                                incident.priority
                              )}`}
                            >
                              {incident.priority}
                            </span>
                          </td>

                          <td>
                            <span
                              className={`badge ${getBadgeClass(
                                "status",
                                incident.status
                              )}`}
                            >
                              {incident.status}
                            </span>
                          </td>

                          <td>
                            {incident.sla ? (
                              <div className="sla-cell">
                                <span
                                  className={`sla-badge ${getSlaBadgeClass(
                                    incident.sla.overallStatus
                                  )}`}
                                >
                                  {formatSlaStatus(incident.sla.overallStatus)}
                                </span>
                                {incident.sla.requiresEscalation && (
                                  <span className="sla-escalation-tag">
                                    Escalate
                                  </span>
                                )}
                                {timing && (
                                  <span className="sla-timing-sub">
                                    {timing}
                                  </span>
                                )}
                              </div>
                            ) : (
                              <span className="sla-badge sla-na">N/A</span>
                            )}
                          </td>

                          <td>{incident.reporter?.name ?? "—"}</td>

                          <td>
                            {managerTab === "unassigned" ? (
                              <select
                                className="quick-assign-select"
                                value={inlineAssignments[incident.id] || ""}
                                onChange={(e) =>
                                  setInlineAssignments((prev) => ({
                                    ...prev,
                                    [incident.id]: e.target.value,
                                  }))
                                }
                                disabled={isBusy}
                                aria-label="Select technician to assign"
                              >
                                <option value="">Assign technician…</option>
                                {technicians.map((t) => (
                                  <option key={t.id} value={t.id}>
                                    {t.firstName} {t.lastName}
                                  </option>
                                ))}
                              </select>
                            ) : incident.assignedTo ? (
                              incident.assignedTo.name
                            ) : (
                              <span className="unassigned-chip">
                                Unassigned
                              </span>
                            )}
                          </td>

                          <td>{formatDate(incident.updatedAt)}</td>

                          {(managerTab === "triage" ||
                            managerTab === "unassigned") && (
                            <td>
                              {managerTab === "triage" && (
                                <button
                                  type="button"
                                  className="quick-action-btn primary"
                                  disabled={isBusy}
                                  onClick={() =>
                                    void handleQuickTriage(incident.id)
                                  }
                                >
                                  {isBusy ? "Triaging…" : "Triage"}
                                </button>
                              )}

                              {managerTab === "unassigned" && (
                                <button
                                  type="button"
                                  className="quick-action-btn secondary"
                                  disabled={
                                    isBusy ||
                                    !inlineAssignments[incident.id]
                                  }
                                  onClick={() =>
                                    void handleQuickAssign(incident.id)
                                  }
                                >
                                  {isBusy ? "Assigning…" : "Assign"}
                                </button>
                              )}
                            </td>
                          )}
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            )}
          </section>
        </div>
      </main>
    </div>
  );
}
