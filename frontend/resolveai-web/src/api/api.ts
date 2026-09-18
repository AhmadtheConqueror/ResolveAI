const DEFAULT_API_URL = "http://localhost:5151";
const configuredApiUrl = import.meta.env?.VITE_API_URL?.trim();
const API_URL = (configuredApiUrl || DEFAULT_API_URL).replace(/\/+$/, "");

type ApiErrorKind =
  | "auth"
  | "forbidden"
  | "validation"
  | "network"
  | "not_found"
  | "rate_limit"
  | "server";

export class ApiError extends Error {
  kind: ApiErrorKind;
  status?: number;

  constructor(kind: ApiErrorKind, message: string, status?: number) {
    super(message);
    this.name = "ApiError";
    this.kind = kind;
    this.status = status;
  }
}

export type CurrentUser = {
  id: string;
  firstName: string;
  lastName: string;
  email: string;
  role: string;
};

export type IncidentStatus =
  | "Open"
  | "Triaged"
  | "Assigned"
  | "InProgress"
  | "WaitingForUser"
  | "Resolved"
  | "Closed";

type LoginResponse = {
  token: string;
  user: CurrentUser;
};

export type Incident = {
  id: string;
  incidentNumber: string;
  title: string;
  status: IncidentStatus;
  category: string;
  priority: string;
  createdAt: string;
  updatedAt?: string;
  firstRespondedAt?: string | null;
  reporter?: {
    id: string;
    name: string;
  };
  assignedTo?: {
    id: string;
    name: string;
  } | null;
  resolution?: string | null;
  sla?: IncidentSlaCompact;
};

export type SlaStatus =
  | "OnTrack"
  | "AtRisk"
  | "Breached"
  | "Met"
  | "NotApplicable";

export type IncidentSlaCompact = {
  overallStatus: SlaStatus | string;
  responseStatus: SlaStatus | string;
  resolutionStatus: SlaStatus | string;
  responseDueAt: string | null;
  resolutionDueAt: string | null;
  requiresEscalation: boolean;
  responseRemainingMinutes?: number | null;
  responseOverdueMinutes?: number | null;
  resolutionRemainingMinutes?: number | null;
  resolutionOverdueMinutes?: number | null;
};

export type PagedResult<T> = {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
};

export type IncidentQueryParams = {
  search?: string;
  status?: string;
  priority?: string;
  category?: string;
  slaStatus?: string;
  assignment?: string;
  assignedToId?: string;
  page?: number;
  pageSize?: number;
  sortBy?: string;
  sortDirection?: "asc" | "desc";
};

export type QueueSummary = {
  role: string;
  triageCount?: number;
  unassignedCount?: number;
  slaAttentionCount?: number;
  activeCount?: number;
  resolvedCount?: number;
  allAssignedCount?: number;
  assignedCount?: number;
  inProgressCount?: number;
  waitingForUserCount?: number;
};

export type TechnicianWorkload = {
  technician: {
    id: string;
    name: string;
    email: string;
  };
  activeCount: number;
  assignedCount: number;
  inProgressCount: number;
  waitingForUserCount: number;
};

export type NotificationItem = {
  id: string;
  type: string;
  title: string;
  message: string;
  incidentId: string | null;
  incidentTitle?: string | null;
  incidentNumber?: string | null;
  isRead: boolean;
  createdAt: string;
  readAt: string | null;
  actorUserId?: string | null;
};

export type NotificationsResponse = {
  items: NotificationItem[];
  unreadCount: number;
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
};

export type AnalyticsRange = "7" | "30" | "90" | "all";

export type AnalyticsTrendPoint = {
  date: string;
  dateEnd?: string | null;
  count: number;
  granularity?: "daily" | "weekly" | "monthly";
};

