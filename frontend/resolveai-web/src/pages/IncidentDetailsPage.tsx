import { useCallback, useEffect, useMemo, useState } from "react";
import type { FormEvent } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";

import {
  addIncidentComment,
  assignIncident,
  getIncident,
  getIncidentAIAnalyses,
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
  IncidentComment,
  IncidentDetail,
  IncidentStatus,
  TechnicianUser,
} from "../api/api";

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
  tone?: "primary" | "secondary" | "success";
};

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
      ];
    case "Assigned":
      return [
        {
          label: "Start Work",
          status: "InProgress",
          tone: "primary",
        },
      ];
    case "InProgress":
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
    case "WaitingForUser":
      return [
        {
          label: "Resume Work",
          status: "InProgress",
          tone: "primary",
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

  const handleUnauthorized = useCallback(() => {
    sessionStorage.clear();
    navigate("/", { replace: true });
  }, [navigate]);

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

      await refreshIncidentData();
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
  }, [handleUnauthorized, id, refreshIncidentData]);

  useEffect(() => {
    if (!user) {
      handleUnauthorized();
      return;
    }

    void Promise.resolve().then(loadIncident);
  }, [handleUnauthorized, loadIncident, user]);

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

  async function handleStatusUpdate(status: IncidentStatus) {
    if (!id) {
      return;
    }

    setActionBusy(status);
    setWorkflowNotice(null);

    try {
      await updateIncidentStatus(id, status);
      await refreshIncidentData();

      setWorkflowNotice({
        tone: "success",
        message: `Status updated to ${formatStatusLabel(status)}.`,
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

      await refreshIncidentData();

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
      await refreshIncidentData();

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
          <Link className="nav-item" to="/dashboard">
            Dashboard
          </Link>

          <Link
            className="nav-item active"
            to={id ? `/incidents/${id}` : "/dashboard"}
            aria-current="page"
          >
            Incidents
          </Link>

          <Link className="nav-item" to="/dashboard">
            My Work
          </Link>

          <Link className="nav-item" to="/dashboard">
            Analytics
          </Link>

          {user?.role === "Admin" && (
            <Link className="nav-item" to="/dashboard">
              Users
            </Link>
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
          <section className="incident-details-page">
            <Link className="back-link" to="/dashboard">
              Back to Dashboard
            </Link>

            {loading && (
              <div className="details-state">
                Loading incident details...
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
                        <a href={`mailto:${incident.reporter.email}`}>
                          {incident.reporter.email}
                        </a>
                      </div>

                      <div className="person-block">
                        <span>Assigned Technician</span>

                        {incident.assignedTo ? (
                          <>
                            <strong>{incident.assignedTo.name}</strong>
                            <a
                              href={`mailto:${incident.assignedTo.email}`}
                            >
                              {incident.assignedTo.email}
                            </a>
                          </>
                        ) : (
                          <strong>Unassigned</strong>
                        )}
                      </div>
                    </div>
                  </section>

                  <section className="detail-card detail-card-wide">
                    <h2>Resolution</h2>
                    <p className="detail-body">
                      {incident.resolution ||
                        "No resolution recorded yet."}
                    </p>
                  </section>

                  <section className="detail-card detail-card-wide">
                    <h2>Timeline</h2>

                    <dl className="timeline-list">
                      <div>
                        <dt>Created</dt>
                        <dd>{formatDate(incident.createdAt)}</dd>
                      </div>

                      <div>
                        <dt>Updated</dt>
                        <dd>{formatDate(incident.updatedAt)}</dd>
                      </div>

                      {incident.resolvedAt && (
                        <div>
                          <dt>Resolved</dt>
                          <dd>{formatDate(incident.resolvedAt)}</dd>
                        </div>
                      )}

                      {incident.closedAt && (
                        <div>
                          <dt>Closed</dt>
                          <dd>{formatDate(incident.closedAt)}</dd>
                        </div>
                      )}
                    </dl>
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
                </div>
              </>
            )}
          </section>
        </div>
      </main>
    </div>
  );
}
