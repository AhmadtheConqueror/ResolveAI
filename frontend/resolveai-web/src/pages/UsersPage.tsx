import { useCallback, useEffect, useMemo, useState } from "react";
import type { FormEvent } from "react";
import { useNavigate } from "react-router-dom";

import {
  getUsers,
  getUserOptions,
  createUser,
  updateUser,
  updateUserStatus,
  resetUserPassword,
  isUnauthorizedError,
} from "../api/api";
import type { CurrentUser, UserRecord, UserOptions } from "../api/api";

// ─── Helpers ────────────────────────────────────────────────────────────────

function readCurrentUser(): CurrentUser | null {
  const raw = sessionStorage.getItem("currentUser");
  if (!raw) return null;
  try {
    return JSON.parse(raw) as CurrentUser;
  } catch {
    return null;
  }
}

function formatDate(iso: string) {
  return new Intl.DateTimeFormat("en", {
    month: "short",
    day: "numeric",
    year: "numeric",
  }).format(new Date(iso));
}

// ─── Add User Modal ──────────────────────────────────────────────────────────

interface AddUserModalProps {
  options: UserOptions;
  onClose: () => void;
  onCreated: (user: UserRecord) => void;
  onUnauthorized: () => void;
}

function AddUserModal({
  options,
  onClose,
  onCreated,
  onUnauthorized,
}: AddUserModalProps) {
  const [firstName, setFirstName] = useState("");
  const [lastName, setLastName] = useState("");
  const [email, setEmail] = useState("");
  const [roleId, setRoleId] = useState(options.roles[0]?.id ?? "");
  const [departmentId, setDepartmentId] = useState("");
  const [tempPassword, setTempPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState("");

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setSubmitting(true);
    setError("");

    try {
      const user = await createUser({
        firstName: firstName.trim(),
        lastName: lastName.trim(),
        email: email.trim(),
        roleId,
        departmentId: departmentId || null,
        temporaryPassword: tempPassword,
      });
      onCreated(user);
    } catch (err) {
      if (isUnauthorizedError(err)) {
        onUnauthorized();
        return;
      }
      setError(
        err instanceof Error ? err.message : "Unable to create user."
      );
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div
      className="modal-backdrop"
      role="dialog"
      aria-modal="true"
      aria-labelledby="add-user-modal-title"
    >
      <div className="modal-card user-modal-card">
        <div className="modal-header">
          <div>
            <h2 id="add-user-modal-title">Add User</h2>
            <p>Provision a new organisational account.</p>
          </div>
          <button
            type="button"
            className="close-button"
            onClick={onClose}
            aria-label="Close"
          >
            ✕
          </button>
        </div>

        <form onSubmit={handleSubmit} noValidate>
          <div className="form-row">
            <div className="form-field">
              <label htmlFor="add-first-name">First name</label>
              <input
                id="add-first-name"
                type="text"
                value={firstName}
                onChange={(e) => setFirstName(e.target.value)}
                required
                maxLength={100}
                placeholder="Amina"
              />
            </div>

            <div className="form-field">
              <label htmlFor="add-last-name">Last name</label>
              <input
                id="add-last-name"
                type="text"
                value={lastName}
                onChange={(e) => setLastName(e.target.value)}
                required
                maxLength={100}
                placeholder="Yusuf"
              />
            </div>
          </div>

          <div className="form-field">
            <label htmlFor="add-email">Email address</label>
            <input
              id="add-email"
              type="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              required
              placeholder="amina@company.com"
            />
          </div>

          <div className="form-row">
            <div className="form-field">
              <label htmlFor="add-role">Role</label>
              <select
                id="add-role"
                value={roleId}
                onChange={(e) => setRoleId(e.target.value)}
                required
              >
                {options.roles.map((r) => (
                  <option key={r.id} value={r.id}>
                    {r.name}
                  </option>
                ))}
              </select>
            </div>

            <div className="form-field">
              <label htmlFor="add-department">Department</label>
              <select
                id="add-department"
                value={departmentId}
                onChange={(e) => setDepartmentId(e.target.value)}
              >
                <option value="">None</option>
                {options.departments.map((d) => (
                  <option key={d.id} value={d.id}>
                    {d.name}
                  </option>
                ))}
              </select>
            </div>
          </div>

          <div className="form-field">
            <label htmlFor="add-password">Temporary password</label>
            <div className="password-field-wrapper">
              <input
                id="add-password"
                type={showPassword ? "text" : "password"}
                value={tempPassword}
                onChange={(e) => setTempPassword(e.target.value)}
                required
                minLength={8}
                placeholder="Min. 8 characters"
              />
              <button
                type="button"
                className="password-toggle-button"
                onClick={() => setShowPassword((v) => !v)}
                aria-label={showPassword ? "Hide password" : "Show password"}
              >
                {showPassword ? "🙈" : "👁"}
              </button>
            </div>
          </div>

          {error && (
            <div className="error-message" role="alert">
              {error}
            </div>
          )}

          <div className="modal-actions">
            <button
              type="button"
              className="secondary-button"
              onClick={onClose}
              disabled={submitting}
            >
              Cancel
            </button>
            <button
              type="submit"
              className="primary-button"
              disabled={submitting}
            >
              {submitting ? "Creating…" : "Create User"}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}

// ─── Edit User Modal ─────────────────────────────────────────────────────────

interface EditUserModalProps {
  user: UserRecord;
  options: UserOptions;
  currentAdminId: string;
  isSoleActiveAdmin: boolean;
  onClose: () => void;
  onUpdated: (user: UserRecord) => void;
  onStatusChanged: (id: string, isActive: boolean) => void;
  onUnauthorized: () => void;
}

function EditUserModal({
  user,
  options,
  currentAdminId,
  isSoleActiveAdmin,
  onClose,
  onUpdated,
  onStatusChanged,
  onUnauthorized,
}: EditUserModalProps) {
  const [firstName, setFirstName] = useState(user.firstName);
  const [lastName, setLastName] = useState(user.lastName);
  const [email, setEmail] = useState(user.email);
  const [roleId, setRoleId] = useState(user.role.id);
  const [departmentId, setDepartmentId] = useState(
    user.department?.id ?? ""
  );
  const [submitting, setSubmitting] = useState(false);
  const [statusChanging, setStatusChanging] = useState(false);
  const [error, setError] = useState("");
  const [successMessage, setSuccessMessage] = useState("");

  // Reset password state
  const [showResetPassword, setShowResetPassword] = useState(false);
  const [newPassword, setNewPassword] = useState("");
  const [showNewPassword, setShowNewPassword] = useState(false);
  const [resettingPassword, setResettingPassword] = useState(false);

  // Status change confirmation
  const [confirmingDeactivate, setConfirmingDeactivate] = useState(false);

  const isSelf = user.id === currentAdminId;

  async function handleSave(e: FormEvent) {
    e.preventDefault();
    setSubmitting(true);
    setError("");
    setSuccessMessage("");

    try {
      const updated = await updateUser(user.id, {
        firstName: firstName.trim(),
        lastName: lastName.trim(),
        email: email.trim(),
        roleId,
        departmentId: departmentId || null,
      });
      setSuccessMessage("Changes saved.");
      onUpdated(updated);
    } catch (err) {
      if (isUnauthorizedError(err)) {
        onUnauthorized();
        return;
      }
      setError(
        err instanceof Error ? err.message : "Unable to save changes."
      );
    } finally {
      setSubmitting(false);
    }
  }

  async function handleStatusChange(activate: boolean) {
    setStatusChanging(true);
    setError("");

    try {
      await updateUserStatus(user.id, activate);
      onStatusChanged(user.id, activate);
      setConfirmingDeactivate(false);
    } catch (err) {
      if (isUnauthorizedError(err)) {
        onUnauthorized();
        return;
      }
      setError(
        err instanceof Error ? err.message : "Unable to change account status."
      );
    } finally {
      setStatusChanging(false);
    }
  }

  async function handleResetPassword(e: FormEvent) {
    e.preventDefault();
    setResettingPassword(true);
    setError("");

    try {
      await resetUserPassword(user.id, newPassword);
      setSuccessMessage("Password reset successfully.");
      setShowResetPassword(false);
      setNewPassword("");
    } catch (err) {
      if (isUnauthorizedError(err)) {
        onUnauthorized();
        return;
      }
      setError(
        err instanceof Error ? err.message : "Unable to reset password."
      );
    } finally {
      setResettingPassword(false);
    }
  }

  return (
    <div
      className="modal-backdrop"
      role="dialog"
      aria-modal="true"
      aria-labelledby="edit-user-modal-title"
    >
      <div className="modal-card user-modal-card">
        <div className="modal-header">
          <div>
            <h2 id="edit-user-modal-title">
              Edit User
            </h2>
            <p>
              {user.firstName} {user.lastName} ·{" "}
              <span
                className={`user-status-inline ${user.isActive ? "status-active" : "status-inactive"}`}
              >
                {user.isActive ? "Active" : "Inactive"}
              </span>
            </p>
          </div>
          <button
            type="button"
            className="close-button"
            onClick={onClose}
            aria-label="Close"
          >
            ✕
          </button>
        </div>

        {successMessage && (
          <div className="success-message" role="status">
            ✓ {successMessage}
          </div>
        )}

        {error && (
          <div className="error-message" role="alert">
            {error}
          </div>
        )}

        <form onSubmit={handleSave} noValidate>
          <div className="form-row">
            <div className="form-field">
              <label htmlFor="edit-first-name">First name</label>
              <input
                id="edit-first-name"
                type="text"
                value={firstName}
                onChange={(e) => setFirstName(e.target.value)}
                required
                maxLength={100}
              />
            </div>

            <div className="form-field">
              <label htmlFor="edit-last-name">Last name</label>
              <input
                id="edit-last-name"
                type="text"
                value={lastName}
                onChange={(e) => setLastName(e.target.value)}
                required
                maxLength={100}
              />
            </div>
          </div>

          <div className="form-field">
            <label htmlFor="edit-email">Email address</label>
            <input
              id="edit-email"
              type="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              required
            />
          </div>

          <div className="form-row">
            <div className="form-field">
              <label htmlFor="edit-role">Role</label>
              <select
                id="edit-role"
                value={roleId}
                onChange={(e) => setRoleId(e.target.value)}
                disabled={isSoleActiveAdmin}
                required
              >
                {options.roles.map((r) => (
                  <option key={r.id} value={r.id}>
                    {r.name}
                  </option>
                ))}
              </select>
              {isSoleActiveAdmin && (
                <small className="field-hint warning-hint">
                  {isSelf
                    ? "You are the only active Admin. Assign another Admin before changing this role."
                    : "This user is the only active Admin. Assign another Admin before changing this role."}
                </small>
              )}
            </div>

            <div className="form-field">
              <label htmlFor="edit-department">Department</label>
              <select
                id="edit-department"
                value={departmentId}
                onChange={(e) => setDepartmentId(e.target.value)}
              >
                <option value="">None</option>
                {options.departments.map((d) => (
                  <option key={d.id} value={d.id}>
                    {d.name}
                  </option>
                ))}
              </select>
            </div>
          </div>

          <div className="modal-actions">
            <button
              type="button"
              className="secondary-button"
              onClick={onClose}
              disabled={submitting}
            >
              Cancel
            </button>
            <button
              type="submit"
              className="primary-button"
              disabled={submitting}
            >
              {submitting ? "Saving…" : "Save Changes"}
            </button>
          </div>
        </form>

        {/* Reset Password */}
        <div className="edit-user-extra-actions">
          <button
            type="button"
            className="edit-extra-action-link"
            onClick={() => setShowResetPassword((v) => !v)}
          >
            {showResetPassword ? "Cancel password reset" : "Reset password"}
          </button>

          {showResetPassword && (
            <form onSubmit={handleResetPassword} className="reset-password-form" noValidate>
              <div className="form-field">
                <label htmlFor="edit-new-password">New temporary password</label>
                <div className="password-field-wrapper">
                  <input
                    id="edit-new-password"
                    type={showNewPassword ? "text" : "password"}
                    value={newPassword}
                    onChange={(e) => setNewPassword(e.target.value)}
                    required
                    minLength={8}
                    placeholder="Min. 8 characters"
                  />
                  <button
                    type="button"
                    className="password-toggle-button"
                    onClick={() => setShowNewPassword((v) => !v)}
                    aria-label={showNewPassword ? "Hide" : "Show"}
                  >
                    {showNewPassword ? "🙈" : "👁"}
                  </button>
                </div>
              </div>
              <button
                type="submit"
                className="secondary-button"
                disabled={resettingPassword}
              >
                {resettingPassword ? "Resetting…" : "Set Password"}
              </button>
            </form>
          )}

          {/* Activate / Deactivate */}
          {!isSelf && (
            <>
              {user.isActive ? (
                !confirmingDeactivate ? (
                  <button
                    type="button"
                    className="danger-action-link"
                    onClick={() => setConfirmingDeactivate(true)}
                  >
                    Deactivate account
                  </button>
                ) : (
                  <div className="deactivate-confirm">
                    <p>
                      Deactivate <strong>{user.firstName} {user.lastName}</strong>?
                      They will not be able to log in.
                    </p>
                    <div className="modal-actions" style={{ marginTop: 10 }}>
                      <button
                        type="button"
                        className="secondary-button"
                        onClick={() => setConfirmingDeactivate(false)}
                        disabled={statusChanging}
                      >
                        Cancel
                      </button>
                      <button
                        type="button"
                        className="danger-button"
                        onClick={() => void handleStatusChange(false)}
                        disabled={statusChanging}
                      >
                        {statusChanging ? "Deactivating…" : "Confirm Deactivate"}
                      </button>
                    </div>
                  </div>
                )
              ) : (
                <button
                  type="button"
                  className="activate-action-link"
                  onClick={() => void handleStatusChange(true)}
                  disabled={statusChanging}
                >
                  {statusChanging ? "Activating…" : "Reactivate account"}
                </button>
              )}
            </>
          )}
        </div>
      </div>
    </div>
  );
}

// ─── Users Page ──────────────────────────────────────────────────────────────

export default function UsersPage() {
  const navigate = useNavigate();
  const [user] = useState<CurrentUser | null>(() => readCurrentUser());

  const [users, setUsers] = useState<UserRecord[]>([]);
  const [options, setOptions] = useState<UserOptions | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  const [showAddModal, setShowAddModal] = useState(false);
  const [editingUser, setEditingUser] = useState<UserRecord | null>(null);

  // Filters
  const [search, setSearch] = useState("");
  const [filterRole, setFilterRole] = useState("");
  const [filterDept, setFilterDept] = useState("");
  const [filterStatus, setFilterStatus] = useState<"" | "active" | "inactive">(
    ""
  );

  const handleUnauthorized = useCallback(() => {
    sessionStorage.clear();
    navigate("/", { replace: true });
  }, [navigate]);

  useEffect(() => {
    if (!user) {
      handleUnauthorized();
      return;
    }
    if (user.role !== "Admin") {
      navigate("/dashboard", { replace: true });
      return;
    }

    async function load() {
      try {
        const [usersData, optionsData] = await Promise.all([
          getUsers(),
          getUserOptions(),
        ]);
        setUsers(usersData);
        setOptions(optionsData);
      } catch (err) {
        if (isUnauthorizedError(err)) {
          handleUnauthorized();
          return;
        }
        setError(
          err instanceof Error ? err.message : "Unable to load users."
        );
      } finally {
        setLoading(false);
      }
    }

    void load();
  }, [handleUnauthorized, navigate, user]);

  function logout() {
    sessionStorage.clear();
    navigate("/", { replace: true });
  }

  // KPI counts
  const kpis = useMemo(() => {
    const total = users.length;
    const active = users.filter((u) => u.isActive).length;
    const inactive = users.filter((u) => !u.isActive).length;
    const admins = users.filter(
      (u) => u.isActive && u.role.name === "Admin"
    ).length;
    return { total, active, inactive, admins };
  }, [users]);

  // Filtered list
  const filtered = useMemo(() => {
    return users.filter((u) => {
      const q = search.toLowerCase();
      if (
        q &&
        !`${u.firstName} ${u.lastName}`.toLowerCase().includes(q) &&
        !u.email.toLowerCase().includes(q)
      ) {
        return false;
      }
      if (filterRole && u.role.name !== filterRole) return false;
      if (filterDept && u.department?.name !== filterDept) return false;
      if (filterStatus === "active" && !u.isActive) return false;
      if (filterStatus === "inactive" && u.isActive) return false;
      return true;
    });
  }, [users, search, filterRole, filterDept, filterStatus]);

  function handleUserCreated(newUser: UserRecord) {
    setUsers((prev) => [newUser, ...prev]);
    setShowAddModal(false);
  }

  function handleUserUpdated(updated: UserRecord) {
    setUsers((prev) =>
      prev.map((u) => (u.id === updated.id ? { ...u, ...updated } : u))
    );
  }

  function handleStatusChanged(id: string, isActive: boolean) {
    setUsers((prev) =>
      prev.map((u) => (u.id === id ? { ...u, isActive } : u))
    );
    setEditingUser((prev) => (prev?.id === id ? { ...prev, isActive } : prev));
  }

  const roleColors: Record<string, string> = {
    Admin: "role-admin",
    Manager: "role-manager",
    Technician: "role-technician",
    Employee: "role-employee",
  };

  return (
    <div className="app-shell">
      {/* Sidebar */}
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
            className="nav-item"
            onClick={() => navigate("/dashboard")}
          >
            Dashboard
          </button>

          <button
            type="button"
            className="nav-item"
            onClick={() => navigate("/dashboard")}
          >
            Incidents
          </button>

          <button type="button" className="nav-item">
            My Work
          </button>

          <button type="button" className="nav-item">
            Analytics
          </button>

          {user?.role === "Admin" && (
            <button
              type="button"
              className="nav-item active"
              aria-current="page"
            >
              Users
            </button>
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

      {/* Main */}
      <main className="main-content">
        <div className="content-inner">
          <header className="topbar">
            <div>
              <span className="eyebrow">Administration</span>
              <h1>Users</h1>
              <p>
                Manage organisational access, roles and departments.
              </p>
            </div>

            <button
              type="button"
              id="add-user-button"
              className="new-incident-button"
              onClick={() => setShowAddModal(true)}
              disabled={!options}
            >
              <span aria-hidden="true">+</span>
              Add User
            </button>
          </header>

          {/* KPI Cards */}
          <section className="kpi-grid" aria-label="User summary">
            <div className="kpi-card">
              <span>Total Users</span>
              <strong>{kpis.total}</strong>
              <small>Provisioned accounts</small>
            </div>
            <div className="kpi-card">
              <span>Active</span>
              <strong>{kpis.active}</strong>
              <small>Can log in</small>
            </div>
            <div className="kpi-card">
              <span>Inactive</span>
              <strong>{kpis.inactive}</strong>
              <small>Blocked from login</small>
            </div>
            <div className="kpi-card">
              <span>Administrators</span>
              <strong>{kpis.admins}</strong>
              <small>Active Admins</small>
            </div>
          </section>

          {/* Filter bar */}
          <section className="users-filter-bar">
            <input
              type="search"
              id="user-search"
              className="users-search-input"
              placeholder="Search name or email…"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              aria-label="Search users"
            />

            <select
              id="filter-role"
              className="users-filter-select"
              value={filterRole}
              onChange={(e) => setFilterRole(e.target.value)}
              aria-label="Filter by role"
            >
              <option value="">All roles</option>
              {options?.roles.map((r) => (
                <option key={r.id} value={r.name}>
                  {r.name}
                </option>
              ))}
            </select>

            <select
              id="filter-department"
              className="users-filter-select"
              value={filterDept}
              onChange={(e) => setFilterDept(e.target.value)}
              aria-label="Filter by department"
            >
              <option value="">All departments</option>
              {options?.departments.map((d) => (
                <option key={d.id} value={d.name}>
                  {d.name}
                </option>
              ))}
            </select>

            <select
              id="filter-status"
              className="users-filter-select"
              value={filterStatus}
              onChange={(e) =>
                setFilterStatus(e.target.value as "" | "active" | "inactive")
              }
              aria-label="Filter by status"
            >
              <option value="">All statuses</option>
              <option value="active">Active</option>
              <option value="inactive">Inactive</option>
            </select>
          </section>

          {/* Table */}
          <section className="incidents-panel">
            {loading && (
              <div className="table-message">Loading users…</div>
            )}

            {!loading && error && (
              <div className="error-message">{error}</div>
            )}

            {!loading && !error && filtered.length === 0 && (
              <div className="table-message">
                {users.length === 0
                  ? "No users found."
                  : "No users match your filters."}
              </div>
            )}

            {!loading && !error && filtered.length > 0 && (
              <div className="table-wrapper">
                <table className="incidents-table users-table">
                  <thead>
                    <tr>
                      <th>Name</th>
                      <th>Email</th>
                      <th>Department</th>
                      <th>Role</th>
                      <th>Status</th>
                      <th>Created</th>
                      <th>Actions</th>
                    </tr>
                  </thead>
                  <tbody>
                    {filtered.map((u) => (
                      <tr key={u.id}>
                        <td className="user-name-cell">
                          <div className="user-avatar">
                            {u.firstName[0]}{u.lastName[0]}
                          </div>
                          <span>
                            {u.firstName} {u.lastName}
                          </span>
                        </td>
                        <td className="user-email-cell">{u.email}</td>
                        <td>
                          {u.department ? (
                            u.department.name
                          ) : (
                            <span className="no-dept">—</span>
                          )}
                        </td>
                        <td>
                          <span
                            className={`badge user-role-badge ${roleColors[u.role.name] ?? ""}`}
                          >
                            {u.role.name}
                          </span>
                        </td>
                        <td>
                          <span
                            className={`badge ${u.isActive ? "user-status-active" : "user-status-inactive"}`}
                          >
                            {u.isActive ? "Active" : "Inactive"}
                          </span>
                        </td>
                        <td>{formatDate(u.createdAt)}</td>
                        <td>
                          <button
                            type="button"
                            className="table-action-button"
                            onClick={() => setEditingUser(u)}
                          >
                            Edit
                          </button>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </section>
        </div>
      </main>

      {/* Modals */}
      {showAddModal && options && (
        <AddUserModal
          options={options}
          onClose={() => setShowAddModal(false)}
          onCreated={handleUserCreated}
          onUnauthorized={handleUnauthorized}
        />
      )}

      {editingUser && options && (
        <EditUserModal
          user={editingUser}
          options={options}
          currentAdminId={user?.id ?? ""}
          isSoleActiveAdmin={
            editingUser.isActive &&
            editingUser.role.name === "Admin" &&
            users.filter(
              (u) =>
                u.isActive && u.role.name === "Admin" && u.id !== editingUser.id
            ).length === 0
          }
          onClose={() => setEditingUser(null)}
          onUpdated={handleUserUpdated}
          onStatusChanged={handleStatusChanged}
          onUnauthorized={handleUnauthorized}
        />
      )}
    </div>
  );
}