export type AnalyticsOverview = {
  period: {
    from: string | null;
    to: string;
    range: string;
  };
  kpis: {
    totalIncidents: number;
    activeIncidents: number;
    resolvedIncidents: number;
    slaSuccessPercent: number | null;
    averageFirstResponseMinutes: number | null;
    averageResolutionMinutes: number | null;
  };
  incidentTrend: AnalyticsTrendPoint[];
  byStatus: Array<{
    status: string;
    count: number;
  }>;
  byPriority: Array<{
    priority: string;
    count: number;
  }>;
  byCategory: Array<{
    category: string;
    count: number;
  }>;
  slaPerformance: {
    met: number;
    breached: number;
    activeAtRisk: number;
    activeBreached: number;
  };
  technicianPerformance: Array<{
    technicianId: string;
    technicianName: string;
    active: number;
    resolved: number;
    slaSuccessPercent: number | null;
    averageResolutionMinutes: number | null;
  }>;
  departmentPerformance: Array<{
    departmentId: string | null;
    departmentName: string;
    incidents: number;
    active: number;
    resolved: number;
    slaSuccessPercent: number | null;
  }>;
};

export type IncidentSla = {
  responseTargetMinutes: number;
  resolutionTargetMinutes: number;
  responseDueAt: string | null;
  resolutionDueAt: string | null;
  firstRespondedAt: string | null;
  resolvedAt: string | null;
  responseBreached: boolean;
  resolutionBreached: boolean;
  overallBreached: boolean;
  responseRemainingMinutes: number | null;
  resolutionRemainingMinutes: number | null;
  responseOverdueMinutes: number | null;
  resolutionOverdueMinutes: number | null;
  responseStatus: SlaStatus | string;
  resolutionStatus: SlaStatus | string;
  overallStatus: SlaStatus | string;
  requiresEscalation: boolean;
};

export type IncidentPerson = {
  id: string;
  name: string;
  email: string;
};

export type IncidentDetail = {
  id: string;
  incidentNumber: string;
  title: string;
  description: string;
  status: IncidentStatus;
  category: string;
  priority: {
    name: string;
    level: number;
  };
  reporter: IncidentPerson;
  assignedTo: IncidentPerson | null;
  resolution: string | null;
  createdAt: string;
  updatedAt: string;
  firstRespondedAt: string | null;
  resolvedAt: string | null;
  closedAt: string | null;
  sla?: IncidentSla;
};

export type TechnicianUser = {
  id: string;
  firstName: string;
  lastName: string;
  email: string;
};

export type IncidentAssignment = {
  id: string;
  status: IncidentStatus;
  assignedTo: IncidentPerson;
  updatedAt: string;
  assignmentType: "Assigned" | "Reassigned";
};

export type IncidentStatusUpdate = {
  id: string;
  status: IncidentStatus;
  updatedAt: string;
  resolvedAt: string | null;
  closedAt: string | null;
};

export type IncidentComment = {
  id: string;
  comment: string;
  createdAt: string;
  author: {
    id: string;
    name: string;
    role: string;
  };
};

export type IncidentActivityActorType = "User" | "System" | "AI";

export type IncidentActivityEvent = {
  id: string;
  eventType: string;
  summary: string;
  oldValue: string | null;
  newValue: string | null;
  actor: {
    id: string | null;
    name: string | null;
  };
  actorType: IncidentActivityActorType | string;
  metadata: unknown | null;
  createdAt: string;
};

export type IncidentAIAnalysis = {
  id: string;
  provider: string;
  model: string;
  categoryRecommendation: string;
  priorityRecommendation: string;
  urgency: string;
  confidence: number;
  possibleCause: string;
  reasoningSummary: string;
  suggestedActions: string[];
  createdAt: string;
  promptVersion?: string;
  categoryApplied?: boolean;
  priorityApplied?: boolean;
  appliedAt?: string | null;
  appliedByUserId?: string | null;
  appliedByUser?: {
    id: string;
    name: string;
    email?: string;
  } | null;
};

export type SuggestedResolutionStep = {
  stepNumber: number;
  action: string;
  reason: string;
  evidenceIncidentNumbers: string[];
};

