import { useNavigate } from "react-router-dom";
import type { CurrentUser } from "../api/api";
import NotificationBell from "./NotificationBell";

type SidebarProps = {
  currentTab: "dashboard" | "incidents" | "my-work" | "analytics" | "users";
  user: CurrentUser | null;
  onLogout: () => void;
};

export default function Sidebar({ currentTab, user, onLogout }: SidebarProps) {
  const navigate = useNavigate();
  const role = user?.role ?? "";
  const isEmployee = role === "Employee";
  const isAdmin = role === "Admin";
  const canViewAnalytics = role === "Technician" || role === "Manager" || role === "Admin";

  return (
    <aside className="sidebar">
      <div className="sidebar-brand">
        <div className="sidebar-logo">R</div>
        <div>
          <strong>ResolveAI</strong>
          <span>Incident Management</span>
        </div>
      </div>

      <nav className="sidebar-nav" aria-label="Main navigation">
        <button
          type="button"
          id="nav-dashboard"
          className={`nav-item ${currentTab === "dashboard" ? "active" : ""}`}
          aria-current={currentTab === "dashboard" ? "page" : undefined}
          onClick={() => navigate("/dashboard")}
        >
          Dashboard
        </button>

        <button
          type="button"
          id="nav-incidents"
          className={`nav-item ${currentTab === "incidents" ? "active" : ""}`}
          aria-current={currentTab === "incidents" ? "page" : undefined}
          onClick={() => navigate("/incidents")}
        >
          Incidents
        </button>

        {!isEmployee && (
          <button
            type="button"
            id="nav-my-work"
            className={`nav-item ${currentTab === "my-work" ? "active" : ""}`}
            aria-current={currentTab === "my-work" ? "page" : undefined}
            onClick={() => navigate("/my-work")}
          >
            My Work
          </button>
        )}

        {canViewAnalytics && (
          <button
            type="button"
            id="nav-analytics"
            className={`nav-item ${currentTab === "analytics" ? "active" : ""}`}
            aria-current={currentTab === "analytics" ? "page" : undefined}
            onClick={() => navigate("/analytics")}
          >
            Analytics
          </button>
        )}

        {isAdmin && (
          <button
            type="button"
            id="nav-users"
            className={`nav-item ${currentTab === "users" ? "active" : ""}`}
            aria-current={currentTab === "users" ? "page" : undefined}
            onClick={() => navigate("/users")}
          >
            Users
          </button>
        )}
      </nav>

      <NotificationBell
        user={user}
        onAuthLost={onLogout}
      />

      <div className="sidebar-footer">
        <div className="user-summary">
          <strong>
            {user ? `${user.firstName} ${user.lastName}` : "Signed out"}
          </strong>
          <span>{user?.role ?? "No active session"}</span>
        </div>

        <button
          type="button"
          className="logout-button"
          onClick={onLogout}
        >
          Sign out
        </button>
      </div>
    </aside>
  );
}
