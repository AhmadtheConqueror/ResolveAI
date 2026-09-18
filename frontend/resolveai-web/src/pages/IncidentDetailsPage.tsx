import { useCallback, useEffect, useMemo, useState } from "react";
import type { FormEvent } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";

import {
  addIncidentComment,
  applyAIRecommendation,
  assignIncident,
  generateIncidentAIResolutionAnalysis,
  getIncident,
  getIncidentAIAnalyses,
  getIncidentAIResolutionAnalyses,
  getIncidentActivity,
  getIncidentComments,
  getTechnicians,
  isForbiddenError,
  isNotFoundError,
  isUnauthorizedError,
  runIncidentAIAnalysis,
  updateIncidentStatus,
} from "../api/api";
import type {
  CurrentUser,
  IncidentAIAnalysis,
  IncidentAIResolutionAnalysis,
  IncidentActivityEvent,
  IncidentComment,
  IncidentDetail,
  IncidentSla,
  IncidentStatus,
  SuggestedResolutionStep,
  TechnicianUser,
} from "../api/api";
import Sidebar from "../components/Sidebar";
import { Skeleton, SkeletonText } from "../components/Skeleton";

type DetailError = {
  title: string;
  message: string;
};

type UiNotice = {
  tone: "success" | "error";
  message: string;
};

type StatusAction = {
  label: string;
  status: IncidentStatus;
  tone?: "primary" | "secondary" | "success" | "danger";
};

type ActivityChange = {
  field?: string;
  oldValue?: string | null;
  newValue?: string | null;
};

type ActivityMetadata = {
  changes?: ActivityChange[];
  dimension?: string;
  targetMinutes?: number | null;
  remainingMinutes?: number | null;
  overdueMinutes?: number | null;
  reason?: string | null;
  isAdministrativeClosure?: boolean | null;
};

const ACTIVITY_PAGE_SIZE = 20;

const STATUS_LABELS: Record<IncidentStatus, string> = {
  Open: "Open",
  Triaged: "Triaged",
  Assigned: "Assigned",
  InProgress: "In Progress",
  WaitingForUser: "Waiting for User",
  Resolved: "Resolved",
  Closed: "Closed",
};

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

function formatDate(value?: string | null) {
  if (!value) {
    return "Not recorded";
  }

  return new Intl.DateTimeFormat("en", {
    month: "short",
    day: "numeric",
    year: "numeric",
    hour: "numeric",
    minute: "2-digit",
  }).format(new Date(value));
}

function formatStatusLabel(value: IncidentStatus | string) {
  return value in STATUS_LABELS
    ? STATUS_LABELS[value as IncidentStatus]
    : value;
}

function isPlainRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function getActivityMetadata(event: IncidentActivityEvent) {
  return isPlainRecord(event.metadata)
    ? (event.metadata as ActivityMetadata)
    : null;
}

function getActivityChanges(event: IncidentActivityEvent) {
  const metadata = getActivityMetadata(event);

  if (Array.isArray(metadata?.changes)) {
    return metadata.changes.filter(
      (change) => change.oldValue || change.newValue
    );
  }

  if (event.oldValue && event.newValue) {
    return [
      {
        oldValue: event.oldValue,
        newValue: event.newValue,
      },
    ];
  }

  return [];
}

function getActivityActorLabel(event: IncidentActivityEvent) {
  if (event.actorType === "System") {
    return "System";
  }

  if (event.actorType === "AI") {
    return event.actor.name || "ResolveAI AI";
  }

  const name = event.actor.name || "Unknown user";

  return event.eventType === "AIRecommendationApplied"
    ? `Approved by ${name}`
    : name;
}

function getActivityMarker(eventType: string) {
  if (eventType.startsWith("AI")) {
    return "AI";
  }

  if (eventType.startsWith("Sla")) {
    return "SLA";
  }

  if (eventType.includes("Assigned") || eventType.includes("Unassigned")) {
    return "A";
  }

  if (eventType === "CommentAdded") {
    return "C";
  }

  if (eventType.includes("Priority")) {
    return "P";
  }

  return "S";
}

function getActivityTone(eventType: string) {
  if (eventType === "SlaBreached") {
    return "critical";
  }

  if (eventType === "SlaAtRisk" || eventType === "PriorityChanged") {
    return "attention";
  }

  if (eventType.startsWith("AI")) {
    return "ai";
  }

  return "default";
}

function getActivitySlaDetail(event: IncidentActivityEvent) {
  if (!event.eventType.startsWith("Sla")) {
    return null;
  }

  const metadata = getActivityMetadata(event);

  if (!metadata) {
    return null;
  }

  const parts: string[] = [];

  if (typeof metadata.targetMinutes === "number") {
    parts.push(`Target: ${formatTargetDuration(metadata.targetMinutes)}`);
  }

  if (typeof metadata.overdueMinutes === "number" && metadata.overdueMinutes > 0) {
    parts.push(`Exceeded by: ${formatMinutesHuman(metadata.overdueMinutes)}`);
  } else if (
    typeof metadata.remainingMinutes === "number" &&
    metadata.remainingMinutes > 0
  ) {
    parts.push(`Remaining: ${formatMinutesHuman(metadata.remainingMinutes)}`);
  }

  return parts.length > 0 ? parts.join(" | ") : null;
}