export type SimilarResolvedIncidentEvidence = {
  incidentId: string;
  incidentNumber: string;
  title: string;
  category: string;
  resolvedAt: string | null;
  resolutionExcerpt: string;
  matchStrength: "High" | "Moderate" | "Low" | string;
  reasonForMatch: string;
};

export type IncidentAIResolutionAnalysis = {
  id: string;
  incidentId: string;
  summary: string;
  likelyIssue: string;
  confidence: "High" | "Moderate" | "Low" | string;
  suggestedSteps: SuggestedResolutionStep[];
  evidence: SimilarResolvedIncidentEvidence[];
  caveats: string;
  hasSufficientEvidence: boolean;
  candidateCount: number;
  provider: string;
  model: string;
  promptVersion: string;
  createdAt: string;
  requestedByUserId: string;
  requestedByUser?: {
    id: string;
    name: string;
    email: string;
  } | null;
};

export type IncidentOption = {
  id: string;
  name: string;
  level?: number;
};

export type IncidentOptions = {
  categories: IncidentOption[];
  priorities: IncidentOption[];
};

type CreateIncidentRequest = {
  title: string;
  description: string;
  categoryId: string;
  priorityId: string;
};

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}

export function isUnauthorizedError(error: unknown) {
  return (
    error instanceof ApiError &&
    error.kind === "auth" &&
    error.status === 401
  );
}

export function isForbiddenError(error: unknown) {
  return (
    error instanceof ApiError &&
    error.kind === "forbidden" &&
    error.status === 403
  );
}

export function isNotFoundError(error: unknown) {
  return (
    error instanceof ApiError &&
    error.kind === "not_found" &&
    error.status === 404
  );
}

export function isValidationError(error: unknown) {
  return (
    error instanceof ApiError &&
    error.kind === "validation" &&
    error.status === 400
  );
}

export function isRateLimitError(error: unknown) {
  return (
    error instanceof ApiError &&
    (error.kind === "rate_limit" || error.status === 429)
  );
}

function isLoginResponse(value: unknown): value is LoginResponse {
  if (
    !isRecord(value) ||
    typeof value.token !== "string" ||
    !isRecord(value.user)
  ) {
    return false;
  }

  const { id, firstName, lastName, email, role } = value.user;

  return (
    typeof id === "string" &&
    typeof firstName === "string" &&
    typeof lastName === "string" &&
    typeof email === "string" &&
    typeof role === "string"
  );
}

async function readLoginResponse(response: Response) {
  try {
    const data: unknown = await response.json();

    if (isLoginResponse(data)) {
      return data;
    }
  } catch {
    // Handled below with a consistent UI-facing error.
  }

  throw new ApiError(
    "server",
    "ResolveAI returned an unexpected sign-in response.",
    response.status
  );
}

export async function login(email: string, password: string) {
  let response: Response;

  try {
    response = await fetch(`${API_URL}/api/auth/login`, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
      },
      body: JSON.stringify({
        email,
        password,
      }),
    });
  } catch {
    throw new ApiError(
      "network",
      "Unable to connect to ResolveAI."
    );
  }

  if (response.status === 401) {
    throw new ApiError(
      "auth",
      "Invalid email or password.",
      response.status
    );
  }

  if (response.status === 429) {
    throw new ApiError(
      "rate_limit",
      "Too many login attempts. Please try again shortly.",
      response.status
    );
  }

  if (!response.ok) {
    throw new ApiError(
      "server",
      "ResolveAI could not sign you in. Please try again.",
      response.status
    );
  }

  return readLoginResponse(response);
}

function getAuthHeaders() {
  const token = sessionStorage.getItem("accessToken");

  if (!token) {
    throw new ApiError(
      "auth",
      "Session expired. Please sign in again.",
      401
    );
  }

  return {
    Authorization: `Bearer ${token}`,
  };
}

async function readJson<T>(
  response: Response,
  fallbackMessage: string
) {
  try {
    return (await response.json()) as T;
  } catch {
    throw new ApiError(
      "server",
      fallbackMessage,
      response.status
    );
  }
}

