import { useCallback, useEffect, useState } from "react";
import { Link, useNavigate, useSearchParams } from "react-router-dom";

import {
  getIncidentOptions,
  getIncidentsPaged,
  isUnauthorizedError,
} from "../api/api";
import type {
  CurrentUser,
  Incident,
  IncidentOptions,
  IncidentSlaCompact,
} from "../api/api";
import NewIncidentModal from "../components/NewIncidentModal";
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

export default function IncidentsPage() {
  const navigate = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();

  const [user] = useState<CurrentUser | null>(() => readCurrentUser());
  const isManagerOrAdmin = user?.role === "Manager" || user?.role === "Admin";

  // URL Query State
  const initialSearch = searchParams.get("search") || "";
  const initialStatus = searchParams.get("status") || "all";
  const initialPriority = searchParams.get("priority") || "all";
  const initialCategory = searchParams.get("category") || "all";
  const initialSlaStatus = searchParams.get("slaStatus") || "all";
  const initialAssignment = searchParams.get("assignment") || "all";
  const initialSortBy = searchParams.get("sortBy") || "updatedAt";
  const initialSortDir = (searchParams.get("sortDirection") as "asc" | "desc") || "desc";
  const initialPage = parseInt(searchParams.get("page") || "1", 10) || 1;
  const initialPageSize = parseInt(searchParams.get("pageSize") || "25", 10) || 25;

  const [search, setSearch] = useState(initialSearch);
  const [status, setStatus] = useState(initialStatus);
  const [priority, setPriority] = useState(initialPriority);
  const [category, setCategory] = useState(initialCategory);
  const [slaStatus, setSlaStatus] = useState(initialSlaStatus);
  const [assignment, setAssignment] = useState(initialAssignment);
  const [sortBy, setSortBy] = useState(initialSortBy);
  const [sortDir, setSortDir] = useState<"asc" | "desc">(initialSortDir);
  const [page, setPage] = useState(initialPage);
  const [pageSize, setPageSize] = useState(initialPageSize);

  // Data State
  const [incidents, setIncidents] = useState<Incident[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [totalPages, setTotalPages] = useState(1);
  const [options, setOptions] = useState<IncidentOptions | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [showNewIncident, setShowNewIncident] = useState(false);

  const handleUnauthorized = useCallback(() => {
    sessionStorage.clear();
    navigate("/", { replace: true });
  }, [navigate]);

  // Sync state to URL search params
  const updateUrlParams = useCallback(
    (newParams: Record<string, string | number>) => {
      const updated = new URLSearchParams(searchParams);
      for (const [k, v] of Object.entries(newParams)) {
        if (!v || v === "all" || (k === "page" && v === 1)) {
          updated.delete(k);
        } else {
          updated.set(k, String(v));
        }
      }
      setSearchParams(updated, { replace: true });
    },
    [searchParams, setSearchParams]
  );

  // Load options (categories, priorities)
  useEffect(() => {
    if (!user) {
      handleUnauthorized();
      return;
    }

    let active = true;
    getIncidentOptions()
      .then((opts) => {
        if (active) setOptions(opts);
      })
      .catch(() => {
        // Ignore fallback
      });

    return () => {
      active = false;
    };
  }, [handleUnauthorized, user]);

  // Load Incidents
  const fetchIncidents = useCallback(async () => {
    setLoading(true);
    setError("");

    try {
      const result = await getIncidentsPaged({
        search: search.trim() || undefined,
        status: status !== "all" ? status : undefined,
        priority: priority !== "all" ? priority : undefined,
        category: category !== "all" ? category : undefined,
        slaStatus: slaStatus !== "all" ? slaStatus : undefined,
        assignment: assignment !== "all" ? assignment : undefined,
        sortBy,
        sortDirection: sortDir,
        page,
        pageSize,
      });

      setIncidents(result.items);
      setTotalCount(result.totalCount);
      setTotalPages(result.totalPages);
    } catch (err) {
      if (isUnauthorizedError(err)) {
        handleUnauthorized();
        return;
      }
      setError(
        err instanceof Error ? err.message : "Unable to load incidents."
      );
    } finally {
      setLoading(false);
    }
  }, [
    assignment,
    category,
    handleUnauthorized,
    page,
    pageSize,
    priority,
    search,
    slaStatus,
    sortBy,
    sortDir,
    status,
  ]);

  useEffect(() => {
    if (!user) {
      handleUnauthorized();
      return;
    }
    void Promise.resolve().then(fetchIncidents);
  }, [fetchIncidents, handleUnauthorized, user]);

  function handleFilterChange(key: string, value: string) {
    setPage(1);
    if (key === "status") setStatus(value);
    if (key === "priority") setPriority(value);
    if (key === "category") setCategory(value);
    if (key === "slaStatus") setSlaStatus(value);
    if (key === "assignment") setAssignment(value);

    updateUrlParams({
      [key]: value,
      page: 1,
    });
  }

  function handleSortChange(value: string) {
    setPage(1);
    let newSortBy: string;
    let newSortDir: "asc" | "desc";

    if (value === "newest") {
      newSortBy = "createdAt";
      newSortDir = "desc";
    } else if (value === "oldest") {
      newSortBy = "createdAt";
      newSortDir = "asc";
    } else if (value === "priority") {
      newSortBy = "priority";
      newSortDir = "desc";
    } else if (value === "urgency") {
      newSortBy = "urgency";
      newSortDir = "desc";
    } else if (value === "number") {
      newSortBy = "incidentNumber";
      newSortDir = "asc";
    } else {
      newSortBy = "updatedAt";
      newSortDir = "desc";
    }

    setSortBy(newSortBy);
    setSortDir(newSortDir);
    updateUrlParams({
      sortBy: newSortBy,
      sortDirection: newSortDir,
      page: 1,
    });
  }

  function handleSearchSubmit(e: React.FormEvent) {
    e.preventDefault();
    setPage(1);
    updateUrlParams({ search, page: 1 });
  }

  function handleClearFilters() {
    setSearch("");
    setStatus("all");
    setPriority("all");
    setCategory("all");
    setSlaStatus("all");
    setAssignment("all");
    setSortBy("updatedAt");
    setSortDir("desc");
    setPage(1);
    setSearchParams(new URLSearchParams(), { replace: true });
  }

  const startItem = totalCount === 0 ? 0 : (page - 1) * pageSize + 1;
  const endItem = Math.min(page * pageSize, totalCount);

  return (
    <div className="app-shell">
      <Sidebar
        currentTab="incidents"
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
              <span className="eyebrow">Service Desk</span>
              <h1>Incidents</h1>
              <p>Search, filter and review incidents you have access to.</p>
            </div>

            <button
              type="button"
              id="new-incident-btn"
              className="new-incident-button"
              onClick={() => setShowNewIncident(true)}
            >
              <span aria-hidden="true">+</span>
              New Incident
            </button>
          </header>

          {/* Filter Toolbar */}
          <section className="filter-toolbar" aria-label="Incident filters">
            <form className="filter-search-form" onSubmit={handleSearchSubmit}>
              <input
                type="search"
                id="incident-search-input"
                className="filter-search-input"
                placeholder="Search incident #, title, reporter…"
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                aria-label="Search incidents"
              />
              <button
                type="submit"
                id="search-submit-btn"
                className="filter-btn secondary"
              >
                Search
              </button>
            </form>

            <div className="filter-group">
              <select
                id="filter-status-select"
                className="filter-select"
                value={status}
                onChange={(e) => handleFilterChange("status", e.target.value)}
                aria-label="Filter by status"
              >
                <option value="all">All Statuses</option>
                <option value="active">Active (Non-Resolved)</option>
                <option value="Open">Open</option>
                <option value="Triaged">Triaged</option>
                <option value="Assigned">Assigned</option>
                <option value="InProgress">In Progress</option>
                <option value="WaitingForUser">Waiting for User</option>
                <option value="Resolved">Resolved</option>
                <option value="Closed">Closed</option>
              </select>

              <select
                id="filter-priority-select"
                className="filter-select"
                value={priority}
                onChange={(e) => handleFilterChange("priority", e.target.value)}
                aria-label="Filter by priority"
              >
                <option value="all">All Priorities</option>
                <option value="Critical">Critical</option>
                <option value="High">High</option>
                <option value="Medium">Medium</option>
                <option value="Low">Low</option>
              </select>

              <select
                id="filter-category-select"
                className="filter-select"
                value={category}
                onChange={(e) => handleFilterChange("category", e.target.value)}
                aria-label="Filter by category"
              >
                <option value="all">All Categories</option>
                {options?.categories.map((c) => (
                  <option key={c.id} value={c.name}>
                    {c.name}
                  </option>
                ))}
              </select>

              <select
                id="filter-sla-select"
                className="filter-select"
                value={slaStatus}
                onChange={(e) => handleFilterChange("slaStatus", e.target.value)}
                aria-label="Filter by SLA status"
              >
                <option value="all">All SLA States</option>
                <option value="OnTrack">On Track</option>
                <option value="AtRisk">At Risk</option>
                <option value="Breached">Breached</option>
                <option value="Met">Met</option>
              </select>

              {isManagerOrAdmin && (
                <select
                  id="filter-assignment-select"
                  className="filter-select"
                  value={assignment}
                  onChange={(e) =>
                    handleFilterChange("assignment", e.target.value)
                  }
                  aria-label="Filter by assignment"
                >
                  <option value="all">All Assignments</option>
                  <option value="assigned">Assigned</option>
                  <option value="unassigned">Unassigned</option>
                </select>
              )}

              <select
                id="filter-sort-select"
                className="filter-select"
                value={
                  sortBy === "createdAt" && sortDir === "desc"
                    ? "newest"
                    : sortBy === "createdAt" && sortDir === "asc"
                      ? "oldest"
                      : sortBy === "priority"
                        ? "priority"
                        : sortBy === "urgency"
                          ? "urgency"
                          : sortBy === "incidentNumber"
                            ? "number"
                            : "updatedAt"
                }
                onChange={(e) => handleSortChange(e.target.value)}
                aria-label="Sort order"
              >
                <option value="updatedAt">Recently Updated</option>
                <option value="newest">Newest First</option>
                <option value="oldest">Oldest First</option>
                <option value="priority">Highest Priority</option>
                <option value="urgency">SLA Urgency</option>
                <option value="number">Incident #</option>
              </select>

              {(search ||
                status !== "all" ||
                priority !== "all" ||
                category !== "all" ||
                slaStatus !== "all" ||
                assignment !== "all") && (
                <button
                  type="button"
                  id="clear-filters-btn"
                  className="filter-btn reset"
                  onClick={handleClearFilters}
                >
                  Reset
                </button>
              )}
            </div>
          </section>

          {/* Incident Table Card */}
          <section className="detail-card detail-card-wide table-card">
            <div className="table-card-header">
              <h2>
                All Incidents{" "}
                <span className="count-pill">
                  {totalCount}
                </span>
              </h2>

              <div className="table-header-meta">
                Showing {startItem}–{endItem} of {totalCount}
              </div>
            </div>

            {error && (
              <div className="inline-notice error" role="alert">
                {error}
              </div>
            )}

            {loading ? (
              <div className="table-loading-state">
                <span className="spinner-indicator" aria-hidden="true" />
                Loading incidents…
              </div>
            ) : incidents.length === 0 ? (
              <div className="table-empty-state">
                <p>No incidents match the selected search or filter criteria.</p>
                <button
                  type="button"
                  className="filter-btn secondary"
                  onClick={handleClearFilters}
                >
                  Reset filters
                </button>
              </div>
            ) : (
              <div className="table-responsive">
                <table className="incident-table" aria-label="Incident directory">
                  <thead>
                    <tr>
                      <th scope="col">Incident</th>
                      <th scope="col">Title</th>
                      <th scope="col">Reporter</th>
                      <th scope="col">Category</th>
                      <th scope="col">Priority</th>
                      <th scope="col">Status</th>
                      <th scope="col">Assigned To</th>
                      <th scope="col">SLA</th>
                      <th scope="col">Created</th>
                      <th scope="col">Updated</th>
                    </tr>
                  </thead>
                  <tbody>
                    {incidents.map((incident) => {
                      const timing = formatSlaTiming(incident.sla);
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

                          <td>{incident.reporter?.name ?? "—"}</td>

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

                          <td>
                            {incident.assignedTo ? (
                              incident.assignedTo.name
                            ) : (
                              <span className="unassigned-chip">
                                Unassigned
                              </span>
                            )}
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

                          <td>{formatDate(incident.createdAt)}</td>

                          <td>{formatDate(incident.updatedAt)}</td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            )}

            {/* Pagination Controls */}
            {totalPages > 1 && (
              <div className="pagination-bar" aria-label="Pagination">
                <div className="pagination-info">
                  Page <strong>{page}</strong> of <strong>{totalPages}</strong> (
                  {totalCount} incidents)
                </div>

                <div className="pagination-actions">
                  <button
                    type="button"
                    id="pagination-prev"
                    className="page-nav-button"
                    disabled={page <= 1 || loading}
                    onClick={() => {
                      const newPage = page - 1;
                      setPage(newPage);
                      updateUrlParams({ page: newPage });
                    }}
                  >
                    ← Previous
                  </button>

                  <div className="page-numbers">
                    {Array.from({ length: totalPages }, (_, i) => i + 1)
                      .filter((p) => {
                        return (
                          p === 1 ||
                          p === totalPages ||
                          (p >= page - 2 && p <= page + 2)
                        );
                      })
                      .map((p, idx, arr) => {
                        const prev = arr[idx - 1];
                        return (
                          <span key={p} className="page-btn-wrapper">
                            {prev && p - prev > 1 && (
                              <span className="page-ellipsis">…</span>
                            )}
                            <button
                              type="button"
                              className={`page-number ${
                                p === page ? "active" : ""
                              }`}
                              disabled={p === page || loading}
                              onClick={() => {
                                setPage(p);
                                updateUrlParams({ page: p });
                              }}
                            >
                              {p}
                            </button>
                          </span>
                        );
                      })}
                  </div>

                  <button
                    type="button"
                    id="pagination-next"
                    className="page-nav-button"
                    disabled={page >= totalPages || loading}
                    onClick={() => {
                      const newPage = page + 1;
                      setPage(newPage);
                      updateUrlParams({ page: newPage });
                    }}
                  >
                    Next →
                  </button>

                  <select
                    id="page-size-select"
                    className="page-size-select"
                    value={pageSize}
                    onChange={(e) => {
                      const newSize = parseInt(e.target.value, 10);
                      setPageSize(newSize);
                      setPage(1);
                      updateUrlParams({ pageSize: newSize, page: 1 });
                    }}
                    aria-label="Items per page"
                  >
                    <option value="20">20 / page</option>
                    <option value="25">25 / page</option>
                    <option value="50">50 / page</option>
                  </select>
                </div>
              </div>
            )}
          </section>
        </div>
      </main>

      {showNewIncident && (
        <NewIncidentModal
          onClose={() => setShowNewIncident(false)}
          onCreated={async () => {
            setShowNewIncident(false);
            await fetchIncidents();
          }}
          onUnauthorized={handleUnauthorized}
        />
      )}
    </div>
  );
}
