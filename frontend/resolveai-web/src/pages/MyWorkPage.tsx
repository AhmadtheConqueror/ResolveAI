import { useCallback, useEffect, useMemo, useState } from "react";
import { Link, useNavigate, useSearchParams } from "react-router-dom";

import {
  assignIncident,
  getIncidentsPaged,
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
import { Skeleton, SkeletonTableRow } from "../components/Skeleton";

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

const VALID_TECH_TABS: TechnicianTab[] = [
  "all",
  "assigned",
  "inprogress",
  "waiting",
  "sla",
];

const VALID_MANAGER_TABS: ManagerTab[] = [
  "triage",
  "unassigned",
  "sla",
  "active",
  "resolved",
];

function parseSortCombo(combo: string): {
  sortBy: string;
  sortDirection: "asc" | "desc";
} {
  switch (combo) {
    case "createdat_desc":
      return { sortBy: "createdat", sortDirection: "desc" };
    case "createdat_asc":
      return { sortBy: "createdat", sortDirection: "asc" };
    case "priority_desc":
      return { sortBy: "priority", sortDirection: "desc" };
    case "priority_asc":
      return { sortBy: "priority", sortDirection: "asc" };
    case "updatedat_desc":
      return { sortBy: "updatedat", sortDirection: "desc" };
    case "urgency":
    default:
      return { sortBy: "urgency", sortDirection: "desc" };
  }
}

function getSortCombo(
  sortBy?: string | null,
  sortDirection?: string | null
): string {
  if (sortBy === "createdat" && sortDirection === "asc") return "createdat_asc";
  if (sortBy === "createdat") return "createdat_desc";
  if (sortBy === "priority" && sortDirection === "asc") return "priority_asc";
  if (sortBy === "priority") return "priority_desc";
  if (sortBy === "updatedat") return "updatedat_desc";
  return "urgency";
}

function getSortMetaLabel(combo: string): string {
  switch (combo) {
    case "createdat_desc":
      return "Sorted by Newest First";
    case "createdat_asc":
      return "Sorted by Oldest First";
    case "priority_desc":
      return "Sorted by Priority (Highest First)";
    case "priority_asc":
      return "Sorted by Priority (Lowest First)";
    case "updatedat_desc":
      return "Sorted by Recently Updated";
    case "urgency":
    default:
      return "Sorted by SLA Urgency";
  }
}

function getStatusOptionsForTab(
  isTechnician: boolean,
  activeTab: string
): Array<{ value: string; label: string }> | null {
  if (isTechnician) {
    if (activeTab === "all" || activeTab === "sla") {
      return [
        { value: "all", label: "All Active Statuses" },
        { value: "Assigned", label: "Assigned" },
        { value: "InProgress", label: "In Progress" },
        { value: "WaitingForUser", label: "Waiting for User" },
      ];
    }
    // Single-status technician tabs ("assigned", "inprogress", "waiting")
    return null;
  }

  // Manager / Admin operational tabs
  if (activeTab === "active" || activeTab === "sla") {
    return [
      { value: "all", label: "All Active Statuses" },
      { value: "Open", label: "Open" },
      { value: "Triaged", label: "Triaged" },
      { value: "Assigned", label: "Assigned" },
      { value: "InProgress", label: "In Progress" },
      { value: "WaitingForUser", label: "Waiting for User" },
    ];
  }

  if (activeTab === "unassigned") {
    return [
      { value: "all", label: "All Unassigned (Triaged & Assigned)" },
      { value: "Triaged", label: "Triaged" },
      { value: "Assigned", label: "Assigned" },
    ];
  }

  // "triage" (Open) and "resolved" (Resolved) are single-status tabs
  return null;
}

export default function MyWorkPage() {
  const navigate = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();
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

  // URL state initialization
  const urlTab = searchParams.get("tab");
  const initialTechTab: TechnicianTab =
    isTechnician && urlTab && (VALID_TECH_TABS as string[]).includes(urlTab)
      ? (urlTab as TechnicianTab)
      : "all";

  const initialManagerTab: ManagerTab =
    !isTechnician && urlTab && (VALID_MANAGER_TABS as string[]).includes(urlTab)
      ? (urlTab as ManagerTab)
      : "triage";

  // Active tab states
  const [managerTab, setManagerTab] = useState<ManagerTab>(initialManagerTab);
  const [techTab, setTechTab] = useState<TechnicianTab>(initialTechTab);

  // Search, sort, and filter states
  const initialSearch = searchParams.get("search") || "";
  const [searchInput, setSearchInput] = useState(initialSearch);
  const [search, setSearch] = useState(initialSearch);

  const initialSort = getSortCombo(
    searchParams.get("sortBy"),
    searchParams.get("sortDirection")
  );
  const [sortCombo, setSortCombo] = useState(initialSort);

  const initialPriority = searchParams.get("priority") || "all";
  const [priorityFilter, setPriorityFilter] = useState(initialPriority);

  const initialStatus = searchParams.get("status") || "all";
  const [statusFilter, setStatusFilter] = useState(initialStatus);

  const initialSla = searchParams.get("slaStatus") || "all";
  const [slaFilter, setSlaFilter] = useState(initialSla);

  // Pagination state
  const initialPage = parseInt(searchParams.get("page") || "1", 10) || 1;
  const initialPageSize =
    parseInt(searchParams.get("pageSize") || "25", 10) || 25;
  const [page, setPage] = useState(initialPage);
  const [pageSize, setPageSize] = useState(initialPageSize);
  const [totalCount, setTotalCount] = useState(0);
  const [totalPages, setTotalPages] = useState(1);

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

  // Sync state to URL search params
  const updateUrlParams = useCallback(
    (updates: {
      tab?: string;
      search?: string;
      sortCombo?: string;
      priority?: string;
      status?: string;
      slaStatus?: string;
      page?: number;
      pageSize?: number;
    }) => {
      const sp = new URLSearchParams();
      const currentTab = updates.tab ?? (isTechnician ? techTab : managerTab);
      const defaultTab = isTechnician ? "all" : "triage";
      if (currentTab && currentTab !== defaultTab) {
        sp.set("tab", currentTab);
      }

      const currentSearch =
        updates.search !== undefined ? updates.search : search;
      if (currentSearch.trim()) {
        sp.set("search", currentSearch.trim());
      }

      const currentSort =
        updates.sortCombo !== undefined ? updates.sortCombo : sortCombo;
      if (currentSort && currentSort !== "urgency") {
        const { sortBy, sortDirection } = parseSortCombo(currentSort);
        sp.set("sortBy", sortBy);
        sp.set("sortDirection", sortDirection);
      }

      const currentPriority =
        updates.priority !== undefined ? updates.priority : priorityFilter;
      if (currentPriority && currentPriority !== "all") {
        sp.set("priority", currentPriority);
      }

      const currentStatus =
        updates.status !== undefined ? updates.status : statusFilter;
      if (currentStatus && currentStatus !== "all") {
        sp.set("status", currentStatus);
      }

      const currentSla =
        updates.slaStatus !== undefined ? updates.slaStatus : slaFilter;
      if (currentSla && currentSla !== "all") {
        sp.set("slaStatus", currentSla);
      }

      const currentPage = updates.page !== undefined ? updates.page : page;
      if (currentPage && currentPage > 1) {
        sp.set("page", String(currentPage));
      }

      const currentPageSize =
        updates.pageSize !== undefined ? updates.pageSize : pageSize;
      if (currentPageSize && currentPageSize !== 25) {
        sp.set("pageSize", String(currentPageSize));
      }

      setSearchParams(sp, { replace: true });
    },
    [
      isTechnician,
      managerTab,
      page,
      pageSize,
      priorityFilter,
      search,
      setSearchParams,
      slaFilter,
      sortCombo,
      statusFilter,
      techTab,
    ]
  );

  // Status and tab helpers
  const currentTab = isTechnician ? techTab : managerTab;
  const statusOptions = useMemo(
    () => getStatusOptionsForTab(isTechnician, currentTab),
    [isTechnician, currentTab]
  );
  const isSlaTab = isTechnician ? techTab === "sla" : managerTab === "sla";

  const hasActiveFilters =
    search.trim() !== "" ||
    sortCombo !== "urgency" ||
    priorityFilter !== "all" ||
    statusFilter !== "all" ||
    slaFilter !== "all";

  // Tab selection handlers
  function handleSelectTechTab(tab: TechnicianTab) {
    setTechTab(tab);
    setStatusFilter("all");
    setPage(1);
    updateUrlParams({ tab, status: "all", page: 1 });
  }

  function handleSelectManagerTab(tab: ManagerTab) {
    setManagerTab(tab);
    setStatusFilter("all");
    setPage(1);
    updateUrlParams({ tab, status: "all", page: 1 });
  }

  // Filter toolbar handlers
  function handleSearchSubmit(e: React.FormEvent) {
    e.preventDefault();
    const trimmed = searchInput.trim();
    setSearch(trimmed);
    setPage(1);
    updateUrlParams({ search: trimmed, page: 1 });
  }

  function handleSortChange(value: string) {
    setSortCombo(value);
    setPage(1);
    updateUrlParams({ sortCombo: value, page: 1 });
  }

  function handlePriorityChange(value: string) {
    setPriorityFilter(value);
    setPage(1);
    updateUrlParams({ priority: value, page: 1 });
  }

  function handleStatusChange(value: string) {
    setStatusFilter(value);
    setPage(1);
    updateUrlParams({ status: value, page: 1 });
  }

  function handleSlaChange(value: string) {
    setSlaFilter(value);
    setPage(1);
    updateUrlParams({ slaStatus: value, page: 1 });
  }

  function handleClearFilters() {
    setSearchInput("");
    setSearch("");
    setSortCombo("urgency");
    setPriorityFilter("all");
    setStatusFilter("all");
    setSlaFilter("all");
    setPage(1);
    setPageSize(25);
    updateUrlParams({
      search: "",
      sortCombo: "urgency",
      priority: "all",
      status: "all",
      slaStatus: "all",
      page: 1,
      pageSize: 25,
    });
  }

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

  // Load Incidents for the selected tab and active filters
  const loadTabIncidents = useCallback(async () => {
    setLoading(true);
    setError("");

    try {
      const { sortBy, sortDirection } = parseSortCombo(sortCombo);
      let queryParams: Record<string, string | number> = {
        sortBy,
        sortDirection,
        page,
        pageSize,
      };

      if (search.trim()) {
        queryParams.search = search.trim();
      }

      if (priorityFilter !== "all") {
        queryParams.priority = priorityFilter;
      }

      if (isTechnician) {
        // Technician queue: incidents assigned to me
        queryParams = {
          ...queryParams,
          assignedToId: user?.id ?? "",
        };

        if (techTab === "assigned") {
          queryParams.status = "Assigned";
          if (slaFilter !== "all") queryParams.slaStatus = slaFilter;
        } else if (techTab === "inprogress") {
          queryParams.status = "InProgress";
          if (slaFilter !== "all") queryParams.slaStatus = slaFilter;
        } else if (techTab === "waiting") {
          queryParams.status = "WaitingForUser";
          if (slaFilter !== "all") queryParams.slaStatus = slaFilter;
        } else if (techTab === "sla") {
          queryParams.status = statusFilter !== "all" ? statusFilter : "active";
          queryParams.slaStatus = slaFilter !== "all" ? slaFilter : "Attention";
        } else {
          // "all" active assigned to me
          queryParams.status = statusFilter !== "all" ? statusFilter : "active";
          if (slaFilter !== "all") queryParams.slaStatus = slaFilter;
        }
      } else {
        // Manager / Admin operational queues
        if (managerTab === "triage") {
          queryParams.status = "Open";
          if (slaFilter !== "all") queryParams.slaStatus = slaFilter;
        } else if (managerTab === "unassigned") {
          queryParams.assignment = "unassigned";
          queryParams.status =
            statusFilter !== "all" ? statusFilter : "Triaged,Assigned";
          if (slaFilter !== "all") queryParams.slaStatus = slaFilter;
        } else if (managerTab === "sla") {
          queryParams.status = statusFilter !== "all" ? statusFilter : "active";
          queryParams.slaStatus = slaFilter !== "all" ? slaFilter : "Attention";
        } else if (managerTab === "resolved") {
          queryParams.status = "Resolved";
          if (slaFilter !== "all") queryParams.slaStatus = slaFilter;
        } else {
          // "active" (backend excludes Resolved and Closed)
          queryParams.status = statusFilter !== "all" ? statusFilter : "active";
          if (slaFilter !== "all") queryParams.slaStatus = slaFilter;
        }
      }

      const result = await getIncidentsPaged(queryParams);
      setIncidents(result.items);
      setTotalCount(result.totalCount);
      setTotalPages(result.totalPages);
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
    page,
    pageSize,
    priorityFilter,
    search,
    slaFilter,
    sortCombo,
    statusFilter,
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
          {(isManager || isAdmin) && (
            loading ? (
              <section className="detail-card detail-card-wide workload-card" aria-busy="true">
                <span className="sr-only">Loading technician workload...</span>
                <div className="workload-card-header">
                  <div>
                    <h2>Technician Workload</h2>
                    <p className="subtext">
                      Active operational capacity across the support engineering team.
                    </p>
                  </div>
                </div>

                <div className="workload-grid">
                  {Array.from({ length: 3 }).map((_, i) => (
                    <div key={i} className="workload-item" aria-hidden="true">
                      <div className="workload-tech-info">
                        <Skeleton width="110px" height={16} style={{ marginBottom: 6 }} />
                        <Skeleton width="150px" height={12} />
                      </div>

                      <div className="workload-metrics">
                        <div className="workload-total-pill">
                          <Skeleton variant="pill" width={28} height={18} />
                        </div>

                        <div className="workload-breakdown">
                          <Skeleton width="140px" height={12} />
                        </div>
                      </div>
                    </div>
                  ))}
                </div>
              </section>
            ) : workload.length > 0 ? (
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
            ) : null
          )}

          {/* Queue Tab Navigation */}
          <div className="queue-tabs-wrapper">
            {isTechnician ? (
              <nav className="queue-tabs" aria-label="Technician queues">
                <button
                  type="button"
                  id="tab-all-assigned"
                  className={`queue-tab-btn ${techTab === "all" ? "active" : ""}`}
                  onClick={() => handleSelectTechTab("all")}
                >
                  All Assigned
                  <span className="queue-tab-badge">
                    {loading ? (
                      <Skeleton variant="pill" width={18} height={14} />
                    ) : (
                      queueSummary?.allAssignedCount ?? 0
                    )}
                  </span>
                </button>

                <button
                  type="button"
                  id="tab-assigned"
                  className={`queue-tab-btn ${
                    techTab === "assigned" ? "active" : ""
                  }`}
                  onClick={() => handleSelectTechTab("assigned")}
                >
                  Assigned
                  <span className="queue-tab-badge">
                    {loading ? (
                      <Skeleton variant="pill" width={18} height={14} />
                    ) : (
                      queueSummary?.assignedCount ?? 0
                    )}
                  </span>
                </button>

                <button
                  type="button"
                  id="tab-in-progress"
                  className={`queue-tab-btn ${
                    techTab === "inprogress" ? "active" : ""
                  }`}
                  onClick={() => handleSelectTechTab("inprogress")}
                >
                  In Progress
                  <span className="queue-tab-badge">
                    {loading ? (
                      <Skeleton variant="pill" width={18} height={14} />
                    ) : (
                      queueSummary?.inProgressCount ?? 0
                    )}
                  </span>
                </button>

                <button
                  type="button"
                  id="tab-waiting"
                  className={`queue-tab-btn ${
                    techTab === "waiting" ? "active" : ""
                  }`}
                  onClick={() => handleSelectTechTab("waiting")}
                >
                  Waiting for User
                  <span className="queue-tab-badge">
                    {loading ? (
                      <Skeleton variant="pill" width={18} height={14} />
                    ) : (
                      queueSummary?.waitingForUserCount ?? 0
                    )}
                  </span>
                </button>

                <button
                  type="button"
                  id="tab-sla-attention"
                  className={`queue-tab-btn alert-tab ${
                    techTab === "sla" ? "active" : ""
                  }`}
                  onClick={() => handleSelectTechTab("sla")}
                >
                  SLA Attention
                  <span className="queue-tab-badge alert">
                    {loading ? (
                      <Skeleton variant="pill" width={18} height={14} />
                    ) : (
                      queueSummary?.slaAttentionCount ?? 0
                    )}
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
                  onClick={() => handleSelectManagerTab("triage")}
                >
                  Triage
                  <span className="queue-tab-badge highlight">
                    {loading ? (
                      <Skeleton variant="pill" width={18} height={14} />
                    ) : (
                      queueSummary?.triageCount ?? 0
                    )}
                  </span>
                </button>

                <button
                  type="button"
                  id="tab-unassigned"
                  className={`queue-tab-btn ${
                    managerTab === "unassigned" ? "active" : ""
                  }`}
                  onClick={() => handleSelectManagerTab("unassigned")}
                >
                  Unassigned
                  <span className="queue-tab-badge highlight">
                    {loading ? (
                      <Skeleton variant="pill" width={18} height={14} />
                    ) : (
                      queueSummary?.unassignedCount ?? 0
                    )}
                  </span>
                </button>

                <button
                  type="button"
                  id="tab-sla-attention"
                  className={`queue-tab-btn alert-tab ${
                    managerTab === "sla" ? "active" : ""
                  }`}
                  onClick={() => handleSelectManagerTab("sla")}
                >
                  SLA Attention
                  <span className="queue-tab-badge alert">
                    {loading ? (
                      <Skeleton variant="pill" width={18} height={14} />
                    ) : (
                      queueSummary?.slaAttentionCount ?? 0
                    )}
                  </span>
                </button>

                <button
                  type="button"
                  id="tab-active"
                  className={`queue-tab-btn ${
                    managerTab === "active" ? "active" : ""
                  }`}
                  onClick={() => handleSelectManagerTab("active")}
                >
                  Active
                  <span className="queue-tab-badge">
                    {loading ? (
                      <Skeleton variant="pill" width={18} height={14} />
                    ) : (
                      queueSummary?.activeCount ?? 0
                    )}
                  </span>
                </button>

                <button
                  type="button"
                  id="tab-resolved"
                  className={`queue-tab-btn ${
                    managerTab === "resolved" ? "active" : ""
                  }`}
                  onClick={() => handleSelectManagerTab("resolved")}
                >
                  Resolved / Awaiting Closure
                  <span className="queue-tab-badge">
                    {loading ? (
                      <Skeleton variant="pill" width={18} height={14} />
                    ) : (
                      queueSummary?.resolvedCount ?? 0
                    )}
                  </span>
                </button>
              </nav>
            )}
          </div>

          {/* Section 13A: My Work Search, Filters & Sorting Toolbar */}
          <section className="filter-toolbar" aria-label="My Work queue filters">
            <form className="filter-search-form" onSubmit={handleSearchSubmit}>
              <input
                type="search"
                id="my-work-search-input"
                className="filter-search-input"
                placeholder="Search incident #, title, reporter…"
                value={searchInput}
                onChange={(e) => setSearchInput(e.target.value)}
                aria-label="Search incidents in this queue"
              />
              <button
                type="submit"
                id="my-work-search-submit-btn"
                className="filter-btn secondary"
              >
                Search
              </button>
            </form>

            <div className="filter-group">
              {/* Sort Dropdown */}
              <select
                id="my-work-sort-select"
                className="filter-select"
                value={sortCombo}
                onChange={(e) => handleSortChange(e.target.value)}
                aria-label="Sort queue incidents"
              >
                <option value="urgency">Sort: SLA Urgency (Default)</option>
                <option value="createdat_desc">Sort: Newest First</option>
                <option value="createdat_asc">Sort: Oldest First</option>
                <option value="priority_desc">Sort: Priority (Highest)</option>
                <option value="priority_asc">Sort: Priority (Lowest)</option>
                <option value="updatedat_desc">Sort: Recently Updated</option>
              </select>

              {/* Priority Filter */}
              <select
                id="my-work-priority-select"
                className="filter-select"
                value={priorityFilter}
                onChange={(e) => handlePriorityChange(e.target.value)}
                aria-label="Filter by priority"
              >
                <option value="all">All Priorities</option>
                <option value="Critical">Critical</option>
                <option value="High">High</option>
                <option value="Medium">Medium</option>
                <option value="Low">Low</option>
              </select>

              {/* Context-Aware Status Filter */}
              {statusOptions && statusOptions.length > 0 && (
                <select
                  id="my-work-status-select"
                  className="filter-select"
                  value={statusFilter}
                  onChange={(e) => handleStatusChange(e.target.value)}
                  aria-label="Filter by status"
                >
                  {statusOptions.map((opt) => (
                    <option key={opt.value} value={opt.value}>
                      {opt.label}
                    </option>
                  ))}
                </select>
              )}

              {/* Context-Aware SLA Filter */}
              <select
                id="my-work-sla-select"
                className="filter-select"
                value={slaFilter}
                onChange={(e) => handleSlaChange(e.target.value)}
                aria-label="Filter by SLA status"
              >
                {isSlaTab ? (
                  <>
                    <option value="all">All SLA Attention</option>
                    <option value="Breached">Breached</option>
                    <option value="AtRisk">At Risk</option>
                  </>
                ) : (
                  <>
                    <option value="all">All SLA States</option>
                    <option value="OnTrack">On Track</option>
                    <option value="AtRisk">At Risk</option>
                    <option value="Breached">Breached</option>
                  </>
                )}
              </select>

              {/* Clear Filters button */}
              {hasActiveFilters && (
                <button
                  type="button"
                  id="my-work-clear-filters-btn"
                  className="filter-btn reset"
                  onClick={handleClearFilters}
                >
                  Clear Filters
                </button>
              )}
            </div>
          </section>

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
                <span className="count-pill">
                  {loading ? (
                    <Skeleton variant="pill" width={22} height={16} />
                  ) : (
                    totalCount
                  )}
                </span>
              </h2>

              <span className="table-header-meta">
                {getSortMetaLabel(sortCombo)}
              </span>
            </div>

            {error && (
              <div className="inline-notice error" role="alert">
                {error}
              </div>
            )}

            {loading ? (
              <div className="table-responsive" aria-busy="true">
                <span className="sr-only">Loading queue items…</span>
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
                    {Array.from({ length: 8 }).map((_, idx) => (
                      <SkeletonTableRow
                        key={idx}
                        columns={[
                          { width: "90px" },
                          { width: "85%" },
                          { width: "70px", variant: "badge", height: 24 },
                          { width: "75px", variant: "badge", height: 24 },
                          { width: "70px", variant: "badge", height: 24 },
                          { width: "95px" },
                          { width: "90px" },
                          { width: "75px" },
                          ...((managerTab === "triage" || managerTab === "unassigned")
                            ? [{ width: "85px", variant: "button" as const, height: 32 }]
                            : []),
                        ]}
                      />
                    ))}
                  </tbody>
                </table>
              </div>
            ) : incidents.length === 0 ? (
              <div className="table-empty-state">
                {hasActiveFilters ? (
                  <>
                    <p>No incidents match the selected filters in this queue.</p>
                    <button
                      type="button"
                      id="my-work-empty-clear-btn"
                      className="filter-btn secondary"
                      onClick={handleClearFilters}
                    >
                      Clear Filters
                    </button>
                  </>
                ) : (
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
                )}
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

            {/* Pagination Controls */}
            {totalCount > 0 && (
              <div className="pagination-bar" aria-label="Pagination">
                <div className="pagination-info">
                  Page <strong>{page}</strong> of <strong>{Math.max(1, totalPages)}</strong> (
                  {totalCount} {totalCount === 1 ? "incident" : "incidents"})
                </div>

                <div className="pagination-actions">
                  <button
                    type="button"
                    id="my-work-pagination-prev"
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
                    {Array.from({ length: Math.max(1, totalPages) }, (_, i) => i + 1)
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
                    id="my-work-pagination-next"
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
                    id="my-work-page-size-select"
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
                    <option value="25">25 / page</option>
                    <option value="50">50 / page</option>
                  </select>
                </div>
              </div>
            )}
          </section>
        </div>
      </main>
    </div>
  );
}