async function readErrorMessage(
  response: Response,
  fallbackMessage: string
) {
  try {
    const data: unknown = await response.json();

    if (isRecord(data)) {
      if (typeof data.detail === "string" && data.detail.trim()) {
        return data.detail.trim();
      }

      if (typeof data.message === "string" && data.message.trim()) {
        return data.message.trim();
      }

      if (typeof data.title === "string" && data.title.trim()) {
        return data.title.trim();
      }

      if (isRecord(data.errors)) {
        for (const value of Object.values(data.errors)) {
          if (
            Array.isArray(value) &&
            typeof value[0] === "string" &&
            value[0].trim()
          ) {
            return value[0].trim();
          }
        }
      }
    }
  } catch {
    // Use the caller's safe fallback below.
  }

  return fallbackMessage;
}

async function requestJson<T>(
  path: string,
  init: RequestInit,
  messages: {
    network: string;
    forbidden?: string;
    notFound?: string;
    validation?: string;
    server: string;
    parse: string;
  }
) {
  let response: Response;

  try {
    response = await fetch(`${API_URL}${path}`, init);
  } catch {
    throw new ApiError("network", messages.network);
  }

  if (response.status === 401) {
    sessionStorage.removeItem("accessToken");
    sessionStorage.removeItem("currentUser");
    throw new ApiError(
      "auth",
      await readErrorMessage(
        response,
        "Session expired. Please sign in again."
      ),
      response.status
    );
  }

  if (response.status === 403) {
    throw new ApiError(
      "forbidden",
      await readErrorMessage(
        response,
        messages.forbidden ?? "Access denied."
      ),
      response.status
    );
  }

  if (response.status === 429) {
    throw new ApiError(
      "rate_limit",
      await readErrorMessage(
        response,
        "Too many requests. Please try again shortly."
      ),
      response.status
    );
  }

  if (response.status === 400) {
    throw new ApiError(
      "validation",
      await readErrorMessage(
        response,
        messages.validation ?? messages.server
      ),
      response.status
    );
  }

  if (response.status === 404) {
    throw new ApiError(
      "not_found",
      await readErrorMessage(
        response,
        messages.notFound ?? "Not found."
      ),
      response.status
    );
  }

  if (!response.ok) {
    throw new ApiError(
      "server",
      await readErrorMessage(response, messages.server),
      response.status
    );
  }

  return readJson<T>(response, messages.parse);
}

export async function getIncidents(params?: IncidentQueryParams) {
  let url = "/api/incidents";
  if (params) {
    const sp = new URLSearchParams();
    if (params.search) sp.set("search", params.search);
    if (params.status) sp.set("status", params.status);
    if (params.priority) sp.set("priority", params.priority);
    if (params.category) sp.set("category", params.category);
    if (params.slaStatus) sp.set("slaStatus", params.slaStatus);
    if (params.assignment) sp.set("assignment", params.assignment);
    if (params.assignedToId) sp.set("assignedToId", params.assignedToId);
    if (params.page !== undefined) sp.set("page", String(params.page));
    if (params.pageSize !== undefined) sp.set("pageSize", String(params.pageSize));
    if (params.sortBy) sp.set("sortBy", params.sortBy);
    if (params.sortDirection) sp.set("sortDirection", params.sortDirection);
    const q = sp.toString();
    if (q) url += `?${q}`;
  }

  return requestJson<Incident[]>(
    url,
    {
      method: "GET",
      headers: getAuthHeaders(),
    },
    {
      network: "Unable to connect to ResolveAI.",
      server: "Unable to load incidents.",
      parse: "ResolveAI returned unexpected incident data.",
    }
  );
}

