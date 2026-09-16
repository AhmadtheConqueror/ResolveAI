import { useCallback, useEffect, useMemo, useState } from "react";
import { Link, useNavigate } from "react-router-dom";

import {
  getIncidents,
  isUnauthorizedError,
} from "../api/api";
import type { CurrentUser, Incident } from "../api/api";
import NewIncidentModal from "../components/NewIncidentModal";

function readCurrentUser() {
  const storedUser = sessionStorage.getItem("currentUser");

  if (!storedUser) {
    return null;
  }

  try {
    return JSON.parse(storedUser) as CurrentUser;
  } catch {
    return null;
  }
}

function formatDate(value: string) {
  return new Intl.DateTimeFormat("en", {
    month: "short",
    day: "numeric",
    year: "numeric",
  }).format(new Date(value));
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

export default function DashboardPage() {
  const navigate = useNavigate();

  const [user] = useState<CurrentUser | null>(() =>
    readCurrentUser()
  );
  const [incidents, setIncidents] = useState<Incident[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [showNewIncident, setShowNewIncident] = useState(false);

  const handleUnauthorized = useCallback(() => {
    sessionStorage.clear();
    navigate("/", { replace: true });
  }, [navigate]);

  const loadIncidents = useCallback(async () => {
    setLoading(true);

    try {
      setError("");

      const data = await getIncidents();

      setIncidents(data);
    } catch (error) {
      if (isUnauthorizedError(error)) {
        handleUnauthorized();
        return;
      }

      setError(
        error instanceof Error
          ? error.message
          : "Unable to load incidents."
      );
    } finally {
      setLoading(false);
    }
  }, [handleUnauthorized]);

  useEffect(() => {
    if (!user) {
      handleUnauthorized();
      return;
    }

    void Promise.resolve().then(loadIncidents);
  }, [handleUnauthorized, loadIncidents, user]);

  function logout() {
    sessionStorage.clear();
    navigate("/", { replace: true });
  }

  const kpis = useMemo(() => {
    const open = incidents.filter(
      (incident) =>
        !["closed", "resolved"].includes(
          incident.status.toLowerCase()
        )
    ).length;

    const critical = incidents.filter(
      (incident) =>
        incident.priority.toLowerCase() === "critical"
    ).length;

    return {
      total: incidents.length,
      open,
      critical,
    };
  }, [incidents]);

  const handleIncidentCreated = useCallback(async () => {
    setShowNewIncident(false);
    await loadIncidents();
  }, [loadIncidents]);

  return (
    <div className="app-shell">
      <aside className="sidebar">
        <div className="sidebar-brand">
          <div className="sidebar-logo">R</div>

          <div>
            <strong>ResolveAI</strong>
            <span>Incident Management</span>
          </div>
        </div>

        <nav className="sidebar-nav" aria-label="Main navigation">
          <button
            type="button"
            className="nav-item active"
            aria-current="page"
          >
            Dashboard
          </button>

          <button type="button" className="nav-item">
            Incidents
          </button>

          <button type="button" className="nav-item">
            My Work
          </button>

          <button type="button" className="nav-item">
            Analytics
          </button>

          {user?.role === "Admin" && (
            <button
              type="button"
              className="nav-item"
              onClick={() => navigate("/users")}
            >
              Users
            </button>
          )}
        </nav>


        <div className="sidebar-footer">
          <div className="user-summary">
            <strong>
              {user
                ? `${user.firstName} ${user.lastName}`
                : "Signed out"}
            </strong>

            <span>{user?.role ?? "No active session"}</span>
          </div>

          <button
            type="button"
            className="logout-button"
            onClick={logout}
          >
            Sign out
          </button>
        </div>
      </aside>

      <main className="main-content">
        <div className="content-inner">
          <header className="topbar">
            <div>
              <span className="eyebrow">Operations</span>
              <h1>Dashboard</h1>

              <p>
                Welcome back, {user?.firstName ?? "there"}.
              </p>
            </div>

            <button
              type="button"
              className="new-incident-button"
              onClick={() => setShowNewIncident(true)}
            >
              <span aria-hidden="true">+</span>
              New Incident
            </button>
          </header>

          <section className="kpi-grid" aria-label="Incident summary">
            <div className="kpi-card">
              <span>Total Incidents</span>
              <strong>{kpis.total}</strong>
              <small>All recorded incidents</small>
            </div>

            <div className="kpi-card">
              <span>Open Incidents</span>
              <strong>{kpis.open}</strong>
              <small>Currently active</small>
            </div>

            <div className="kpi-card">
              <span>Critical</span>
              <strong>{kpis.critical}</strong>
              <small>Require attention</small>
            </div>

            <div className="kpi-card">
              <span>SLA Success</span>
              <strong>N/A</strong>
              <small>Coming soon</small>
            </div>
          </section>

          <section className="incidents-panel">
            <div className="panel-header">
              <div>
                <h2>Recent Incidents</h2>

                <p>
                  Latest incidents recorded in ResolveAI
                </p>
              </div>
            </div>

            {loading && (
              <div className="table-message">
                Loading incidents...
              </div>
            )}

            {error && (
              <div className="error-message">
                {error}
              </div>
            )}

            {!loading &&
              !error &&
              incidents.length === 0 && (
                <div className="table-message">
                  No incidents have been recorded yet.
                </div>
              )}

            {!loading &&
              !error &&
              incidents.length > 0 && (
                <div className="table-wrapper">
                  <table className="incidents-table">
                    <thead>
                      <tr>
                        <th>Incident Number</th>
                        <th>Title</th>
                        <th>Category</th>
                        <th>Priority</th>
                        <th>Status</th>
                        <th>Created Date</th>
                      </tr>
                    </thead>

                    <tbody>
                      {incidents.map((incident) => (
                        <tr key={incident.id}>
                          <td className="incident-number">
                            <Link
                              className="incident-number-link"
                              to={`/incidents/${incident.id}`}
                            >
                              {incident.incidentNumber}
                            </Link>
                          </td>

                          <td className="incident-title">
                            {incident.title}
                          </td>

                          <td>{incident.category}</td>

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

                          <td>{formatDate(incident.createdAt)}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
          </section>
        </div>
      </main>

      {showNewIncident && (
        <NewIncidentModal
          onClose={() => setShowNewIncident(false)}
          onCreated={handleIncidentCreated}
          onUnauthorized={handleUnauthorized}
        />
      )}
    </div>
  );
}
