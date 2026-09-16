import { useCallback, useEffect, useMemo, useState } from "react";
import { useNavigate } from "react-router-dom";

import {
  getAnalyticsOverview,
  isForbiddenError,
  isUnauthorizedError,
} from "../api/api";
import type {
  AnalyticsOverview,
  AnalyticsRange,
  CurrentUser,
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

function formatPercent(value: number | null) {
  return value === null ? "N/A" : `${Math.round(value)}%`;
}

function formatDuration(minutes: number | null) {
  if (minutes === null) return "N/A";

  const rounded = Math.max(0, Math.round(minutes));
  if (rounded < 60) return `${rounded} min`;

  const hours = Math.floor(rounded / 60);
  const remainingMinutes = rounded % 60;

  if (hours < 24) {
    return remainingMinutes > 0
      ? `${hours}h ${remainingMinutes}m`
      : `${hours}h`;
  }

  const days = Math.floor(hours / 24);
  const remainingHours = hours % 24;

  return remainingHours > 0 ? `${days}d ${remainingHours}h` : `${days}d`;
}

function formatDateLabel(value: string) {
  return new Intl.DateTimeFormat("en", {
    month: "short",
    day: "numeric",
  }).format(new Date(value));
}

function getMaxCount(items: Array<{ count: number }>) {
  return Math.max(1, ...items.map((item) => item.count));
}

type CountBarProps = {
  label: string;
  count: number;
  max: number;
};

function CountBar({ label, count, max }: CountBarProps) {
  const width = `${Math.max(4, (count / max) * 100)}%`;

  return (
    <div className="analytics-count-row">
      <div className="analytics-count-label">
        <span>{label}</span>
        <strong>{count}</strong>
      </div>
      <div className="analytics-count-track" aria-hidden="true">
        <span style={{ width }} />
      </div>
    </div>
  );
}

export default function AnalyticsPage() {
  const navigate = useNavigate();
  const [user] = useState<CurrentUser | null>(() => readCurrentUser());
  const [range, setRange] = useState<AnalyticsRange>("30");
  const [data, setData] = useState<AnalyticsOverview | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  const role = user?.role ?? "";
  const canViewAnalytics =
    role === "Technician" || role === "Manager" || role === "Admin";
  const canViewOrganizationTables = role === "Manager" || role === "Admin";

  const handleUnauthorized = useCallback(() => {
    sessionStorage.clear();
    navigate("/", { replace: true });
  }, [navigate]);

  useEffect(() => {
    if (!user) {
      handleUnauthorized();
      return;
    }

    if (!canViewAnalytics) {
      navigate("/dashboard", { replace: true });
    }
  }, [canViewAnalytics, handleUnauthorized, navigate, user]);

  const loadAnalytics = useCallback(async () => {
    if (!user || !canViewAnalytics) return;

    setLoading(true);
    setError("");

    try {
      const overview = await getAnalyticsOverview({ range });
      setData(overview);
    } catch (err) {
      if (isUnauthorizedError(err)) {
        handleUnauthorized();
        return;
      }

      if (isForbiddenError(err)) {
        setError("You do not have permission to view analytics.");
        return;
      }

      setError(
        err instanceof Error ? err.message : "Unable to load analytics."
      );
    } finally {
      setLoading(false);
    }
  }, [canViewAnalytics, handleUnauthorized, range, user]);

  useEffect(() => {
    void Promise.resolve().then(loadAnalytics);
  }, [loadAnalytics]);

  function logout() {
    sessionStorage.clear();
    navigate("/", { replace: true });
  }

  const statusMax = useMemo(
    () => getMaxCount(data?.byStatus ?? []),
    [data]
  );
  const priorityMax = useMemo(
    () => getMaxCount(data?.byPriority ?? []),
    [data]
  );
  const categoryMax = useMemo(
    () => getMaxCount(data?.byCategory ?? []),
    [data]
  );
  const trendMax = useMemo(
    () => getMaxCount(data?.incidentTrend ?? []),
    [data]
  );

  if (!canViewAnalytics) {
    return null;
  }

  return (
    <div className="app-shell">
      <Sidebar
        currentTab="analytics"
        user={user}
        onLogout={logout}
      />

      <main className="main-content">
        <div className="content-inner analytics-page">
          <header className="topbar analytics-topbar">
            <div>
              <span className="eyebrow">Operations</span>
              <h1>Analytics</h1>
              <p>Operational performance and service delivery insights.</p>
            </div>

            <div className="range-selector" aria-label="Analytics date range">
              {(["7", "30", "90", "all"] as AnalyticsRange[]).map((item) => (
                <button
                  key={item}
                  type="button"
                  className={range === item ? "active" : ""}
                  onClick={() => setRange(item)}
                >
                  {item === "all" ? "All time" : `${item} days`}
                </button>
              ))}
            </div>
          </header>

          {error && (
            <div className="inline-notice error" role="alert">
              {error}
            </div>
          )}

          {loading && (
            <div className="table-loading-state">
              <span className="spinner-indicator" aria-hidden="true" />
              Loading analytics...
            </div>
          )}

          {!loading && !error && data && (
            <>
              <section className="kpi-grid analytics-kpi-grid">
                <div className="kpi-card">
                  <span>Total Incidents</span>
                  <strong>{data.kpis.totalIncidents}</strong>
                  <small>Created in selected period</small>
                </div>

                <div className="kpi-card">
                  <span>Active Incidents</span>
                  <strong>{data.kpis.activeIncidents}</strong>
                  <small>Resolved and closed excluded</small>
                </div>

                <div className="kpi-card">
                  <span>SLA Success</span>
                  <strong>{formatPercent(data.kpis.slaSuccessPercent)}</strong>
                  <small>Resolved within resolution target</small>
                </div>

                <div className="kpi-card">
                  <span>Avg Resolution Time</span>
                  <strong>
                    {formatDuration(data.kpis.averageResolutionMinutes)}
                  </strong>
                  <small>Resolved incidents in scope</small>
                </div>
              </section>

              {data.kpis.totalIncidents === 0 && (
                <div className="analytics-empty-state">
                  No incident data is available for this period.
                </div>
              )}

              <section className="analytics-grid two-column">
                <div className="detail-card analytics-card">
                  <div className="analytics-card-header">
                    <h2>Incident Volume Trend</h2>
                  </div>
                  {data.incidentTrend.length === 0 ? (
                    <div className="analytics-chart-empty">
                      No incident data is available for this period.
                    </div>
                  ) : (
                    <div className="trend-chart">
                      {data.incidentTrend.map((point) => (
                        <div className="trend-bar-wrap" key={point.date}>
                          <span
                            className="trend-bar"
                            style={{
                              height: `${Math.max(
                                4,
                                (point.count / trendMax) * 100
                              )}%`,
                            }}
                            title={`${formatDateLabel(point.date)}: ${point.count}`}
                          />
                          <small>{formatDateLabel(point.date)}</small>
                        </div>
                      ))}
                    </div>
                  )}
                </div>

                <div className="detail-card analytics-card">
                  <div className="analytics-card-header">
                    <h2>Status Distribution</h2>
                  </div>
                  <div className="analytics-count-list">
                    {data.byStatus.map((item) => (
                      <CountBar
                        key={item.status}
                        label={item.status}
                        count={item.count}
                        max={statusMax}
                      />
                    ))}
                  </div>
                </div>
              </section>

              <section className="analytics-grid two-column">
                <div className="detail-card analytics-card">
                  <div className="analytics-card-header">
                    <h2>Incidents by Priority</h2>
                  </div>
                  <div className="analytics-count-list">
                    {data.byPriority.map((item) => (
                      <CountBar
                        key={item.priority}
                        label={item.priority}
                        count={item.count}
                        max={priorityMax}
                      />
                    ))}
                  </div>
                </div>

                <div className="detail-card analytics-card">
                  <div className="analytics-card-header">
                    <h2>Incidents by Category</h2>
                  </div>
                  <div className="analytics-count-list">
                    {data.byCategory.map((item) => (
                      <CountBar
                        key={item.category}
                        label={item.category}
                        count={item.count}
                        max={categoryMax}
                      />
                    ))}
                  </div>
                </div>
              </section>

              <section className="detail-card analytics-card">
                <div className="analytics-card-header">
                  <h2>SLA Performance</h2>
                </div>
                <div className="sla-performance-grid">
                  <div>
                    <span>Resolved Met</span>
                    <strong>{data.slaPerformance.met}</strong>
                  </div>
                  <div>
                    <span>Resolved Breached</span>
                    <strong>{data.slaPerformance.breached}</strong>
                  </div>
                  <div>
                    <span>Active At Risk</span>
                    <strong>{data.slaPerformance.activeAtRisk}</strong>
                  </div>
                  <div>
                    <span>Active Breached</span>
                    <strong>{data.slaPerformance.activeBreached}</strong>
                  </div>
                </div>
              </section>

              <section className="detail-card detail-card-wide table-card analytics-table-card">
                <div className="table-card-header">
                  <h2>Technician Performance</h2>
                </div>
                <div className="table-responsive">
                  <table className="incident-table">
                    <thead>
                      <tr>
                        <th>Technician</th>
                        <th>Active</th>
                        <th>Resolved</th>
                        <th>SLA Success</th>
                        <th>Avg Resolution Time</th>
                      </tr>
                    </thead>
                    <tbody>
                      {data.technicianPerformance.length === 0 ? (
                        <tr>
                          <td colSpan={5}>
                            No technician data is available for this period.
                          </td>
                        </tr>
                      ) : (
                        data.technicianPerformance.map((item) => (
                          <tr key={item.technicianId}>
                            <td>{item.technicianName}</td>
                            <td>{item.active}</td>
                            <td>{item.resolved}</td>
                            <td>{formatPercent(item.slaSuccessPercent)}</td>
                            <td>
                              {formatDuration(item.averageResolutionMinutes)}
                            </td>
                          </tr>
                        ))
                      )}
                    </tbody>
                  </table>
                </div>
              </section>

              {canViewOrganizationTables && (
                <section className="detail-card detail-card-wide table-card analytics-table-card">
                  <div className="table-card-header">
                    <h2>Department Performance</h2>
                  </div>
                  <div className="table-responsive">
                    <table className="incident-table">
                      <thead>
                        <tr>
                          <th>Department</th>
                          <th>Incidents</th>
                          <th>Active</th>
                          <th>Resolved</th>
                          <th>SLA Success</th>
                        </tr>
                      </thead>
                      <tbody>
                        {data.departmentPerformance.length === 0 ? (
                          <tr>
                            <td colSpan={5}>
                              No department data is available for this period.
                            </td>
                          </tr>
                        ) : (
                          data.departmentPerformance.map((item) => (
                            <tr key={item.departmentId ?? "none"}>
                              <td>{item.departmentName}</td>
                              <td>{item.incidents}</td>
                              <td>{item.active}</td>
                              <td>{item.resolved}</td>
                              <td>{formatPercent(item.slaSuccessPercent)}</td>
                            </tr>
                          ))
                        )}
                      </tbody>
                    </table>
                  </div>
                </section>
              )}
            </>
          )}
        </div>
      </main>
    </div>
  );
}