export async function getIncidentsPaged(params: IncidentQueryParams) {
  const sp = new URLSearchParams();
  if (params.search) sp.set("search", params.search);
  if (params.status) sp.set("status", params.status);
  if (params.priority) sp.set("priority", params.priority);
  if (params.category) sp.set("category", params.category);
  if (params.slaStatus) sp.set("slaStatus", params.slaStatus);
  if (params.assignment) sp.set("assignment", params.assignment);
  if (params.assignedToId) sp.set("assignedToId", params.assignedToId);
  sp.set("page", String(params.page ?? 1));
  sp.set("pageSize", String(params.pageSize ?? 25));
  if (params.sortBy) sp.set("sortBy", params.sortBy);
  if (params.sortDirection) sp.set("sortDirection", params.sortDirection);

  return requestJson<PagedResult<Incident>>(
    `/api/incidents?${sp.toString()}`,
    {
      method: "GET",
      headers: getAuthHeaders(),
    },
    {
      network: "Unable to connect to ResolveAI.",
      server: "Unable to load incidents.",
      parse: "ResolveAI returned unexpected incident data.",
    }
  );
}

export async function getQueueSummary() {
  return requestJson<QueueSummary>(
    "/api/incidents/queue-summary",
    {
      method: "GET",
      headers: getAuthHeaders(),
    },
    {
      network: "Unable to connect to ResolveAI.",
      server: "Unable to load queue metrics.",
      parse: "ResolveAI returned unexpected queue data.",
    }
  );
}

export async function getTechnicianWorkload() {
  return requestJson<TechnicianWorkload[]>(
    "/api/incidents/workload",
    {
      method: "GET",
      headers: getAuthHeaders(),
    },
    {
      network: "Unable to connect to ResolveAI.",
      forbidden: "You do not have permission to view technician workload.",
      server: "Unable to load technician workload.",
      parse: "ResolveAI returned unexpected workload data.",
    }
  );
}

export async function getNotifications(params?: {
  page?: number;
  pageSize?: number;
  unreadOnly?: boolean;
}) {
  const sp = new URLSearchParams();
  sp.set("page", String(params?.page ?? 1));
  sp.set("pageSize", String(params?.pageSize ?? 20));
  if (params?.unreadOnly) sp.set("unreadOnly", "true");

  return requestJson<NotificationsResponse>(
    `/api/notifications?${sp.toString()}`,
    {
      method: "GET",
      headers: getAuthHeaders(),
    },
    {
      network: "Unable to connect to ResolveAI.",
      server: "Unable to load notifications.",
      parse: "ResolveAI returned unexpected notification data.",
    }
  );
}

export async function markNotificationRead(id: string) {
  return requestJson<{
    id: string;
    isRead: boolean;
    readAt: string | null;
  }>(
    `/api/notifications/${id}/read`,
    {
      method: "PATCH",
      headers: getAuthHeaders(),
    },
    {
      network: "Unable to connect to ResolveAI.",
      notFound: "Notification not found.",
      server: "Unable to mark notification as read.",
      parse: "ResolveAI returned unexpected notification data.",
    }
  );
}

export async function markAllNotificationsRead() {
  return requestJson<{
    updatedCount: number;
    unreadCount: number;
  }>(
    "/api/notifications/read-all",
    {
      method: "PATCH",
      headers: getAuthHeaders(),
    },
    {
      network: "Unable to connect to ResolveAI.",
      server: "Unable to mark notifications as read.",
      parse: "ResolveAI returned unexpected notification data.",
    }
  );
}

export async function getAnalyticsOverview(params?: {
  range?: AnalyticsRange;
  from?: string;
  to?: string;
  departmentId?: string;
  technicianId?: string;
}) {
  const sp = new URLSearchParams();
  if (params?.range) sp.set("range", params.range);
  if (params?.from) sp.set("from", params.from);
  if (params?.to) sp.set("to", params.to);
  if (params?.departmentId) sp.set("departmentId", params.departmentId);
  if (params?.technicianId) sp.set("technicianId", params.technicianId);

  const query = sp.toString();

  return requestJson<AnalyticsOverview>(
    `/api/analytics/overview${query ? `?${query}` : ""}`,
    {
      method: "GET",
      headers: getAuthHeaders(),
    },
    {
      network: "Unable to connect to ResolveAI.",
      forbidden: "You do not have permission to view analytics.",
      server: "Unable to load analytics.",
      parse: "ResolveAI returned unexpected analytics data.",
    }
  );
}

