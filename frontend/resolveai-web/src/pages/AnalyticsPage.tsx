import { useCallback, useEffect, useMemo, useRef, useState } from "react";
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
import {
  Skeleton,
  SkeletonKpiCard,
  SkeletonTableRow,
} from "../components/Skeleton";

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

function formatTooltipDate(value: string) {
  return new Intl.DateTimeFormat("en", {
    month: "short",
    day: "numeric",
    year: "numeric",
  }).format(new Date(value));
}

function formatAriaDate(value: string) {
  return new Intl.DateTimeFormat("en", {
    month: "long",
    day: "numeric",
    year: "numeric",
  }).format(new Date(value));
}

function formatIncidentNoun(count: number) {
  return count === 1 ? "incident" : "incidents";
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
  const trendChartRef = useRef<HTMLDivElement | null>(null);
  const [user] = useState<CurrentUser | null>(() => readCurrentUser());
  const [range, setRange] = useState<AnalyticsRange>("30");
  const [data, setData] = useState<AnalyticsOverview | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [trendTooltip, setTrendTooltip] = useState<{
    date: string;
    count: number;
    left: number;
    top: number;
    width: number;
  } | null>(null);

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

    setTrendTooltip(null);
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

  const hideTrendTooltip = useCallback(() => {
    setTrendTooltip(null);
  }, []);

  const showTrendTooltip = useCallback(
    (
      point: AnalyticsOverview["incidentTrend"][number],
      target: HTMLElement
    ) => {
      const chart = trendChartRef.current;
      if (!chart) return;

      const chartRect = chart.getBoundingClientRect();
      const targetRect = target.getBoundingClientRect();
      const tooltipWidth = Math.min(180, Math.max(136, chartRect.width - 16));
      const halfTooltipWidth = tooltipWidth / 2;
      const center = targetRect.left - chartRect.left + targetRect.width / 2;
      const minLeft = halfTooltipWidth + 8;
      const maxLeft = chartRect.width - halfTooltipWidth - 8;
      const left =
        maxLeft < minLeft
          ? chartRect.width / 2
          : Math.min(Math.max(center, minLeft), maxLeft);
      const top = Math.max(8, targetRect.top - chartRect.top - 64);

      setTrendTooltip({
        date: point.date,
        count: point.count,
        left,
        top,
        width: tooltipWidth,
      });
    },
    []
  );

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
            <div className="analytics-skeleton-container" aria-busy="true">
              <span className="sr-only">Loading analytics insights...</span>

              {/* KPI Grid Placeholder */}
              <section className="kpi-grid analytics-kpi-grid">
                <SkeletonKpiCard />
                <SkeletonKpiCard />
                <SkeletonKpiCard />
                <SkeletonKpiCard />
              </section>

              {/* Charts Grid 1 */}
              <section className="analytics-grid two-column">
                <div className="detail-card analytics-card">
                  <div className="analytics-card-header">
                    <Skeleton width={180} height={20} />
                  </div>
                  <div className="skeleton-chart-bars" aria-hidden="true">
                    {[45, 80, 60, 95, 30, 75, 55, 90, 40, 70, 85, 65].map((h, i) => (
                      <div key={i} className="skeleton-chart-col">
                        <Skeleton
                          width="100%"
                          height={`${h}%`}
                          borderRadius="4px 4px 0 0"
                        />
                        <Skeleton width="18px" height={10} />
                      </div>
                    ))}
                  </div>
                </div>

                <div className="detail-card analytics-card">
                  <div className="analytics-card-header">
                    <Skeleton width={160} height={20} />
                  </div>
                  <div className="analytics-count-list">
                    {Array.from({ length: 5 }).map((_, i) => (
                      <div key={i} className="analytics-count-row">
                        <div className="analytics-count-label">
                          <Skeleton width={90} height={14} />
                          <Skeleton width={24} height={14} />
                        </div>
                        <div className="analytics-count-track" aria-hidden="true">
                          <Skeleton width={`${75 - i * 12}%`} height="100%" />
                        </div>
                      </div>
                    ))}
                  </div>
                </div>
              </section>

              {/* Charts Grid 2 */}
              <section className="analytics-grid two-column">
                <div className="detail-card analytics-card">
                  <div className="analytics-card-header">
                    <Skeleton width={170} height={20} />
                  </div>
                  <div className="analytics-count-list">
                    {Array.from({ length: 4 }).map((_, i) => (
                      <div key={i} className="analytics-count-row">
                        <div className="analytics-count-label">
                          <Skeleton width={80} height={14} />
                          <Skeleton width={24} height={14} />
                        </div>
                        <div className="analytics-count-track" aria-hidden="true">
                          <Skeleton width={`${80 - i * 16}%`} height="100%" />
                        </div>
                      </div>
                    ))}
                  </div>
                </div>

                <div className="detail-card analytics-card">
                  <div className="analytics-card-header">
                    <Skeleton width={170} height={20} />
                  </div>
                  <div className="analytics-count-list">
                    {Array.from({ length: 4 }).map((_, i) => (
                      <div key={i} className="analytics-count-row">
                        <div className="analytics-count-label">
                          <Skeleton width={95} height={14} />
                          <Skeleton width={24} height={14} />
                        </div>
                        <div className="analytics-count-track" aria-hidden="true">
                          <Skeleton width={`${70 - i * 14}%`} height="100%" />
                        </div>
                      </div>
                    ))}
                  </div>
                </div>
              </section>

              {/* SLA Performance Placeholder */}
              <section className="detail-card analytics-card">
                <div className="analytics-card-header">
                  <Skeleton width={150} height={20} />
                </div>
                <div className="sla-performance-grid">
                  {Array.from({ length: 4 }).map((_, i) => (
                    <div key={i}>
                      <Skeleton width={110} height={12} style={{ marginBottom: 8 }} />
                      <Skeleton width={60} height={30} />
                    </div>
                  ))}
                </div>
              </section>

              {/* Technician Performance Table Placeholder */}
              <section className="detail-card detail-card-wide table-card analytics-table-card">
                <div className="table-card-header">
                  <Skeleton width={200} height={20} />
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
                      {Array.from({ length: 4 }).map((_, i) => (
                        <SkeletonTableRow
                          key={i}
                          columns={[
                            { width: "130px" },
                            { width: "30px" },
                            { width: "30px" },
                            { width: "50px" },
                            { width: "70px" },
                          ]}
                        />
                      ))}
                    </tbody>
                  </table>
                </div>
              </section>

              {/* Department Performance Table Placeholder */}
              {canViewOrganizationTables && (
                <section className="detail-card detail-card-wide table-card analytics-table-card">
                  <div className="table-card-header">
                    <Skeleton width={200} height={20} />
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
                        {Array.from({ length: 3 }).map((_, i) => (
                          <SkeletonTableRow
                            key={i}
                            columns={[
                              { width: "120px" },
                              { width: "35px" },
                              { width: "30px" },
                              { width: "30px" },
                              { width: "50px" },
                            ]}
                          />
                        ))}
                      </tbody>
                    </table>
                  </div>
                </section>
              )}
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
                    <div
                      className="trend-chart"
                      ref={trendChartRef}
                      onMouseLeave={hideTrendTooltip}
                    >
                      {data.incidentTrend.map((point) => {
                        const isActive = trendTooltip?.date === point.date;
                        const ariaLabel = `${formatAriaDate(point.date)}: ${
                          point.count
                        } ${formatIncidentNoun(point.count)}`;

                        return (
                        <div className="trend-bar-wrap" key={point.date}>
                          <button
                            type="button"
                            className={`trend-bar ${isActive ? "active" : ""}`}
                            style={{
                              height: `${Math.max(
                                4,
                                (point.count / trendMax) * 100
                              )}%`,
                            }}
                            aria-label={ariaLabel}
                            aria-describedby={
                              isActive ? "trend-tooltip" : undefined
                            }
                            onMouseEnter={(event) =>
                              showTrendTooltip(point, event.currentTarget)
                            }
                            onFocus={(event) =>
                              showTrendTooltip(point, event.currentTarget)
                            }
                            onBlur={hideTrendTooltip}
                            onClick={(event) =>
                              showTrendTooltip(point, event.currentTarget)
                            }
                          />
                          <small>{formatDateLabel(point.date)}</small>
                        </div>
                        );
                      })}

                      {trendTooltip && (
                        <div
                          id="trend-tooltip"
                          role="tooltip"
                          className="trend-tooltip"
                          style={{
                            left: `${trendTooltip.left}px`,
                            top: `${trendTooltip.top}px`,
                            width: `${trendTooltip.width}px`,
                          }}
                        >
                          <div>
                            <span>Date</span>
                            <strong>{formatTooltipDate(trendTooltip.date)}</strong>
                          </div>
                          <div>
                            <span>Incidents</span>
                            <strong>{trendTooltip.count}</strong>
                          </div>
                        </div>
                      )}
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
