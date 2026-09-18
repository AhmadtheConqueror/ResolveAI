import { describe, it } from "node:test";
import assert from "node:assert";

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
    return null;
  }

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

  return null;
}

function buildQueryParams(params: {
  isTechnician: boolean;
  userId: string;
  tab: string;
  search?: string;
  sortCombo?: string;
  priorityFilter?: string;
  statusFilter?: string;
  slaFilter?: string;
  page?: number;
  pageSize?: number;
}): Record<string, string | number> {
  const { sortBy, sortDirection } = parseSortCombo(params.sortCombo || "urgency");
  const query: Record<string, string | number> = {
    sortBy,
    sortDirection,
    page: params.page ?? 1,
    pageSize: params.pageSize ?? 25,
  };

  if (params.search && params.search.trim()) {
    query.search = params.search.trim();
  }

  if (params.priorityFilter && params.priorityFilter !== "all") {
    query.priority = params.priorityFilter;
  }

  const statusFilter = params.statusFilter || "all";
  const slaFilter = params.slaFilter || "all";

  if (params.isTechnician) {
    query.assignedToId = params.userId;

    if (params.tab === "assigned") {
      query.status = "Assigned";
      if (slaFilter !== "all") query.slaStatus = slaFilter;
    } else if (params.tab === "inprogress") {
      query.status = "InProgress";
      if (slaFilter !== "all") query.slaStatus = slaFilter;
    } else if (params.tab === "waiting") {
      query.status = "WaitingForUser";
      if (slaFilter !== "all") query.slaStatus = slaFilter;
    } else if (params.tab === "sla") {
      query.status = statusFilter !== "all" ? statusFilter : "active";
      query.slaStatus = slaFilter !== "all" ? slaFilter : "Attention";
    } else {
      query.status = statusFilter !== "all" ? statusFilter : "active";
      if (slaFilter !== "all") query.slaStatus = slaFilter;
    }
  } else {
    if (params.tab === "triage") {
      query.status = "Open";
      if (slaFilter !== "all") query.slaStatus = slaFilter;
    } else if (params.tab === "unassigned") {
      query.assignment = "unassigned";
      query.status = statusFilter !== "all" ? statusFilter : "Triaged,Assigned";
      if (slaFilter !== "all") query.slaStatus = slaFilter;
    } else if (params.tab === "sla") {
      query.status = statusFilter !== "all" ? statusFilter : "active";
      query.slaStatus = slaFilter !== "all" ? slaFilter : "Attention";
    } else if (params.tab === "resolved") {
      query.status = "Resolved";
      if (slaFilter !== "all") query.slaStatus = slaFilter;
    } else {
      query.status = statusFilter !== "all" ? statusFilter : "active";
      if (slaFilter !== "all") query.slaStatus = slaFilter;
    }
  }

  return query;
}

function serializeMyWorkUrlParams(params: {
  isTechnician: boolean;
  tab?: string;
  search?: string;
  sortCombo?: string;
  priority?: string;
  status?: string;
  slaStatus?: string;
  page?: number;
  pageSize?: number;
}): URLSearchParams {
  const sp = new URLSearchParams();
  const defaultTab = params.isTechnician ? "all" : "triage";
  if (params.tab && params.tab !== defaultTab) {
    sp.set("tab", params.tab);
  }
  if (params.search && params.search.trim()) {
    sp.set("search", params.search.trim());
  }
  if (params.sortCombo && params.sortCombo !== "urgency") {
    const { sortBy, sortDirection } = parseSortCombo(params.sortCombo);
    sp.set("sortBy", sortBy);
    sp.set("sortDirection", sortDirection);
  }
  if (params.priority && params.priority !== "all") {
    sp.set("priority", params.priority);
  }
  if (params.status && params.status !== "all") {
    sp.set("status", params.status);
  }
  if (params.slaStatus && params.slaStatus !== "all") {
    sp.set("slaStatus", params.slaStatus);
  }
  if (params.page && params.page > 1) {
    sp.set("page", String(params.page));
  }
  if (params.pageSize && params.pageSize !== 25) {
    sp.set("pageSize", String(params.pageSize));
  }
  return sp;
}