export async function getIncident(id: string) {
  return requestJson<IncidentDetail>(
    `/api/incidents/${id}`,
    {
      method: "GET",
      headers: getAuthHeaders(),
    },
    {
      network: "Unable to connect to ResolveAI.",
      forbidden: "FORBIDDEN",
      notFound: "NOT_FOUND",
      server: "Unable to load incident details.",
      parse: "ResolveAI returned unexpected incident details.",
    }
  );
}

export async function getIncidentOptions() {
  return requestJson<IncidentOptions>(
    "/api/incidents/options",
    {
      method: "GET",
      headers: getAuthHeaders(),
    },
    {
      network: "Unable to connect to ResolveAI.",
      server: "Unable to load incident options.",
      parse: "ResolveAI returned unexpected incident options.",
    }
  );
}

export async function createIncident(data: CreateIncidentRequest) {
  return requestJson<Incident>(
    "/api/incidents",
    {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        ...getAuthHeaders(),
      },
      body: JSON.stringify(data),
    },
    {
      network: "Unable to connect to ResolveAI.",
      server: "Unable to create incident.",
      parse: "ResolveAI returned unexpected incident data.",
    }
  );
}

export async function getTechnicians() {
  return requestJson<TechnicianUser[]>(
    "/api/users/technicians",
    {
      method: "GET",
      headers: getAuthHeaders(),
    },
    {
      network: "Unable to connect to ResolveAI.",
      forbidden: "You do not have permission to view technicians.",
      server: "Unable to load technicians.",
      parse: "ResolveAI returned unexpected technician data.",
    }
  );
}

export async function assignIncident(
  incidentId: string,
  technicianId: string
) {
  return requestJson<IncidentAssignment>(
    `/api/incidents/${incidentId}/assign`,
    {
      method: "PATCH",
      headers: {
        "Content-Type": "application/json",
        ...getAuthHeaders(),
      },
      body: JSON.stringify({
        technicianId,
      }),
    },
    {
      network: "Unable to connect to ResolveAI.",
      forbidden: "You do not have permission to assign this incident.",
      validation: "Unable to assign technician.",
      server: "Unable to assign technician.",
      parse: "ResolveAI returned unexpected assignment data.",
    }
  );
}

export async function updateIncidentStatus(
  incidentId: string,
  status: IncidentStatus,
  resolution?: string,
  reason?: string
) {
  return requestJson<IncidentStatusUpdate>(
    `/api/incidents/${incidentId}/status`,
    {
      method: "PATCH",
      headers: {
        "Content-Type": "application/json",
        ...getAuthHeaders(),
      },
      body: JSON.stringify({
        status,
        resolution: resolution?.trim() || undefined,
        reason: reason?.trim() || undefined,
      }),
    },
    {
      network: "Unable to connect to ResolveAI.",
      forbidden: "You do not have permission to update this incident.",
      validation: "Unable to update incident status.",
      server: "Unable to update incident status.",
      parse: "ResolveAI returned unexpected status data.",
    }
  );
}

export async function getIncidentComments(incidentId: string) {
  return requestJson<IncidentComment[]>(
    `/api/incidents/${incidentId}/comments`,
    {
      method: "GET",
      headers: getAuthHeaders(),
    },
    {
      network: "Unable to connect to ResolveAI.",
      forbidden: "You do not have permission to view this conversation.",
      notFound: "Incident not found.",
      server: "Unable to load incident conversation.",
      parse: "ResolveAI returned unexpected comment data.",
    }
  );
}

export async function getIncidentActivity(
  incidentId: string,
  params?: {
    page?: number;
    pageSize?: number;
  }
) {
  const sp = new URLSearchParams();
  sp.set("page", String(params?.page ?? 1));
  sp.set("pageSize", String(params?.pageSize ?? 20));

  return requestJson<PagedResult<IncidentActivityEvent>>(
    `/api/incidents/${incidentId}/activity?${sp.toString()}`,
    {
      method: "GET",
      headers: getAuthHeaders(),
    },
    {
      network: "Unable to connect to ResolveAI.",
      forbidden: "You do not have permission to view this activity history.",
      notFound: "Incident not found.",
      server: "Unable to load activity history.",
      parse: "ResolveAI returned unexpected activity history data.",
    }
  );
}

