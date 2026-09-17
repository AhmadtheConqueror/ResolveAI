import { useCallback, useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";

import {
  getNotifications,
  isUnauthorizedError,
  markAllNotificationsRead,
  markNotificationRead,
} from "../api/api";
import type { CurrentUser, NotificationItem } from "../api/api";
import { Skeleton } from "./Skeleton";

type NotificationBellProps = {
  user: CurrentUser | null;
  onAuthLost: () => void;
};

function formatRelativeTime(value: string) {
  const created = new Date(value).getTime();
  const diffMs = Date.now() - created;
  const diffMinutes = Math.max(0, Math.floor(diffMs / 60000));

  if (diffMinutes < 1) return "Just now";
  if (diffMinutes < 60) return `${diffMinutes}m ago`;

  const hours = Math.floor(diffMinutes / 60);
  if (hours < 24) return `${hours}h ago`;

  const days = Math.floor(hours / 24);
  if (days < 7) return `${days}d ago`;

  return new Intl.DateTimeFormat("en", {
    month: "short",
    day: "numeric",
  }).format(new Date(value));
}

function getTypeClass(type: string) {
  const normalized = type.toLowerCase();

  if (normalized.includes("sla")) return "sla";
  if (normalized.includes("resolved")) return "success";
  if (normalized.includes("assigned")) return "assigned";
  if (normalized.includes("comment")) return "comment";

  return "default";
}

export default function NotificationBell({
  user,
  onAuthLost,
}: NotificationBellProps) {
  const navigate = useNavigate();
  const [open, setOpen] = useState(false);
  const [items, setItems] = useState<NotificationItem[]>([]);
  const [unreadCount, setUnreadCount] = useState(0);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");

  const loadNotifications = useCallback(async () => {
    if (!user) return;

    try {
      setError("");
      const data = await getNotifications({
        page: 1,
        pageSize: 20,
      });

      setItems(data.items);
      setUnreadCount(data.unreadCount);
    } catch (err) {
      if (isUnauthorizedError(err)) {
        onAuthLost();
        return;
      }

      setError(
        err instanceof Error
          ? err.message
          : "Unable to load notifications."
      );
    }
  }, [onAuthLost, user]);

  useEffect(() => {
    if (!user) return;

    void Promise.resolve().then(loadNotifications);

    const intervalId = window.setInterval(() => {
      void loadNotifications();
    }, 45000);

    return () => window.clearInterval(intervalId);
  }, [loadNotifications, user]);

  async function handleToggle() {
    const nextOpen = !open;
    setOpen(nextOpen);

    if (nextOpen) {
      if (items.length === 0) {
        setLoading(true);
      }
      await loadNotifications();
      setLoading(false);
    }
  }

  async function handleNotificationClick(notification: NotificationItem) {
    if (!notification.isRead) {
      try {
        await markNotificationRead(notification.id);
        setItems((current) =>
          current.map((item) =>
            item.id === notification.id
              ? {
                  ...item,
                  isRead: true,
                  readAt: new Date().toISOString(),
                }
              : item
          )
        );
        setUnreadCount((count) => Math.max(0, count - 1));
      } catch (err) {
        if (isUnauthorizedError(err)) {
          onAuthLost();
          return;
        }
      }
    }

    setOpen(false);

    if (notification.incidentId) {
      navigate(`/incidents/${notification.incidentId}`);
    }
  }

  async function handleMarkAllRead() {
    try {
      await markAllNotificationsRead();
      setItems((current) =>
        current.map((item) => ({
          ...item,
          isRead: true,
          readAt: item.readAt ?? new Date().toISOString(),
        }))
      );
      setUnreadCount(0);
    } catch (err) {
      if (isUnauthorizedError(err)) {
        onAuthLost();
        return;
      }

      setError(
        err instanceof Error
          ? err.message
          : "Unable to update notifications."
      );
    }
  }

  return (
    <div className="notification-root">
      <button
        type="button"
        className={`notification-trigger ${open ? "active" : ""}`}
        onClick={() => void handleToggle()}
        aria-haspopup="dialog"
        aria-expanded={open}
      >
        <span className="notification-trigger-icon" aria-hidden="true">
          !
        </span>
        <span>Notifications</span>
        {unreadCount > 0 && (
          <span className="notification-count">
            {unreadCount > 99 ? "99+" : unreadCount}
          </span>
        )}
      </button>

      {open && (
        <div className="notification-panel" role="dialog" aria-label="Notifications">
          <div className="notification-panel-header">
            <strong>Notifications</strong>
            {unreadCount > 0 && (
              <button
                type="button"
                className="notification-mark-all"
                onClick={() => void handleMarkAllRead()}
              >
                Mark all as read
              </button>
            )}
          </div>

          {loading && items.length === 0 && (
            <div className="notification-list" aria-busy="true">
              <span className="sr-only">Loading notifications…</span>
              {Array.from({ length: 4 }).map((_, i) => (
                <div
                  key={i}
                  className="skeleton-notification-row"
                  aria-hidden="true"
                >
                  <Skeleton
                    variant="circle"
                    width={9}
                    height={9}
                    style={{ marginTop: 5 }}
                  />
                  <div className="notification-copy">
                    <Skeleton
                      width="60%"
                      height={14}
                      style={{ marginBottom: 4 }}
                    />
                    <Skeleton
                      width="90%"
                      height={12}
                      style={{ marginBottom: 4 }}
                    />
                    <Skeleton width="35%" height={11} />
                  </div>
                </div>
              ))}
            </div>
          )}

          {error && (
            <div className="notification-state error">
              {error}
            </div>
          )}

          {!loading && !error && items.length === 0 && (
            <div className="notification-state">
              You're all caught up.
            </div>
          )}

          {!loading && !error && items.length > 0 && (
            <div className="notification-list">
              {items.map((notification) => (
                <button
                  type="button"
                  key={notification.id}
                  className={`notification-item ${
                    notification.isRead ? "read" : "unread"
                  }`}
                  onClick={() =>
                    void handleNotificationClick(notification)
                  }
                >
                  <span
                    className={`notification-dot ${getTypeClass(
                      notification.type
                    )}`}
                    aria-hidden="true"
                  />
                  <span className="notification-copy">
                    <strong className="notification-title">{notification.title}</strong>
                    {notification.incidentTitle && (
                      <span className="notification-incident-title">
                        {notification.incidentTitle}
                      </span>
                    )}
                    <span className="notification-meta">
                      {notification.incidentNumber && (
                        <span className="notification-inc-number">
                          {notification.incidentNumber}
                        </span>
                      )}
                      <small>
                        {formatRelativeTime(notification.createdAt)}
                      </small>
                    </span>
                    {!notification.incidentTitle && (
                      <span className="notification-plain-message">
                        {notification.message}
                      </span>
                    )}
                  </span>
                </button>
              ))}
            </div>
          )}
        </div>
      )}
    </div>
  );
}