function formatPercent(value: number) {
  return `${Math.round(value * 100)}%`;
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

function formatTargetDuration(minutes: number) {
  if (minutes < 60) {
    return `${minutes} minutes`;
  }
  const hours = minutes / 60;
  if (hours % 24 === 0 && hours >= 24) {
    const days = hours / 24;
    return `${days} day${days > 1 ? "s" : ""} (${hours} hours)`;
  }
  return `${hours} hour${hours > 1 ? "s" : ""}`;
}

function formatMinutesHuman(minutes: number) {
  if (minutes < 60) {
    return `${minutes}m`;
  }
  const h = Math.floor(minutes / 60);
  const m = minutes % 60;
  return m > 0 ? `${h}h ${m}m` : `${h}h`;
}

function getResponseStatusDescription(sla: IncidentSla) {
  if (sla.responseStatus === "Met") {
    return "Met within target";
  }
  if (sla.responseStatus === "Breached") {
    return sla.responseOverdueMinutes != null
      ? `Breached (${formatMinutesHuman(sla.responseOverdueMinutes)} overdue)`
      : "Breached";
  }
  if (sla.responseStatus === "AtRisk") {
    return sla.responseRemainingMinutes != null
      ? `At Risk (${formatMinutesHuman(sla.responseRemainingMinutes)} remaining)`
      : "At Risk";
  }
  if (sla.responseStatus === "OnTrack") {
    return sla.responseRemainingMinutes != null
      ? `On Track (${formatMinutesHuman(sla.responseRemainingMinutes)} remaining)`
      : "On Track";
  }
  return "N/A";
}

function getResolutionStatusDescription(sla: IncidentSla) {
  if (sla.resolutionStatus === "Met") {
    return "Met within target";
  }
  if (sla.resolutionStatus === "Breached") {
    return sla.resolutionOverdueMinutes != null
      ? `Breached (${formatMinutesHuman(sla.resolutionOverdueMinutes)} overdue)`
      : "Breached";
  }
  if (sla.resolutionStatus === "AtRisk") {
    return sla.resolutionRemainingMinutes != null
      ? `At Risk (${formatMinutesHuman(sla.resolutionRemainingMinutes)} remaining)`
      : "At Risk";
  }
  if (sla.resolutionStatus === "OnTrack") {
    return sla.resolutionRemainingMinutes != null
      ? `On Track (${formatMinutesHuman(sla.resolutionRemainingMinutes)} remaining)`
      : "On Track";
  }
  return "N/A";
}

function isManagerOrAdmin(role?: string | null) {
  return role === "Manager" || role === "Admin";
}

function getErrorMessage(error: unknown, fallback: string) {
  return error instanceof Error && error.message.trim()
    ? error.message
    : fallback;
}

function canCommentOnIncident(
  user: CurrentUser,
  incident: IncidentDetail
) {
  if (isManagerOrAdmin(user.role)) {
    return true;
  }

  if (user.role === "Employee") {
    return incident.reporter.id === user.id;
  }

  if (user.role === "Technician") {
    return incident.assignedTo?.id === user.id;
  }

  return false;
}

function canRunAIAnalysis(
  user: CurrentUser,
  incident: IncidentDetail
) {
  if (isManagerOrAdmin(user.role)) {
    return true;
  }

  return (
    user.role === "Technician" &&
    incident.assignedTo?.id === user.id
  );
}

function canAccessResolutionAssistant(
  user: CurrentUser,
  incident: IncidentDetail
) {
  if (user.role === "Employee") {
    return false;
  }

  if (isManagerOrAdmin(user.role)) {
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

  return (
    incident.status === "Open" ||
    incident.status === "Triaged" ||
    incident.status === "Assigned" ||
    incident.status === "InProgress" ||
    incident.status === "WaitingForUser"
  );
}

function getStatusActions(
  incident: IncidentDetail,
  user: CurrentUser
): StatusAction[] {
  const assignedToCurrentUser =
    incident.assignedTo?.id === user.id;

  if (user.role === "Employee") {
    return incident.status === "Resolved" &&
      incident.reporter.id === user.id
      ? [
          {
            label: "Confirm & Close",
            status: "Closed",
            tone: "secondary",
          },
        ]
      : [];
  }

  if (user.role === "Technician") {
    if (!assignedToCurrentUser) {
      return [];
    }

    if (incident.status === "Assigned") {
      return [
        {
          label: "Start Work",
          status: "InProgress",
          tone: "primary",
        },
      ];
    }

    if (incident.status === "InProgress") {
      return [
        {
          label: "Waiting for User",
          status: "WaitingForUser",
          tone: "secondary",
        },
        {
          label: "Resolve Incident",
          status: "Resolved",
          tone: "success",
        },
      ];
    }

    if (incident.status === "WaitingForUser") {
      return [
        {
          label: "Resume Work",
          status: "InProgress",
          tone: "primary",
        },
      ];
    }

    return [];
  }

  if (!isManagerOrAdmin(user.role)) {
    return [];
  }

  switch (incident.status) {
    case "Open":
      return [
        {
          label: "Mark as Triaged",
          status: "Triaged",
          tone: "primary",
        },
        {
          label: "Administrative Close",
          status: "Closed",
          tone: "danger",
        },
      ];
    case "Triaged":
    case "Assigned":
    case "InProgress":
    case "WaitingForUser":
      return [
        {
          label: "Administrative Close",
          status: "Closed",
          tone: "danger",
        },
      ];
    case "Resolved":
      return [
        {
          label: "Close Incident",
          status: "Closed",
          tone: "secondary",
        },
      ];
    default:
      return [];
  }
}

export default function IncidentDetailsPage() {
  const { id } = useParams();
  const navigate = useNavigate();

  const [user] = useState<CurrentUser | null>(() =>
    readCurrentUser()
  );
  const [incident, setIncident] =
    useState<IncidentDetail | null>(null);
  const [comments, setComments] = useState<IncidentComment[]>([]);
  const [activityItems, setActivityItems] = useState<IncidentActivityEvent[]>(
    []
  );
  const [activityPage, setActivityPage] = useState(1);
  const [activityTotalCount, setActivityTotalCount] = useState(0);
  const [activityLoading, setActivityLoading] = useState(false);
  const [activityLoadingMore, setActivityLoadingMore] = useState(false);
  const [activityError, setActivityError] = useState("");
  const [technicians, setTechnicians] = useState<TechnicianUser[]>(
    []
  );
  const [selectedTechnicianId, setSelectedTechnicianId] =
    useState("");
  const [loading, setLoading] = useState(true);
  const [loadingTechnicians, setLoadingTechnicians] =
    useState(false);
  const [error, setError] = useState<DetailError | null>(null);
  const [workflowNotice, setWorkflowNotice] =
    useState<UiNotice | null>(null);
  const [commentNotice, setCommentNotice] =
    useState<UiNotice | null>(null);
  const [actionBusy, setActionBusy] =
    useState<IncidentStatus | "assign" | null>(null);
  const [commentText, setCommentText] = useState("");
  const [postingComment, setPostingComment] = useState(false);

  // AI Analysis state
  const [aiAnalyses, setAiAnalyses] = useState<IncidentAIAnalysis[]>([]);
  const [aiLoading, setAiLoading] = useState(false);
  const [runningAnalysis, setRunningAnalysis] = useState(false);
  const [aiNotice, setAiNotice] = useState<UiNotice | null>(null);
  const [applyingRecommendation, setApplyingRecommendation] = useState<
    "category" | "priority" | "both" | null
  >(null);
  const [confirmApplyTarget, setConfirmApplyTarget] = useState<
    "category" | "priority" | "both" | null
  >(null);

  // AI Resolution Assistant state (Phase 2)
  const [resolutionAnalyses, setResolutionAnalyses] = useState<
    IncidentAIResolutionAnalysis[]
  >([]);
  const [resolutionLoading, setResolutionLoading] = useState(false);
  const [runningResolutionAssistant, setRunningResolutionAssistant] =
    useState(false);
  const [resolutionNotice, setResolutionNotice] =
    useState<UiNotice | null>(null);
  const [copiedSteps, setCopiedSteps] = useState(false);

  // Resolution note modal state
  const [showResolveModal, setShowResolveModal] = useState(false);
  const [resolutionText, setResolutionText] = useState("");
  const [resolveError, setResolveError] = useState("");

  // Administrative close modal state
  const [showAdminCloseModal, setShowAdminCloseModal] = useState(false);
  const [adminCloseReason, setAdminCloseReason] = useState("");
  const [adminCloseError, setAdminCloseError] = useState("");

  const handleUnauthorized = useCallback(() => {
    sessionStorage.clear();
    navigate("/", { replace: true });
  }, [navigate]);

  const loadActivity = useCallback(
    async (page = 1) => {
      if (!id) {
        return;
      }

      if (page === 1) {
        setActivityLoading(true);
      } else {
        setActivityLoadingMore(true);
      }

      setActivityError("");

      try {
        const data = await getIncidentActivity(id, {
          page,
          pageSize: ACTIVITY_PAGE_SIZE,
        });

        setActivityItems((prev) =>
          page === 1 ? data.items : [...prev, ...data.items]
        );
        setActivityPage(data.page);
        setActivityTotalCount(data.totalCount);
      } catch (error) {
        if (isUnauthorizedError(error)) {
          handleUnauthorized();
          return;
        }

        setActivityError(
          getErrorMessage(error, "Unable to load activity history.")
        );
      } finally {
        if (page === 1) {
          setActivityLoading(false);
        } else {
          setActivityLoadingMore(false);
        }
      }
    },
    [handleUnauthorized, id]
  );

  const refreshIncidentData = useCallback(async () => {
    if (!id) {
      return;
    }

    const [incidentData, commentData] = await Promise.all([
      getIncident(id),
      getIncidentComments(id),
    ]);

    setIncident(incidentData);
    setComments(commentData);
    setSelectedTechnicianId(incidentData.assignedTo?.id ?? "");
  }, [id]);

  const refreshIncidentAndActivity = useCallback(async () => {
    await Promise.all([
      refreshIncidentData(),
      loadActivity(1),
    ]);
  }, [loadActivity, refreshIncidentData]);

  const loadAIAnalyses = useCallback(async () => {
    if (!id) {
      return;
    }

    setAiLoading(true);

    try {
      const data = await getIncidentAIAnalyses(id);
      setAiAnalyses(data);
    } catch (error) {
      if (isUnauthorizedError(error)) {
        handleUnauthorized();
        return;
      }
      // Silently skip forbidden (Employee may not see AI history per RBAC)
    } finally {
      setAiLoading(false);
    }
  }, [handleUnauthorized, id]);

  const loadResolutionAnalyses = useCallback(async () => {
    if (!id || !user) {
      return;
    }

    if (user.role === "Employee") {
      return;
    }

    setResolutionLoading(true);

    try {
      const data = await getIncidentAIResolutionAnalyses(id);
      setResolutionAnalyses(data);
    } catch (error) {
      if (isUnauthorizedError(error)) {
        handleUnauthorized();
        return;
      }
      // Silently skip forbidden for unauthorized roles/technicians
    } finally {
      setResolutionLoading(false);
    }
  }, [handleUnauthorized, id, user]);

  const loadIncident = useCallback(async () => {
    if (!id) {
      setError({
        title: "Incident not found",
        message: "Incident not found.",
      });
      setLoading(false);
      return;
    }

    setLoading(true);

    try {
      setError(null);
      setWorkflowNotice(null);
      setCommentNotice(null);

      await refreshIncidentAndActivity();
    } catch (error) {
      if (isUnauthorizedError(error)) {
        handleUnauthorized();
        return;
      }

      if (isForbiddenError(error)) {
        setError({
          title: "Access denied",
          message: "You do not have access to this incident.",
        });
        return;
      }

      if (isNotFoundError(error)) {
        setError({
          title: "Incident not found",
          message: "Incident not found.",
        });
        return;
      }

      setError({
        title: "Unable to load incident",
        message: getErrorMessage(
          error,
          "Unable to load incident details."
        ),
      });
    } finally {
      setLoading(false);
    }
  }, [handleUnauthorized, id, refreshIncidentAndActivity]);

  useEffect(() => {
    if (!user) {
      handleUnauthorized();
      return;
    }

    void Promise.resolve().then(loadIncident);
    void Promise.resolve().then(loadAIAnalyses);
    void Promise.resolve().then(loadResolutionAnalyses);
  }, [handleUnauthorized, loadIncident, loadAIAnalyses, loadResolutionAnalyses, user]);

  useEffect(() => {
    if (!user || !isManagerOrAdmin(user.role)) {
      return;
    }

    let isActive = true;

    async function loadTechnicians() {
      setLoadingTechnicians(true);

      try {
        const data = await getTechnicians();

        if (isActive) {
          setTechnicians(data);
        }
      } catch (error) {
        if (isUnauthorizedError(error)) {
          handleUnauthorized();
          return;
        }

        if (isActive) {
          setWorkflowNotice({
            tone: "error",
            message: getErrorMessage(
              error,
              "Unable to load technicians."
            ),
          });
        }
      } finally {
        if (isActive) {
          setLoadingTechnicians(false);
        }
      }
    }

    void loadTechnicians();

    return () => {
      isActive = false;
    };
  }, [handleUnauthorized, user]);

  function logout() {
    sessionStorage.clear();
    navigate("/", { replace: true });
  }

  async function handleStatusUpdate(
    status: IncidentStatus,
    customResolution?: string,
    closureReason?: string
  ) {
    if (!id) {
      return;
    }

    if (status === "Resolved" && !customResolution) {
      setShowResolveModal(true);
      setResolutionText("");
      setResolveError("");
      return;
    }

    if (status === "Closed" && incident?.status !== "Resolved" && !closureReason) {
      setShowAdminCloseModal(true);
      setAdminCloseReason("");
      setAdminCloseError("");
      return;
    }

    setActionBusy(status);
    setWorkflowNotice(null);

    try {
      await updateIncidentStatus(id, status, customResolution, closureReason);
      await refreshIncidentAndActivity();

      setWorkflowNotice({
        tone: "success",
        message: status === "Closed" && incident?.status !== "Resolved"
          ? "Incident administratively closed."
          : `Status updated to ${formatStatusLabel(status)}.`,
      });
    } catch (error) {
      if (isUnauthorizedError(error)) {
        handleUnauthorized();
        return;
      }

      setWorkflowNotice({
        tone: "error",
        message: getErrorMessage(
          error,
          "Unable to update incident status."
        ),
      });
    } finally {
      setActionBusy(null);
    }
  }

  async function handleConfirmResolve() {
    if (!resolutionText.trim() || resolutionText.trim().length < 5) {
      setResolveError("A resolution description (at least 5 characters) is required.");
      return;
    }
    const note = resolutionText.trim();
    setShowResolveModal(false);
    await handleStatusUpdate("Resolved", note);
  }

  async function handleConfirmAdminClose() {
    if (!adminCloseReason.trim() || adminCloseReason.trim().length < 3) {
      setAdminCloseError("A closure reason (at least 3 characters) is required.");
      return;
    }
    const reason = adminCloseReason.trim();
    setShowAdminCloseModal(false);
    await handleStatusUpdate("Closed", undefined, reason);
  }

  async function handleAssignTechnician(
    event: FormEvent<HTMLFormElement>
  ) {
    event.preventDefault();

    if (!id) {
      return;
    }

    if (!selectedTechnicianId) {
      setWorkflowNotice({
        tone: "error",
        message: "Select a technician before assigning.",
      });
      return;
    }

    setActionBusy("assign");
    setWorkflowNotice(null);

    try {
      const result = await assignIncident(id, selectedTechnicianId);

      await refreshIncidentAndActivity();

      setWorkflowNotice({
        tone: "success",
        message: `${result.assignmentType} to ${result.assignedTo.name}.`,
      });
    } catch (error) {
      if (isUnauthorizedError(error)) {
        handleUnauthorized();
        return;
      }

      setWorkflowNotice({
        tone: "error",
        message: getErrorMessage(
          error,
          "Unable to assign technician."
        ),
      });
    } finally {
      setActionBusy(null);
    }
  }

  async function handlePostComment(
    event: FormEvent<HTMLFormElement>
  ) {
    event.preventDefault();

    if (!id) {
      return;
    }

    const trimmedComment = commentText.trim();

    if (!trimmedComment) {
      setCommentNotice({
        tone: "error",
        message: "Comment cannot be empty.",
      });
      return;
    }

    setPostingComment(true);
    setCommentNotice(null);

    try {
      await addIncidentComment(id, trimmedComment);
      await refreshIncidentAndActivity();

      setCommentText("");
    } catch (error) {
      if (isUnauthorizedError(error)) {
        handleUnauthorized();
        return;
      }

      setCommentNotice({
        tone: "error",
        message: getErrorMessage(error, "Unable to post comment."),
      });
    } finally {
      setPostingComment(false);
    }
  }

  async function handleRunAIAnalysis() {
    if (!id) {
      return;
    }

    setRunningAnalysis(true);
    setAiNotice(null);

    try {
      const result = await runIncidentAIAnalysis(id);
      setAiAnalyses((prev) => [result, ...prev]);
      await loadActivity(1);
      setAiNotice({
        tone: "success",
        message: "AI analysis complete. Review recommendations below.",
      });
    } catch (error) {
      if (isUnauthorizedError(error)) {
        handleUnauthorized();
        return;
      }

      setAiNotice({
        tone: "error",
        message:
          error instanceof Error && error.message.trim()
            ? error.message
            : "AI triage analysis is temporarily unavailable. Please try again.",
      });
    } finally {
      setRunningAnalysis(false);
    }
  }

  async function handleApplyRecommendation(
    target: "category" | "priority" | "both"
  ) {
    if (!id || aiAnalyses.length === 0) {
      return;
    }

    const latest = aiAnalyses[0];
    setApplyingRecommendation(target);
    setAiNotice(null);
    setConfirmApplyTarget(null);

    const applyCategory = target === "category" || target === "both";
    const applyPriority = target === "priority" || target === "both";

    try {
      await applyAIRecommendation(id, latest.id, {
        applyCategory,
        applyPriority,
      });

      await refreshIncidentAndActivity();
      await loadAIAnalyses();

      let successText = "AI recommendation applied successfully.";
      if (target === "category") {
        successText = "AI category recommendation applied successfully.";
      } else if (target === "priority") {
        successText = "AI priority recommendation applied successfully.";
      } else if (target === "both") {
        successText =
          "AI category and priority recommendations applied successfully.";
      }

      setAiNotice({
        tone: "success",
        message: successText,
      });
    } catch (error) {
      if (isUnauthorizedError(error)) {
        handleUnauthorized();
        return;
      }

      setAiNotice({
        tone: "error",
        message: getErrorMessage(
          error,
          "Unable to apply AI recommendation."
        ),
      });
    } finally {
      setApplyingRecommendation(null);
    }
  }

  async function handleRunResolutionAssistant() {
    if (!id) {
      return;
    }

    setRunningResolutionAssistant(true);
    setResolutionNotice(null);

    try {
      const result = await generateIncidentAIResolutionAnalysis(id);
      setResolutionAnalyses((prev) => [result, ...prev]);
      await loadActivity(1);
      setResolutionNotice({
        tone: "success",
        message: result.hasSufficientEvidence
          ? "Resolution assistance generated with historical evidence."
          : "General resolution guidance generated (insufficient historical matches found).",
      });
    } catch (error) {
      if (isUnauthorizedError(error)) {
        handleUnauthorized();
        return;
      }

      setResolutionNotice({
        tone: "error",
        message:
          error instanceof Error && error.message.trim()
            ? error.message
            : "AI resolution assistance is temporarily unavailable. Please try again.",
      });
    } finally {
      setRunningResolutionAssistant(false);
    }
  }

  function handleCopySteps(steps: SuggestedResolutionStep[]) {
    if (!steps || steps.length === 0) {
      return;
    }

    const text = steps
      .map(
        (s) =>
          `${s.stepNumber}. ${s.action}\n   Reason: ${s.reason}${
            s.evidenceIncidentNumbers && s.evidenceIncidentNumbers.length > 0
              ? `\n   Evidence: ${s.evidenceIncidentNumbers.join(", ")}`
              : ""
          }`
      )
      .join("\n\n");

    void navigator.clipboard.writeText(text);
    setCopiedSteps(true);
    setTimeout(() => setCopiedSteps(false), 2500);
  }

  const statusActions = useMemo(() => {
    if (!incident || !user) {
      return [];
    }

    return getStatusActions(incident, user);
  }, [incident, user]);

  const canManageAssignment = Boolean(
    incident &&
      user &&
      isManagerOrAdmin(user.role) &&
      (incident.status === "Triaged" ||
        incident.status === "Assigned")
  );

  const canPostComment = Boolean(
    incident && user && canCommentOnIncident(user, incident)
  );

  const hasMoreActivity = activityItems.length < activityTotalCount;

  return (
    <div className="app-shell">
      <Sidebar
        currentTab="incidents"
        user={user}
        onLogout={logout}
      />

      <main className="main-content">
        <div className="content-inner">
          <section className="incident-details-page">
            <Link className="back-link" to="/incidents">
              ← Back to Incidents
            </Link>

            {loading && (
              <div className="incident-details-skeleton" aria-busy="true">
                <span className="sr-only">Loading incident details...</span>

                {/* Details Header Placeholder */}
                <header className="details-header" style={{ marginBottom: 18 }}>
                  <div style={{ flex: 1 }}>
                    <Skeleton width={100} height={16} style={{ marginBottom: 10 }} />
                    <Skeleton variant="heading" width="60%" height={32} />
                  </div>
                  <div className="details-badges">
                    <Skeleton variant="badge" width={80} height={26} />
                    <Skeleton variant="badge" width={75} height={26} />
                  </div>
                </header>

                <div className="details-grid">
                  {/* Workflow Card Placeholder */}
                  <section className="detail-card detail-card-wide workflow-card">
                    <div className="workflow-header">
                      <div>
                        <Skeleton width={80} height={18} style={{ marginBottom: 6 }} />
                        <Skeleton width={110} height={14} />
                      </div>
                    </div>
                    <div className="workflow-layout">
                      <div className="workflow-block">
                        <Skeleton width={90} height={12} style={{ marginBottom: 10 }} />
                        <div className="workflow-actions">
                          <Skeleton variant="button" width={110} height={38} />
                          <Skeleton variant="button" width={110} height={38} />
                        </div>
                      </div>
                      <div style={{ width: "240px" }}>
                        <Skeleton width={120} height={12} style={{ marginBottom: 8 }} />
                        <Skeleton variant="button" width="100%" height={42} />
                      </div>
                    </div>
                  </section>

                  {/* AI Analysis Panel Placeholder */}
                  <section className="detail-card detail-card-wide ai-panel">
                    <div className="ai-panel-header">
                      <div className="ai-panel-title">
                        <span className="ai-panel-icon" aria-hidden="true">✦</span>
                        <Skeleton width={160} height={20} />
                      </div>
                      <Skeleton variant="button" width={130} height={36} />
                    </div>
                    <div style={{ padding: "16px 0 6px" }}>
                      <SkeletonText lines={3} lastLineWidth="50%" />
                    </div>
                  </section>

                  {/* Description Card Placeholder */}
                  <section className="detail-card detail-card-wide">
                    <Skeleton width={100} height={18} style={{ marginBottom: 16 }} />
                    <SkeletonText lines={4} lastLineWidth="70%" lineHeight={15} gap={10} />
                  </section>

                  {/* Incident Details Card Placeholder */}
                  <section className="detail-card">
                    <Skeleton width={120} height={18} style={{ marginBottom: 16 }} />
                    <dl className="detail-list">
                      <div>
                        <Skeleton width={60} height={12} />
                        <Skeleton width={120} height={16} />
                      </div>
                      <div>
                        <Skeleton width={50} height={12} />
                        <Skeleton width={100} height={16} />
                      </div>
                      <div>
                        <Skeleton width={50} height={12} />
                        <Skeleton width={90} height={16} />
                      </div>
                    </dl>
                  </section>

                  {/* People Card Placeholder */}
                  <section className="detail-card">
                    <Skeleton width={70} height={18} style={{ marginBottom: 16 }} />
                    <div className="person-stack">
                      <div className="person-block">
                        <Skeleton width={60} height={11} style={{ marginBottom: 4 }} />
                        <Skeleton width={140} height={16} style={{ marginBottom: 4 }} />
                        <Skeleton width={170} height={13} />
                      </div>
                      <div className="person-block">
                        <Skeleton width={110} height={11} style={{ marginBottom: 4 }} />
                        <Skeleton width={130} height={16} style={{ marginBottom: 4 }} />
                        <Skeleton width={160} height={13} />
                      </div>
                    </div>
                  </section>

                  {/* SLA Card Placeholder */}
                  <section className="detail-card detail-card-wide sla-detail-card">
                    <div className="sla-card-header">
                      <div>
                        <Skeleton width={230} height={18} style={{ marginBottom: 6 }} />
                        <Skeleton width={320} height={13} />
                      </div>
                      <Skeleton variant="badge" width={90} height={26} />
                    </div>
                    <div className="sla-targets-grid" style={{ marginTop: 16 }}>
                      <div className="sla-target-card">
                        <Skeleton width={110} height={14} style={{ marginBottom: 12 }} />
                        <Skeleton width="90%" height={14} style={{ marginBottom: 8 }} />
                        <Skeleton width="70%" height={14} />
                      </div>
                      <div className="sla-target-card">
                        <Skeleton width={110} height={14} style={{ marginBottom: 12 }} />
                        <Skeleton width="90%" height={14} style={{ marginBottom: 8 }} />
                        <Skeleton width="70%" height={14} />
                      </div>
                    </div>
                  </section>

                  {/* Resolution Card Placeholder */}
                  <section className="detail-card detail-card-wide">
                    <Skeleton width={90} height={18} style={{ marginBottom: 16 }} />
                    <SkeletonText lines={2} lastLineWidth="40%" />
                  </section>

                  {/* Activity History Placeholder */}
                  <section className="detail-card detail-card-wide">
                    <Skeleton width={140} height={18} style={{ marginBottom: 16 }} />
                    <div className="activity-list activity-list-skeleton">
                      {Array.from({ length: 3 }).map((_, i) => (
                        <div className="activity-item skeleton-activity-item" key={i}>
                          <Skeleton variant="circle" width={32} height={32} />
                          <div className="activity-content">
                            <Skeleton width="42%" height={15} style={{ marginBottom: 8 }} />
                            <Skeleton width="65%" height={13} style={{ marginBottom: 7 }} />
                            <Skeleton width="34%" height={12} />
                          </div>
                        </div>
                      ))}
                    </div>
                  </section>

                  {/* Conversation Card Placeholder */}
                  <section className="detail-card detail-card-wide conversation-card">
                    <div className="conversation-header">
                      <Skeleton width={110} height={18} />
                      <Skeleton variant="pill" width={22} height={18} />
                    </div>
                    <div style={{ display: "grid", gap: 14 }}>
                      <div style={{ padding: 14, background: "var(--surface-muted)", borderRadius: 8 }}>
                        <Skeleton width={140} height={14} style={{ marginBottom: 8 }} />
                        <SkeletonText lines={2} lastLineWidth="60%" />
                      </div>
                      <div style={{ padding: 14, background: "var(--surface-muted)", borderRadius: 8 }}>
                        <Skeleton width={160} height={14} style={{ marginBottom: 8 }} />
                        <SkeletonText lines={2} lastLineWidth="45%" />
                      </div>
                    </div>
                    <div style={{ marginTop: 12 }}>
                      <Skeleton width="100%" height={90} borderRadius={8} style={{ marginBottom: 10 }} />
                      <div style={{ display: "flex", justifyContent: "flex-end" }}>
                        <Skeleton variant="button" width={120} height={38} />
                      </div>
                    </div>
                  </section>
                </div>
              </div>
            )}

            {!loading && error && (
              <div className="details-state">
                <h1>{error.title}</h1>
                <p>{error.message}</p>
              </div>
            )}

            {!loading && !error && incident && (
              <>
                <header className="details-header">
                  <div>
                    <span className="details-incident-number">
                      {incident.incidentNumber}
                    </span>

                    <h1>{incident.title}</h1>
                  </div>

                  <div className="details-badges">
                    <span
                      className={`badge ${getBadgeClass(
                        "status",
                        incident.status
                      )}`}
                    >
                      {formatStatusLabel(incident.status)}
                    </span>

                    <span
                      className={`badge ${getBadgeClass(
                        "priority",
                        incident.priority.name
                      )}`}
                    >
                      {incident.priority.name}
                    </span>
                  </div>
                </header>

                {incident.sla?.requiresEscalation && (
                  <div className="escalation-banner" role="alert">
                    <div className="escalation-banner-icon" aria-hidden="true">
                      ⚠
                    </div>
                    <div className="escalation-banner-content">
                      <strong>SLA Breached — Escalation Required</strong>
                      <span>
                        This incident has breached its agreed service level targets
                        {incident.sla.responseBreached && incident.sla.resolutionBreached
                          ? " (both response and resolution deadlines exceeded)"
                          : incident.sla.responseBreached
                            ? " (response deadline exceeded)"
                            : " (resolution deadline exceeded)"}
                        . Priority management oversight and remediation are required.
                      </span>
                    </div>
                  </div>
                )}

                <div className="details-grid">
                  <section className="detail-card detail-card-wide workflow-card">
                    <div className="workflow-header">
                      <div>
                        <h2>Workflow</h2>
                        <p>{formatStatusLabel(incident.status)}</p>
                      </div>

                      {incident.assignedTo && (
                        <span className="workflow-assignee">
                          {incident.assignedTo.name}
                        </span>
                      )}
                    </div>

                    {workflowNotice && (
                      <div
                        className={`inline-notice ${workflowNotice.tone}`}
                      >
                        {workflowNotice.message}
                      </div>
                    )}

                    <div className="workflow-layout">
                      <div className="workflow-block">
                        <span className="workflow-label">
                          Next Actions
                        </span>

                        {statusActions.length > 0 ? (
                          <div className="workflow-actions">
                            {statusActions.map((action) => (
                              <button
                                key={action.status}
                                type="button"
                                className={`workflow-button ${
                                  action.tone ?? "primary"
                                }`}
                                disabled={actionBusy !== null}
                                onClick={() =>
                                  void handleStatusUpdate(action.status)
                                }
                              >
                                {actionBusy === action.status
                                  ? "Updating..."
                                  : action.label}
                              </button>
                            ))}
                          </div>
                        ) : (
                          <p className="muted-copy">
                            No status actions available.
                          </p>
                        )}
                      </div>

                      {canManageAssignment ? (
                        <form
                          className="assignment-control"
                          onSubmit={(event) =>
                            void handleAssignTechnician(event)
                          }
                        >
                          <label
                            className="form-field assignment-select-field"
                            htmlFor="assigned-technician"
                          >
                            <span>Assigned Technician</span>

                            <select
                              id="assigned-technician"
                              value={selectedTechnicianId}
                              disabled={
                                loadingTechnicians ||
                                actionBusy !== null
                              }
                              onChange={(event) =>
                                setSelectedTechnicianId(
                                  event.target.value
                                )
                              }
                            >
                              <option value="">
                                Select technician
                              </option>

                              {technicians.map((technician) => (
                                <option
                                  key={technician.id}
                                  value={technician.id}
                                >
                                  {technician.firstName}{" "}
                                  {technician.lastName}
                                </option>
                              ))}
                            </select>
                          </label>

                          <button
                            type="submit"
                            className="workflow-button secondary"
                            disabled={
                              !selectedTechnicianId ||
                              loadingTechnicians ||
                              actionBusy !== null
                            }
                          >
                            {actionBusy === "assign"
                              ? "Assigning..."
                              : incident.assignedTo
                                ? "Reassign"
                                : "Assign"}
                          </button>
                        </form>
                      ) : (
                        isManagerOrAdmin(user?.role) && (
                          <p className="muted-copy">
                            Assignment locked at this status.
                          </p>
                        )
                      )}
                    </div>
                  </section>

                  <section className="detail-card detail-card-wide">
                    <h2>Description</h2>
                    <p className="detail-body">
                      {incident.description ||
                        "No description provided."}
                    </p>
                  </section>

                  {incident.resolution && (
                    <section className="detail-card detail-card-wide resolution-card">
                      <div className="resolution-card-header">
                        <span className="resolution-icon" aria-hidden="true">✓</span>
                        <h2>Resolution Details</h2>
                      </div>
                      <p className="detail-body">{incident.resolution}</p>
                    </section>
                  )}

                  <section className="detail-card">
                    <h2>Incident Details</h2>

                    <dl className="detail-list">
                      <div>
                        <dt>Category</dt>
                        <dd>{incident.category}</dd>
                      </div>

                      <div>
                        <dt>Priority</dt>
                        <dd>
                          {incident.priority.name}
                          <span>
                            Level {incident.priority.level}
                          </span>
                        </dd>
                      </div>

                      <div>
                        <dt>Status</dt>
                        <dd>{formatStatusLabel(incident.status)}</dd>
                      </div>
                    </dl>
                  </section>

                  <section className="detail-card">
                    <h2>People</h2>

                    <div className="person-stack">
                      <div className="person-block">
                        <span>Reporter</span>
                        <strong>{incident.reporter.name}</strong>
                        <span className="person-email">
                          {incident.reporter.email}
                        </span>
                      </div>

                      <div className="person-block">
                        <span>Assigned Technician</span>

                        {incident.assignedTo ? (
                          <>
                            <strong>{incident.assignedTo.name}</strong>
                            <span className="person-email">
                              {incident.assignedTo.email}
                            </span>
                          </>
                        ) : (
                          <strong>Unassigned</strong>
                        )}
                      </div>
                    </div>
                  </section>

                  <section className="detail-card detail-card-wide sla-detail-card">
                    <div className="sla-card-header">
                      <div>
                        <h2>Service Level Agreement (SLA)</h2>
                        <p>
                          Target response and resolution times based on priority ({incident.priority.name})
                        </p>
                      </div>

                      {incident.sla && (
                        <span
                          className={`badge sla-badge ${getSlaBadgeClass(
                            incident.sla.overallStatus
                          )}`}
                        >
                          Overall: {formatSlaStatus(incident.sla.overallStatus)}
                        </span>
                      )}
                    </div>

                    {incident.sla ? (
                      <div className="sla-grid">
                        <div className="sla-target-block">
                          <div className="sla-target-title">
                            <span>Response SLA</span>
                            <span
                              className={`badge sla-badge ${getSlaBadgeClass(
                                incident.sla.responseStatus
                              )}`}
                            >
                              {formatSlaStatus(incident.sla.responseStatus)}
                            </span>
                          </div>

                          <dl className="sla-meta-list">
                            <div>
                              <dt>Target Window</dt>
                              <dd>
                                {formatTargetDuration(
                                  incident.sla.responseTargetMinutes
                                )}
                              </dd>
                            </div>

                            <div>
                              <dt>Due By</dt>
                              <dd>{formatDate(incident.sla.responseDueAt)}</dd>
                            </div>

                            <div>
                              <dt>Responded At</dt>
                              <dd>
                                {incident.sla.firstRespondedAt
                                  ? formatDate(incident.sla.firstRespondedAt)
                                  : "Pending (awaiting triage)"}
                              </dd>
                            </div>

                            <div>
                              <dt>Status Detail</dt>
                              <dd
                                className={
                                  incident.sla.responseBreached
                                    ? "text-danger"
                                    : ""
                                }
                              >
                                {getResponseStatusDescription(incident.sla)}
                              </dd>
                            </div>
                          </dl>
                        </div>

                        <div className="sla-target-block">
                          <div className="sla-target-title">
                            <span>Resolution SLA</span>
                            <span
                              className={`badge sla-badge ${getSlaBadgeClass(
                                incident.sla.resolutionStatus
                              )}`}
                            >
                              {formatSlaStatus(incident.sla.resolutionStatus)}
                            </span>
                          </div>

                          <dl className="sla-meta-list">
                            <div>
                              <dt>Target Window</dt>
                              <dd>
                                {formatTargetDuration(
                                  incident.sla.resolutionTargetMinutes
                                )}
                              </dd>
                            </div>

                            <div>
                              <dt>Due By</dt>
                              <dd>{formatDate(incident.sla.resolutionDueAt)}</dd>
                            </div>

                            <div>
                              <dt>Resolved At</dt>
                              <dd>
                                {incident.sla.resolvedAt
                                  ? formatDate(incident.sla.resolvedAt)
                                  : "In Progress"}
                              </dd>
                            </div>

                            <div>
                              <dt>Status Detail</dt>
                              <dd
                                className={
                                  incident.sla.resolutionBreached
                                    ? "text-danger"
                                    : ""
                                }
                              >
                                {getResolutionStatusDescription(incident.sla)}
                              </dd>
                            </div>
                          </dl>
                        </div>
                      </div>
                    ) : (
                      <p className="muted-copy">
                        No SLA configuration available.
                      </p>
                    )}
                  </section>

                  <section className="detail-card detail-card-wide">
                    <h2>Resolution</h2>
                    <p className="detail-body">
                      {incident.resolution ||
                        "No resolution recorded yet."}
                    </p>
                  </section>

                  <section className="detail-card detail-card-wide activity-card">
                    <div className="activity-header">
                      <h2>Activity History</h2>
                      <span>{activityTotalCount}</span>
                    </div>

                    {activityError && (
                      <div className="inline-notice error" role="alert">
                        {activityError}
                      </div>
                    )}

                    {activityLoading ? (
                      <div className="activity-list activity-list-skeleton">
                        {Array.from({ length: 3 }).map((_, index) => (
                          <div
                            className="activity-item skeleton-activity-item"
                            key={index}
                          >
                            <Skeleton variant="circle" width={32} height={32} />
                            <div className="activity-content">
                              <Skeleton
                                width="42%"
                                height={15}
                                style={{ marginBottom: 8 }}
                              />
                              <Skeleton
                                width="65%"
                                height={13}
                                style={{ marginBottom: 7 }}
                              />
                              <Skeleton width="34%" height={12} />
                            </div>
                          </div>
                        ))}
                      </div>
                    ) : activityItems.length === 0 ? (
                      <div className="activity-empty">
                        No activity has been recorded for this incident yet.
                      </div>
                    ) : (
                      <>
                        <ol className="activity-list">
                          {activityItems.map((activity) => {
                            const metadata = getActivityMetadata(activity);
                            const changes = getActivityChanges(activity);
                            const slaDetail = getActivitySlaDetail(activity);

                            return (
                              <li
                                className={`activity-item ${getActivityTone(
                                  activity.eventType
                                )}`}
                                key={activity.id}
                              >
                                <span
                                  className="activity-marker"
                                  aria-hidden="true"
                                >
                                  {getActivityMarker(activity.eventType)}
                                </span>

                                <div className="activity-content">
                                  <h3>{activity.summary}</h3>

                                  {changes.length > 0 && (
                                    <div className="activity-change-list">
                                      {changes.map((change, index) => (
                                        <div
                                          className="activity-change-row"
                                          key={`${activity.id}-${index}`}
                                        >
                                          {change.field && (
                                            <span className="activity-change-field">
                                              {change.field}
                                            </span>
                                          )}
                                          <span className="activity-change-value">
                                            {change.oldValue || "Not recorded"}
                                          </span>
                                          <span
                                            className="activity-change-arrow"
                                            aria-label="changed to"
                                          >
                                            -&gt;
                                          </span>
                                          <span className="activity-change-value strong">
                                            {change.newValue || "Not recorded"}
                                          </span>
                                        </div>
                                      ))}
                                    </div>
                                  )}

                                  {slaDetail && (
                                    <p className="activity-detail-line">
                                      {slaDetail}
                                    </p>
                                  )}

                                  {metadata?.reason && (
                                    <p className="activity-reason">
                                      <strong>Reason:</strong> {metadata.reason}
                                    </p>
                                  )}

                                  <p className="activity-meta">
                                    {getActivityActorLabel(activity)} -{" "}
                                    {formatDate(activity.createdAt)}
                                  </p>
                                </div>
                              </li>
                            );
                          })}
                        </ol>

                        {hasMoreActivity && (
                          <button
                            type="button"
                            className="activity-load-more"
                            disabled={activityLoadingMore}
                            onClick={() =>
                              void loadActivity(activityPage + 1)
                            }
                          >
                            {activityLoadingMore
                              ? "Loading..."
                              : "Load more"}
                          </button>
                        )}
                      </>
                    )}
                  </section>

                  <section className="detail-card detail-card-wide conversation-card">
                    <div className="conversation-header">
                      <h2>Conversation</h2>
                      <span>{comments.length}</span>
                    </div>

                    {commentNotice && (
                      <div
                        className={`inline-notice ${commentNotice.tone}`}
                      >
                        {commentNotice.message}
                      </div>
                    )}

                    {comments.length === 0 ? (
                      <div className="comments-empty">
                        No comments yet.
                      </div>
                    ) : (
                      <ol className="comment-list">
                        {comments.map((comment) => (
                          <li
                            className="comment-item"
                            key={comment.id}
                          >
                            <div className="comment-meta">
                              <strong>
                                {comment.author.name}
                              </strong>
                              <span>
                                {comment.author.role} -{" "}
                                {formatDate(comment.createdAt)}
                              </span>
                            </div>

                            <p>{comment.comment}</p>
                          </li>
                        ))}
                      </ol>
                    )}

                    {canPostComment ? (
                      <form
                        className="comment-composer"
                        onSubmit={(event) =>
                          void handlePostComment(event)
                        }
                      >
                        <label
                          className="form-field"
                          htmlFor="incident-comment"
                        >
                          <span>Add Comment</span>

                          <textarea
                            id="incident-comment"
                            placeholder="Add a comment..."
                            value={commentText}
                            maxLength={4000}
                            disabled={postingComment}
                            onChange={(event) =>
                              setCommentText(event.target.value)
                            }
                          />
                        </label>

                        <div className="comment-composer-actions">
                          <span>{commentText.trim().length}/4000</span>

                          <button
                            type="submit"
                            className="workflow-button primary"
                            disabled={
                              postingComment ||
                              commentText.trim().length === 0
                            }
                          >
                            {postingComment
                              ? "Posting..."
                              : "Post Comment"}
                          </button>
                        </div>
                      </form>
                    ) : (
                      <p className="muted-copy">
                        Comments are unavailable for this incident.
                      </p>
                    )}
                  </section>

                  {/* ── AI Analysis Panel (Phase 1) ── */}
                  <section className="detail-card detail-card-wide ai-panel" id="ai-triage-panel">
                    <div className="ai-panel-header">
                      <div className="ai-panel-title">
                        <span className="ai-panel-icon" aria-hidden="true">
                          ✦
                        </span>
                        <div>
                          <h2>AI Incident Triage &amp; Classification</h2>
                          <p className="ai-panel-subtitle">
                            Initial categorization, priority assessment, and triage recommendations for service desk dispatch.
                          </p>
                        </div>
                      </div>

                      {user && canRunAIAnalysis(user, incident) && (
                        <button
                          id="run-ai-analysis-btn"
                          type="button"
                          className="ai-run-button"
                          disabled={runningAnalysis || aiLoading}
                          onClick={() => void handleRunAIAnalysis()}
                        >
                          {runningAnalysis
                            ? "Analyzing Triage…"
                            : "Run Triage Analysis"}
                        </button>
                      )}
                    </div>

                    {aiNotice && (
                      <div
                        className={`inline-notice ${aiNotice.tone}`}
                        role="status"
                      >
                        {aiNotice.message}
                      </div>
                    )}

                    {runningAnalysis && (
                      <div className="ai-running">
                        <span className="ai-spinner" aria-hidden="true" />
                        Running AI analysis — this may take a few seconds…
                      </div>
                    )}

                    {!runningAnalysis && aiLoading && (
                      <div className="ai-empty">Loading analysis history…</div>
                    )}

                    {!runningAnalysis && !aiLoading && aiAnalyses.length === 0 && (
                      <div className="ai-empty">
                        No analysis yet.
                        {user && canRunAIAnalysis(user, incident)
                          ? " Click \"Analyze with AI\" to generate recommendations."
                          : ""}
                      </div>
                    )}

                    {!runningAnalysis && aiAnalyses.length > 0 && (() => {
                      const latest = aiAnalyses[0];
                      const isManagerOrAdminUser = Boolean(
                        user && isManagerOrAdmin(user.role)
                      );
                      const isCategoryMatchingCurrent =
                        latest.categoryRecommendation.trim().toLowerCase() ===
                        incident.category.trim().toLowerCase();
                      const isPriorityMatchingCurrent =
                        latest.priorityRecommendation.trim().toLowerCase() ===
                        incident.priority.name.trim().toLowerCase();


                      return (
                        <>
                          {/* Context snapshot */}
                          <div className="ai-context-row">
                            <div className="ai-context-block">
                              <span className="ai-context-label">
                                Current category
                              </span>
                              <span className="ai-context-value">
                                {incident.category}
                              </span>
                            </div>

                            <div className="ai-context-block">
                              <span className="ai-context-label">
                                Current priority
                              </span>
                              <span className="ai-context-value">
                                {incident.priority.name}
                              </span>
                            </div>
                          </div>

                          {/* Recommendations grid */}
                          <div className="ai-reco-grid">
                            <div className="ai-reco-card">
                              <span className="ai-reco-label">Category</span>
                              <span className="ai-reco-value">
                                {latest.categoryRecommendation}
                              </span>

                              <div className="ai-reco-action-row">
                                {latest.categoryApplied ? (
                                  <div className="ai-applied-group">
                                    <span className="ai-applied-badge">
                                      <span className="check-icon" aria-hidden="true">✓</span> Approved & Applied
                                    </span>
                                    {latest.appliedByUser?.name && (
                                      <span className="ai-applied-meta">
                                        Approved by {latest.appliedByUser.name}
                                      </span>
                                    )}
                                    {latest.appliedAt && (
                                      <span className="ai-applied-meta">
                                        Applied {formatDate(latest.appliedAt)}
                                      </span>
                                    )}
                                  </div>
                                ) : isCategoryMatchingCurrent ? (
                                  <span className="ai-matches-notice">
                                    Already matches current value
                                  </span>
                                ) : isManagerOrAdminUser ? (
                                  <button
                                    id="apply-category-btn"
                                    type="button"
                                    className="ai-apply-button"
                                    disabled={applyingRecommendation !== null}
                                    onClick={() =>
                                      setConfirmApplyTarget("category")
                                    }
                                  >
                                    {applyingRecommendation === "category"
                                      ? "Applying…"
                                      : "Apply Category"}
                                  </button>
                                ) : null}
                              </div>
                            </div>

                            <div className="ai-reco-card">
                              <span className="ai-reco-label">Priority</span>
                              <span
                                className={`ai-reco-value priority-chip ${getBadgeClass(
                                  "priority",
                                  latest.priorityRecommendation
                                )}`}
                              >
                                {latest.priorityRecommendation}
                              </span>

                              <div className="ai-reco-action-row">
                                {latest.priorityApplied ? (
                                  <div className="ai-applied-group">
                                    <span className="ai-applied-badge">
                                      <span className="check-icon" aria-hidden="true">✓</span> Approved & Applied
                                    </span>
                                    {latest.appliedByUser?.name && (
                                      <span className="ai-applied-meta">
                                        Approved by {latest.appliedByUser.name}
                                      </span>
                                    )}
                                    {latest.appliedAt && (
                                      <span className="ai-applied-meta">
                                        Applied {formatDate(latest.appliedAt)}
                                      </span>
                                    )}
                                  </div>
                                ) : isPriorityMatchingCurrent ? (
                                  <span className="ai-matches-notice">
                                    Already matches current value
                                  </span>
                                ) : isManagerOrAdminUser ? (
                                  <button
                                    id="apply-priority-btn"
                                    type="button"
                                    className="ai-apply-button"
                                    disabled={applyingRecommendation !== null}
                                    onClick={() =>
                                      setConfirmApplyTarget("priority")
                                    }
                                  >
                                    {applyingRecommendation === "priority"
                                      ? "Applying…"
                                      : "Apply Priority"}
                                  </button>
                                ) : null}
                              </div>
                            </div>

                            <div className="ai-reco-card">
                              <span className="ai-reco-label">Urgency</span>
                              <span
                                className={`ai-reco-value priority-chip ${getBadgeClass(
                                  "priority",
                                  latest.urgency
                                )}`}
                              >
                                {latest.urgency}
                              </span>
                            </div>

                            <div className="ai-reco-card">
                              <span className="ai-reco-label">Confidence</span>
                              <span className="ai-reco-value ai-confidence">
                                {formatPercent(latest.confidence)}
                                <span
                                  className="ai-confidence-bar-track"
                                  aria-hidden="true"
                                >
                                  <span
                                    className="ai-confidence-bar-fill"
                                    style={{
                                      width: formatPercent(latest.confidence),
                                    }}
                                  />
                                </span>
                              </span>
                            </div>
                          </div>

                          {/* Possible cause */}
                          <div className="ai-section">
                            <h3 className="ai-section-heading">Possible Cause</h3>
                            <p className="ai-body">{latest.possibleCause}</p>
                          </div>

                          {/* Reasoning */}
                          <div className="ai-section">
                            <h3 className="ai-section-heading">Reasoning</h3>
                            <p className="ai-body">{latest.reasoningSummary}</p>
                          </div>

                          {/* Suggested actions */}
                          <div className="ai-section">
                            <h3 className="ai-section-heading">
                              Suggested Actions
                            </h3>
                            <ol className="ai-actions-list">
                              {latest.suggestedActions.map((action, index) => (
                                <li key={index}>{action}</li>
                              ))}
                            </ol>
                          </div>

                          {/* Human-in-the-loop disclaimer */}
                          <div className="ai-disclaimer" role="note">
                            ⚠ AI-generated recommendation. Human review
                            required before applying any changes.
                          </div>

                          {/* Analysis history */}
                          {aiAnalyses.length > 1 && (
                            <details className="ai-history">
                              <summary className="ai-history-toggle">
                                Analysis history ({aiAnalyses.length - 1} older
                                {aiAnalyses.length - 1 === 1
                                  ? " run"
                                  : " runs"})
                              </summary>

                              <ol className="ai-history-list">
                                {aiAnalyses.slice(1).map((analysis) => (
                                  <li
                                    key={analysis.id}
                                    className="ai-history-item"
                                  >
                                    <div className="ai-history-meta">
                                      <span>
                                        {formatDate(analysis.createdAt)}
                                      </span>
                                      <span>
                                        {analysis.provider} /{" "}
                                        {analysis.model}
                                      </span>
                                    </div>

                                    <div className="ai-history-row">
                                      <span>
                                        Category:{" "}
                                        <strong>
                                          {analysis.categoryRecommendation}
                                        </strong>
                                      </span>
                                      <span>
                                        Priority:{" "}
                                        <strong>
                                          {analysis.priorityRecommendation}
                                        </strong>
                                      </span>
                                      <span>
                                        Confidence:{" "}
                                        <strong>
                                          {formatPercent(analysis.confidence)}
                                        </strong>
                                      </span>
                                    </div>

                                    <div className="ai-history-governance">
                                      {analysis.categoryApplied ||
                                      analysis.priorityApplied ? (
                                        <div className="ai-history-audit">
                                          <span className="ai-history-applied">
                                            ✓ Approved & Applied (
                                            {analysis.categoryApplied &&
                                            analysis.priorityApplied
                                              ? "Category & Priority"
                                              : analysis.categoryApplied
                                                ? "Category"
                                                : "Priority"}
                                            )
                                          </span>
                                          {analysis.appliedByUser?.name && (
                                            <span className="ai-history-audit-meta">
                                              Approved by {analysis.appliedByUser.name}
                                            </span>
                                          )}
                                          {analysis.appliedAt && (
                                            <span className="ai-history-audit-meta">
                                              Applied {formatDate(analysis.appliedAt)}
                                            </span>
                                          )}
                                        </div>
                                      ) : (
                                        <span className="ai-history-not-applied">
                                          Not applied
                                        </span>
                                      )}
                                    </div>
                                  </li>
                                ))}
                              </ol>
                            </details>
                          )}

                          {/* Confirmation Modal */}
                          {confirmApplyTarget && (
                            <div
                              className="modal-backdrop"
                              role="dialog"
                              aria-modal="true"
                              aria-labelledby="confirm-apply-title"
                            >
                              <div className="modal-card ai-confirm-modal">
                                <div className="modal-header">
                                  <div>
                                    <h2 id="confirm-apply-title">
                                      Apply AI Recommendation
                                    </h2>
                                    <p>
                                      Please review and confirm human acceptance
                                      of this recommendation.
                                    </p>
                                  </div>
                                  <button
                                    type="button"
                                    className="close-button"
                                    aria-label="Close"
                                    onClick={() => setConfirmApplyTarget(null)}
                                  >
                                    ✕
                                  </button>
                                </div>

                                <div className="ai-confirm-body">
                                  {(confirmApplyTarget === "category" ||
                                    confirmApplyTarget === "both") && (
                                    <div className="ai-confirm-item">
                                      <span className="ai-confirm-label">
                                        Category Recommendation
                                      </span>
                                      <div className="ai-confirm-values">
                                        <span className="ai-confirm-from">
                                          {incident.category}
                                        </span>
                                        <span className="ai-confirm-arrow">
                                          →
                                        </span>
                                        <span className="ai-confirm-to">
                                          {latest.categoryRecommendation}
                                        </span>
                                      </div>
                                    </div>
                                  )}

                                  {(confirmApplyTarget === "priority" ||
                                    confirmApplyTarget === "both") && (
                                    <div className="ai-confirm-item">
                                      <span className="ai-confirm-label">
                                        Priority Recommendation
                                      </span>
                                      <div className="ai-confirm-values">
                                        <span className="ai-confirm-from">
                                          {incident.priority.name}
                                        </span>
                                        <span className="ai-confirm-arrow">
                                          →
                                        </span>
                                        <span className="ai-confirm-to">
                                          {latest.priorityRecommendation}
                                        </span>
                                      </div>
                                    </div>
                                  )}

                                  <div className="ai-confirm-notice">
                                    Applying will update the incident's
                                    operational classification and record your
                                    account as the authorizing reviewer. The
                                    incident status will remain unchanged.
                                  </div>
                                </div>

                                <div className="modal-actions">
                                  <button
                                    type="button"
                                    className="secondary-button"
                                    disabled={applyingRecommendation !== null}
                                    onClick={() => setConfirmApplyTarget(null)}
                                  >
                                    Cancel
                                  </button>
                                  <button
                                    id="confirm-apply-submit-btn"
                                    type="button"
                                    className="primary-button"
                                    disabled={applyingRecommendation !== null}
                                    onClick={() =>
                                      void handleApplyRecommendation(
                                        confirmApplyTarget
                                      )
                                    }
                                  >
                                    {applyingRecommendation !== null
                                      ? "Applying…"
                                      : "Confirm & Apply"}
                                  </button>
                                </div>
                              </div>
                            </div>
                          )}
                        </>
                      );
                    })()}
                  </section>

                  {/* ── AI Resolution Assistant Panel (Phase 2) ── */}
                  {user && incident && canAccessResolutionAssistant(user, incident) && (
                    <section
                      className="detail-card detail-card-wide ai-panel ai-resolution-panel"
                      id="ai-resolution-assistant-card"
                    >
                      <div className="ai-panel-header">
                        <div className="ai-panel-title">
                          <span className="ai-panel-icon" aria-hidden="true">
                            🔍
                          </span>
                          <div>
                            <h2>AI Resolution Assistant (Evidence-Based)</h2>
                            <p className="ai-panel-subtitle">
                              Technical diagnostic support and step-by-step guidance derived from historical ResolveAI resolutions.
                            </p>
                          </div>
                        </div>

                        {canGenerateResolutionAssistant(user, incident) && (
                          <button
                            id="run-ai-resolution-btn"
                            type="button"
                            className="ai-run-button"
                            disabled={runningResolutionAssistant || resolutionLoading}
                            onClick={() => void handleRunResolutionAssistant()}
                          >
                            {runningResolutionAssistant
                              ? "Analyzing Evidence…"
                              : resolutionAnalyses.length > 0
                                ? "Refresh Resolution Assistance"
                                : "Generate Resolution Assistance"}
                          </button>
                        )}
                      </div>

                      {resolutionNotice && (
                        <div
                          className={`inline-notice ${resolutionNotice.tone}`}
                          role="status"
                        >
                          {resolutionNotice.message}
                        </div>
                      )}

                      {runningResolutionAssistant && (
                        <div className="ai-running">
                          <span className="ai-spinner" aria-hidden="true" />
                          Searching historical resolutions and synthesizing evidence-based recommendations…
                        </div>
                      )}

                      {!runningResolutionAssistant && resolutionLoading && (
                        <div className="ai-empty">Loading resolution assistance history…</div>
                      )}

                      {!runningResolutionAssistant && !resolutionLoading && resolutionAnalyses.length === 0 && (
                        <div className="ai-empty">
                          No resolution assistance generated yet.
                          {canGenerateResolutionAssistant(user, incident)
                            ? " Click \"Generate Resolution Assistance\" to find similar resolved incidents and recommended next steps."
                            : ""}
                        </div>
                      )}

                      {!runningResolutionAssistant && resolutionAnalyses.length > 0 && (() => {
                        const latest = resolutionAnalyses[0];
                        return (
                          <div className="ai-resolution-content">
                            {/* Current Situation & Likely Issue */}
                            <div className="ai-resolution-summary-grid">
                              <div className="ai-resolution-block">
                                <h3 className="ai-resolution-heading">Current Situation</h3>
                                <p className="ai-resolution-text">{latest.summary}</p>
                              </div>

                              <div className="ai-resolution-block">
                                <div className="ai-likely-issue-header">
                                  <h3 className="ai-resolution-heading">Likely Issue</h3>
                                  <span className={`badge badge-confidence confidence-${latest.confidence.toLowerCase()}`}>
                                    {latest.confidence} Confidence
                                  </span>
                                </div>
                                <p className="ai-resolution-text">{latest.likelyIssue}</p>
                              </div>
                            </div>

                            {/* Suggested Troubleshooting Steps */}
                            <div className="ai-section ai-steps-section">
                              <div className="ai-section-title-row">
                                <h3 className="ai-section-heading">Suggested Next Steps</h3>
                                <button
                                  id="copy-suggested-steps-btn"
                                  type="button"
                                  className="secondary-button copy-steps-button"
                                  onClick={() => handleCopySteps(latest.suggestedSteps)}
                                >
                                  {copiedSteps ? "✓ Copied" : "Copy Steps"}
                                </button>
                              </div>

                              <ol className="ai-resolution-steps-list">
                                {latest.suggestedSteps.map((step) => (
                                  <li key={step.stepNumber} className="ai-resolution-step-item">
                                    <div className="step-action-row">
                                      <span className="step-number">{step.stepNumber}</span>
                                      <div className="step-details">
                                        <p className="step-action">{step.action}</p>
                                        {step.reason && (
                                          <p className="step-reason">{step.reason}</p>
                                        )}
                                        {step.evidenceIncidentNumbers && step.evidenceIncidentNumbers.length > 0 && (
                                          <div className="step-evidence-tags">
                                            <span className="evidence-tag-label">Evidence:</span>
                                            {step.evidenceIncidentNumbers.map((num) => (
                                              <span key={num} className="evidence-pill">
                                                {num}
                                              </span>
                                            ))}
                                          </div>
                                        )}
                                      </div>
                                    </div>
                                  </li>
                                ))}
                              </ol>
                            </div>

                            {/* Similar Resolved Incidents */}
                            <div className="ai-section ai-evidence-section">
                              <h3 className="ai-section-heading">
                                Similar Resolved Incidents
                                {latest.evidence.length > 0 && ` (${latest.evidence.length})`}
                              </h3>

                              {latest.evidence.length === 0 ? (
                                <div className="ai-no-evidence-callout">
                                  <p>
                                    <strong>No sufficiently similar resolved incidents were found in ResolveAI history.</strong>
                                  </p>
                                  <p className="muted-copy">
                                    The suggested next steps above represent general diagnostic guidance, not patterns from historical incident resolutions.
                                  </p>
                                </div>
                              ) : (
                                <div className="ai-evidence-cards">
                                  {latest.evidence.map((evidenceItem) => (
                                    <article key={evidenceItem.incidentId} className="ai-evidence-card">
                                      <div className="evidence-card-header">
                                        <div className="evidence-card-title-group">
                                          <span className="evidence-incident-number">
                                            {evidenceItem.incidentNumber}
                                          </span>
                                          <h4 className="evidence-title">{evidenceItem.title}</h4>
                                        </div>
                                        <div className="evidence-badges">
                                          <span className="badge badge-category">
                                            {evidenceItem.category}
                                          </span>
                                          <span className={`badge match-${evidenceItem.matchStrength.toLowerCase()}`}>
                                            Match: {evidenceItem.matchStrength}
                                          </span>
                                        </div>
                                      </div>

                                      <div className="evidence-card-body">
                                        <div className="evidence-field">
                                          <span className="evidence-field-label">Why similar:</span>
                                          <p className="evidence-field-value">{evidenceItem.reasonForMatch}</p>
                                        </div>

                                        <div className="evidence-field">
                                          <span className="evidence-field-label">Successful resolution:</span>
                                          <p className="evidence-field-value resolution-excerpt">
                                            "{evidenceItem.resolutionExcerpt}"
                                          </p>
                                        </div>
                                      </div>
                                    </article>
                                  ))}
                                </div>
                              )}
                            </div>

                            {/* Caveats / Decision Support Callout */}
                            <div className="ai-disclaimer ai-resolution-caveat" role="note">
                              <strong>Decision Support Only:</strong> {latest.caveats}
                            </div>

                            {/* Analysis History */}
                            {resolutionAnalyses.length > 1 && (
                              <details className="ai-history">
                                <summary className="ai-history-toggle">
                                  Resolution assistance history ({resolutionAnalyses.length - 1} older
                                  {resolutionAnalyses.length - 1 === 1 ? " run" : " runs"})
                                </summary>
                                <ol className="ai-history-list">
                                  {resolutionAnalyses.slice(1).map((hist) => (
                                    <li key={hist.id} className="ai-history-item">
                                      <div className="ai-history-meta">
                                        <span>{formatDate(hist.createdAt)}</span>
                                        <span>{hist.provider} / {hist.model}</span>
                                        <span className={`badge match-${hist.confidence.toLowerCase()}`}>
                                          {hist.confidence}
                                        </span>
                                      </div>
                                      <div className="ai-history-row">
                                        <span>Likely Issue: <strong>{hist.likelyIssue}</strong></span>
                                        <span>Similar Evidence: <strong>{hist.evidence.length} incidents</strong></span>
                                      </div>
                                    </li>
                                  ))}
                                </ol>
                              </details>
                            )}
                          </div>
                        );
                      })()}
                    </section>
                  )}
                </div>
              </>
            )}
          </section>
        </div>
      </main>

      {showResolveModal && (
        <div
          className="modal-backdrop"
          role="dialog"
          aria-modal="true"
          aria-labelledby="resolve-modal-title"
        >
          <div className="modal-card resolve-modal-card">
            <div className="modal-header">
              <div>
                <h2 id="resolve-modal-title">Resolve Incident</h2>
                <p>Provide a written description of the resolution actions taken.</p>
              </div>
              <button
                type="button"
                className="close-button"
                onClick={() => setShowResolveModal(false)}
                aria-label="Close"
              >
                ✕
              </button>
            </div>

            <form
              onSubmit={(e) => {
                e.preventDefault();
                void handleConfirmResolve();
              }}
            >
              {resolveError && (
                <div className="inline-notice error" role="alert">
                  {resolveError}
                </div>
              )}

              <div className="form-field">
                <label htmlFor="resolve-note-input">
                  Resolution Summary <span className="required-marker">*</span>
                </label>
                <textarea
                  id="resolve-note-input"
                  className="resolve-textarea"
                  rows={4}
                  placeholder="e.g. Reinstalled VPN client and updated network adapter driver. Confirmed successful connectivity with user."
                  value={resolutionText}
                  onChange={(e) => {
                    setResolutionText(e.target.value);
                    if (resolveError) setResolveError("");
                  }}
                  required
                />
                <span className="field-hint">
                  Minimum 5 characters. This description will be recorded as part of the permanent audit record.
                </span>
              </div>

              <div className="modal-actions">
                <button
                  type="button"
                  className="workflow-button secondary"
                  onClick={() => setShowResolveModal(false)}
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  id="confirm-resolve-btn"
                  className="workflow-button success"
                  disabled={actionBusy !== null}
                >
                  {actionBusy === "Resolved"
                    ? "Resolving…"
                    : "Confirm & Mark Resolved"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {showAdminCloseModal && (
        <div
          className="modal-backdrop"
          role="dialog"
          aria-modal="true"
          aria-labelledby="admin-close-modal-title"
        >
          <div className="modal-card resolve-modal-card">
            <div className="modal-header">
              <div>
                <h2 id="admin-close-modal-title">Administrative Closure</h2>
                <p>Terminate this incident without normal technician resolution.</p>
              </div>
              <button
                type="button"
                className="close-button"
                onClick={() => setShowAdminCloseModal(false)}
                aria-label="Close"
              >
                ✕
              </button>
            </div>

            <form
              onSubmit={(e) => {
                e.preventDefault();
                void handleConfirmAdminClose();
              }}
            >
              {adminCloseError && (
                <div className="inline-notice error" role="alert">
                  {adminCloseError}
                </div>
              )}

              <div className="form-field">
                <label htmlFor="admin-close-reason-input">
                  Closure Reason <span className="required-marker">*</span>
                </label>
                <textarea
                  id="admin-close-reason-input"
                  className="resolve-textarea"
                  rows={4}
                  placeholder="e.g. Duplicate of INC-20260917-ABC123, invalid request, or created in error."
                  value={adminCloseReason}
                  onChange={(e) => {
                    setAdminCloseReason(e.target.value);
                    if (adminCloseError) setAdminCloseError("");
                  }}
                  required
                />
                <span className="field-hint">
                  A reason is mandatory and will be permanently recorded in the incident activity history.
                </span>
              </div>

              <div className="modal-actions">
                <button
                  type="button"
                  className="workflow-button secondary"
                  onClick={() => setShowAdminCloseModal(false)}
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  id="confirm-admin-close-btn"
                  className="workflow-button danger"
                  disabled={actionBusy !== null}
                >
                  {actionBusy === "Closed"
                    ? "Closing…"
                    : "Confirm Administrative Close"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
}