export async function addIncidentComment(
  incidentId: string,
  comment: string
) {
  return requestJson<IncidentComment>(
    `/api/incidents/${incidentId}/comments`,
    {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        ...getAuthHeaders(),
      },
      body: JSON.stringify({
        comment,
      }),
    },
    {
      network: "Unable to connect to ResolveAI.",
      forbidden: "You do not have permission to comment on this incident.",
      validation: "Unable to post comment.",
      server: "Unable to post comment.",
      parse: "ResolveAI returned unexpected comment data.",
    }
  );
}

export async function runIncidentAIAnalysis(incidentId: string) {
  return requestJson<IncidentAIAnalysis>(
    `/api/incidents/${incidentId}/ai-analysis`,
    {
      method: "POST",
      headers: getAuthHeaders(),
    },
    {
      network: "Unable to connect to ResolveAI.",
      forbidden: "You do not have permission to run AI analysis.",
      server: "AI analysis is temporarily unavailable. Please try again.",
      parse: "ResolveAI returned unexpected AI analysis data.",
    }
  );
}

export async function getIncidentAIAnalyses(incidentId: string) {
  return requestJson<IncidentAIAnalysis[]>(
    `/api/incidents/${incidentId}/ai-analysis`,
    {
      method: "GET",
      headers: getAuthHeaders(),
    },
    {
      network: "Unable to connect to ResolveAI.",
      forbidden: "You do not have permission to view AI analysis.",
      notFound: "Incident not found.",
      server: "Unable to load AI analysis history.",
      parse: "ResolveAI returned unexpected AI analysis history.",
    }
  );
}

export async function generateIncidentAIResolutionAnalysis(incidentId: string) {
  return requestJson<IncidentAIResolutionAnalysis>(
    `/api/incidents/${incidentId}/ai-resolution-analysis`,
    {
      method: "POST",
      headers: getAuthHeaders(),
    },
    {
      network: "Unable to connect to ResolveAI.",
      forbidden: "You do not have permission to generate AI resolution assistance.",
      server: "AI resolution assistance is temporarily unavailable. Please try again.",
      parse: "ResolveAI returned unexpected AI resolution assistance data.",
    }
  );
}

export async function getIncidentAIResolutionAnalyses(incidentId: string) {
  return requestJson<IncidentAIResolutionAnalysis[]>(
    `/api/incidents/${incidentId}/ai-resolution-analysis`,
    {
      method: "GET",
      headers: getAuthHeaders(),
    },
    {
      network: "Unable to connect to ResolveAI.",
      forbidden: "You do not have permission to view AI resolution assistance.",
      notFound: "Incident not found.",
      server: "Unable to load AI resolution assistance history.",
      parse: "ResolveAI returned unexpected AI resolution history.",
    }
  );
}

export type ApplyAIRecommendationResponse = {
  incidentId: string;
  category: string;
  priority: string;
  updatedAt: string;
  applied: {
    category: boolean;
    priority: boolean;
  };
};

export async function applyAIRecommendation(
  incidentId: string,
  analysisId: string,
  options: {
    applyCategory: boolean;
    applyPriority: boolean;
  }
) {
  return requestJson<ApplyAIRecommendationResponse>(
    `/api/incidents/${incidentId}/ai-analysis/${analysisId}/apply`,
    {
      method: "PATCH",
      headers: {
        "Content-Type": "application/json",
        ...getAuthHeaders(),
      },
      body: JSON.stringify(options),
    },
    {
      network: "Unable to connect to ResolveAI.",
      forbidden: "You do not have permission to apply AI recommendations.",
      notFound: "Incident or AI analysis not found.",
      validation: "Unable to apply AI recommendation.",
      server: "Unable to apply AI recommendation.",
      parse: "ResolveAI returned unexpected response after applying recommendation.",
    }
  );
}

