const DEFAULT_API_URL = "http://localhost:5151";
const configuredApiUrl = import.meta.env.VITE_API_URL?.trim();
const API_URL = (configuredApiUrl || DEFAULT_API_URL).replace(/\/+$/, "");

type ApiErrorKind =
  | "auth"
  | "forbidden"
  | "validation"
  | "network"
  | "not_found"
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
  reporter?: {
    id: string;
    name: string;
  };
  assignedTo?: {
    id: string;
    name: string;
  } | null;
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
  resolvedAt: string | null;
  closedAt: string | null;
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

    if (
      isRecord(data) &&
      typeof data.message === "string" &&
      data.message.trim()
    ) {
      return data.message;
    }

    if (isRecord(data) && isRecord(data.errors)) {
      for (const value of Object.values(data.errors)) {
        if (
          Array.isArray(value) &&
          typeof value[0] === "string" &&
          value[0].trim()
        ) {
          return value[0];
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
    throw new ApiError(
      "auth",
      "Session expired. Please sign in again.",
      response.status
    );
  }

  if (response.status === 403) {
    throw new ApiError(
      "forbidden",
      await readErrorMessage(
        response,
        messages.forbidden ?? "FORBIDDEN"
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
        messages.notFound ?? "NOT_FOUND"
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

export async function getIncidents() {
  return requestJson<Incident[]>(
    "/api/incidents",
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
  status: IncidentStatus
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