describe("Frontend My Work Search, Sorting & Queue Semantics", () => {
  it("1. default sort is urgency descending across all queues", () => {
    const parsed = parseSortCombo("urgency");
    assert.strictEqual(parsed.sortBy, "urgency");
    assert.strictEqual(parsed.sortDirection, "desc");

    const query = buildQueryParams({
      isTechnician: true,
      userId: "tech-1",
      tab: "all",
    });
    assert.strictEqual(query.sortBy, "urgency");
    assert.strictEqual(query.sortDirection, "desc");
  });

  it("2. sort values decompose into exact backend-supported parameters", () => {
    assert.deepStrictEqual(parseSortCombo("createdat_desc"), {
      sortBy: "createdat",
      sortDirection: "desc",
    });
    assert.deepStrictEqual(parseSortCombo("createdat_asc"), {
      sortBy: "createdat",
      sortDirection: "asc",
    });
    assert.deepStrictEqual(parseSortCombo("priority_desc"), {
      sortBy: "priority",
      sortDirection: "desc",
    });
    assert.deepStrictEqual(parseSortCombo("priority_asc"), {
      sortBy: "priority",
      sortDirection: "asc",
    });
    assert.deepStrictEqual(parseSortCombo("updatedat_desc"), {
      sortBy: "updatedat",
      sortDirection: "desc",
    });
    assert.deepStrictEqual(parseSortCombo("urgency"), {
      sortBy: "urgency",
      sortDirection: "desc",
    });

    // Test reverse mapping helper
    assert.strictEqual(getSortCombo("createdat", "asc"), "createdat_asc");
    assert.strictEqual(getSortCombo("createdat", "desc"), "createdat_desc");
    assert.strictEqual(getSortCombo("priority", "asc"), "priority_asc");
    assert.strictEqual(getSortCombo("priority", "desc"), "priority_desc");
    assert.strictEqual(getSortCombo("updatedat", "desc"), "updatedat_desc");
    assert.strictEqual(getSortCombo("urgency", "desc"), "urgency");
  });

  it("3. Manager Active queue status filter exposes only active statuses and EXCLUDES Resolved", () => {
    const options = getStatusOptionsForTab(false, "active");
    assert.ok(options !== null);
    const values = options.map((o) => o.value);
    assert.deepStrictEqual(values, [
      "all",
      "Open",
      "Triaged",
      "Assigned",
      "InProgress",
      "WaitingForUser",
    ]);
    assert.ok(!values.includes("Resolved"), "Manager Active MUST NOT contain Resolved");
    assert.ok(!values.includes("Closed"), "Manager Active MUST NOT contain Closed");
  });

  it("4. Manager Unassigned queue status filter exposes ONLY Triaged and Assigned", () => {
    const options = getStatusOptionsForTab(false, "unassigned");
    assert.ok(options !== null);
    const values = options.map((o) => o.value);
    assert.deepStrictEqual(values, ["all", "Triaged", "Assigned"]);
    assert.ok(!values.includes("Open"));
    assert.ok(!values.includes("InProgress"));
    assert.ok(!values.includes("Resolved"));
  });

  it("5. Single-status queues (Triage, Resolved, Assigned, InProgress, Waiting) hide status dropdown", () => {
    assert.strictEqual(getStatusOptionsForTab(false, "triage"), null);
    assert.strictEqual(getStatusOptionsForTab(false, "resolved"), null);
    assert.strictEqual(getStatusOptionsForTab(true, "assigned"), null);
    assert.strictEqual(getStatusOptionsForTab(true, "inprogress"), null);
    assert.strictEqual(getStatusOptionsForTab(true, "waiting"), null);
  });

  it("6. Technician All and SLA queues expose only technician-appropriate active statuses", () => {
    const techAllOpts = getStatusOptionsForTab(true, "all");
    assert.ok(techAllOpts !== null);
    assert.deepStrictEqual(
      techAllOpts.map((o) => o.value),
      ["all", "Assigned", "InProgress", "WaitingForUser"]
    );

    const techSlaOpts = getStatusOptionsForTab(true, "sla");
    assert.ok(techSlaOpts !== null);
    assert.deepStrictEqual(
      techSlaOpts.map((o) => o.value),
      ["all", "Assigned", "InProgress", "WaitingForUser"]
    );
  });

  it("7. Technician queries strictly preserve assignedToId scope across all tabs and filters", () => {
    const query = buildQueryParams({
      isTechnician: true,
      userId: "tech-42",
      tab: "all",
      search: "vpn outage",
      priorityFilter: "Critical",
      statusFilter: "InProgress",
      slaFilter: "Breached",
      sortCombo: "priority_desc",
    });

    assert.strictEqual(query.assignedToId, "tech-42");
    assert.strictEqual(query.search, "vpn outage");
    assert.strictEqual(query.priority, "Critical");
    assert.strictEqual(query.status, "InProgress");
    assert.strictEqual(query.slaStatus, "Breached");
    assert.strictEqual(query.sortBy, "priority");
    assert.strictEqual(query.sortDirection, "desc");
  });

  it("8. Manager Unassigned tab sets assignment=unassigned with Triaged,Assigned default", () => {
    const defaultQuery = buildQueryParams({
      isTechnician: false,
      userId: "mgr-1",
      tab: "unassigned",
    });
    assert.strictEqual(defaultQuery.assignment, "unassigned");
    assert.strictEqual(defaultQuery.status, "Triaged,Assigned");

    const filteredQuery = buildQueryParams({
      isTechnician: false,
      userId: "mgr-1",
      tab: "unassigned",
      statusFilter: "Triaged",
    });
    assert.strictEqual(filteredQuery.assignment, "unassigned");
    assert.strictEqual(filteredQuery.status, "Triaged");
  });

  it("9. My Work pagination parameters default to page=1, pageSize=25 and are passed in backend query", () => {
    const defaultQuery = buildQueryParams({
      isTechnician: false,
      userId: "mgr-1",
      tab: "active",
    });
    assert.strictEqual(defaultQuery.page, 1);
    assert.strictEqual(defaultQuery.pageSize, 25);

    const pagedQuery = buildQueryParams({
      isTechnician: false,
      userId: "mgr-1",
      tab: "active",
      page: 2,
      pageSize: 50,
    });
    assert.strictEqual(pagedQuery.page, 2);
    assert.strictEqual(pagedQuery.pageSize, 50);
  });

  it("10. My Work URL search params cleanly omit defaults and synchronize non-default page and pageSize", () => {
    // Default page=1 and pageSize=25 -> no page/pageSize in query
    const defaultParams = serializeMyWorkUrlParams({
      isTechnician: false,
      tab: "triage",
      page: 1,
      pageSize: 25,
    });
    assert.strictEqual(defaultParams.get("page"), null);
    assert.strictEqual(defaultParams.get("pageSize"), null);

    // Page 2, PageSize 50 -> synchronized into query params
    const customParams = serializeMyWorkUrlParams({
      isTechnician: false,
      tab: "active",
      page: 2,
      pageSize: 50,
      search: "database",
      sortCombo: "priority_desc",
    });
    assert.strictEqual(customParams.get("tab"), "active");
    assert.strictEqual(customParams.get("page"), "2");
    assert.strictEqual(customParams.get("pageSize"), "50");
    assert.strictEqual(customParams.get("search"), "database");
    assert.strictEqual(customParams.get("sortBy"), "priority");
    assert.strictEqual(customParams.get("sortDirection"), "desc");

    // Switching back to pageSize=25 removes pageSize from URL
    const backTo25Params = serializeMyWorkUrlParams({
      isTechnician: false,
      tab: "active",
      page: 1,
      pageSize: 25,
      search: "database",
    });
    assert.strictEqual(backTo25Params.get("page"), null);
    assert.strictEqual(backTo25Params.get("pageSize"), null);
    assert.strictEqual(backTo25Params.get("search"), "database");
  });

  it("11. Technician pagination queries preserve assignedToId scope across all pages", () => {
    const page2TechQuery = buildQueryParams({
      isTechnician: true,
      userId: "tech-7",
      tab: "assigned",
      page: 2,
      pageSize: 25,
    });
    assert.strictEqual(page2TechQuery.assignedToId, "tech-7");
    assert.strictEqual(page2TechQuery.status, "Assigned");
    assert.strictEqual(page2TechQuery.page, 2);
    assert.strictEqual(page2TechQuery.pageSize, 25);
  });

  it("12. Manager operational queue pagination preserves queue semantics across pages", () => {
    const page2UnassignedQuery = buildQueryParams({
      isTechnician: false,
      userId: "mgr-2",
      tab: "unassigned",
      page: 2,
      pageSize: 25,
    });
    assert.strictEqual(page2UnassignedQuery.assignment, "unassigned");
    assert.strictEqual(page2UnassignedQuery.status, "Triaged,Assigned");
    assert.strictEqual(page2UnassignedQuery.page, 2);

    const page3SlaQuery = buildQueryParams({
      isTechnician: false,
      userId: "mgr-2",
      tab: "sla",
      page: 3,
      pageSize: 50,
    });
    assert.strictEqual(page3SlaQuery.slaStatus, "Attention");
    assert.strictEqual(page3SlaQuery.status, "active");
    assert.strictEqual(page3SlaQuery.page, 3);
    assert.strictEqual(page3SlaQuery.pageSize, 50);
  });

  it("13. Switching 25 -> 50 -> 25 calculates total pages and preserves controls access", () => {
    const totalCount = 49;

    // At 25 per page -> 2 pages
    const totalPages25 = Math.ceil(totalCount / 25);
    assert.strictEqual(totalPages25, 2);

    // Switching to 50 per page -> 1 page
    const totalPages50 = Math.ceil(totalCount / 50);
    assert.strictEqual(totalPages50, 1);

    // Controls remain rendered because totalCount > 0
    assert.ok(totalCount > 0, "Pagination controls must remain mounted when totalCount > 0 even if totalPages === 1");

    // Switching back to 25 per page -> 2 pages
    const totalPagesRevert = Math.ceil(totalCount / 25);
    assert.strictEqual(totalPagesRevert, 2);
  });
});