// ─── User Administration ────────────────────────────────────────────────────

export type UserRecord = {
  id: string;
  firstName: string;
  lastName: string;
  email: string;
  role: { id: string; name: string };
  department: { id: string; name: string } | null;
  isActive: boolean;
  createdAt: string;
};

export type UserOptions = {
  roles: { id: string; name: string }[];
  departments: { id: string; name: string }[];
};

export async function getUsers(): Promise<UserRecord[]> {
  return requestJson<UserRecord[]>(
    "/api/users",
    {
      method: "GET",
      headers: getAuthHeaders(),
    },
    {
      network: "Unable to connect to ResolveAI.",
      forbidden: "You do not have permission to view users.",
      server: "Unable to load users.",
      parse: "ResolveAI returned unexpected user data.",
    }
  );
}

export async function getUserOptions(): Promise<UserOptions> {
  return requestJson<UserOptions>(
    "/api/users/options",
    {
      method: "GET",
      headers: getAuthHeaders(),
    },
    {
      network: "Unable to connect to ResolveAI.",
      forbidden: "You do not have permission to view user options.",
      server: "Unable to load user options.",
      parse: "ResolveAI returned unexpected user options data.",
    }
  );
}

export async function createUser(payload: {
  firstName: string;
  lastName: string;
  email: string;
  roleId: string;
  departmentId?: string | null;
  temporaryPassword: string;
}): Promise<UserRecord> {
  return requestJson<UserRecord>(
    "/api/users",
    {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        ...getAuthHeaders(),
      },
      body: JSON.stringify(payload),
    },
    {
      network: "Unable to connect to ResolveAI.",
      forbidden: "You do not have permission to create users.",
      validation: "Please check the provided user details.",
      server: "Unable to create user.",
      parse: "ResolveAI returned unexpected user data after creation.",
    }
  );
}

export async function updateUser(
  id: string,
  payload: {
    firstName: string;
    lastName: string;
    email: string;
    roleId: string;
    departmentId?: string | null;
  }
): Promise<UserRecord> {
  return requestJson<UserRecord>(
    `/api/users/${id}`,
    {
      method: "PATCH",
      headers: {
        "Content-Type": "application/json",
        ...getAuthHeaders(),
      },
      body: JSON.stringify(payload),
    },
    {
      network: "Unable to connect to ResolveAI.",
      forbidden: "You do not have permission to update users.",
      notFound: "User not found.",
      validation: "Please check the updated user details.",
      server: "Unable to update user.",
      parse: "ResolveAI returned unexpected user data after update.",
    }
  );
}

export async function updateUserStatus(
  id: string,
  isActive: boolean
): Promise<{ id: string; isActive: boolean }> {
  return requestJson<{ id: string; isActive: boolean }>(
    `/api/users/${id}/status`,
    {
      method: "PATCH",
      headers: {
        "Content-Type": "application/json",
        ...getAuthHeaders(),
      },
      body: JSON.stringify({ isActive }),
    },
    {
      network: "Unable to connect to ResolveAI.",
      forbidden: "You do not have permission to modify user status.",
      notFound: "User not found.",
      server: "Unable to update user status.",
      parse: "ResolveAI returned unexpected response after updating status.",
    }
  );
}

export async function resetUserPassword(
  id: string,
  temporaryPassword: string
): Promise<{ message: string }> {
  return requestJson<{ message: string }>(
    `/api/users/${id}/password`,
    {
      method: "PATCH",
      headers: {
        "Content-Type": "application/json",
        ...getAuthHeaders(),
      },
      body: JSON.stringify({ temporaryPassword }),
    },
    {
      network: "Unable to connect to ResolveAI.",
      forbidden: "You do not have permission to reset user passwords.",
      notFound: "User not found.",
      validation: "Please check the temporary password requirements.",
      server: "Unable to reset user password.",
      parse: "ResolveAI returned unexpected response after resetting password.",
    }
  );
}
