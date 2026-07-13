import { useMemo, useState } from "react";
import { Check, ClipboardCheck, Download, KeyRound, LayoutDashboard, Plus, Search, ShieldCheck, Trash2, UserCog, Users, X } from "lucide-react";
import { useAuth } from "@/hooks/useAuth";
import { useHasPermission, PermissionDenied } from "@/hooks/usePermission";
import {
  useAdminOverview,
  useAdminPermissions,
  useAdminRoles,
  useAdminUsers,
  useAccessReview,
  useAccessReviews,
  useCompleteAccessReview,
  useCreateAdminRole,
  useCreateAdminUser,
  useCreateAccessReview,
  useDecideAccessReviewItem,
  useDeleteAdminUser,
  useUpdateAdminRole,
  useUpdateAdminUser,
} from "@/hooks/useAdmin";
import { useAuditExportRequests, useAuditLogs, useCreateAuditExport } from "@/hooks/useBatch7";
import { useLocalizationSettings, useUpdateLocaleSettings } from "@/hooks/useBatch6";
import { PERMISSIONS } from "@/auth/rbacConfig";
import { EmptyState, ErrorState, LoadingState, PageHeader, StatusBadge } from "@/components/ui";
import type { AnyRecord } from "@/types";

type AdminTab = "dashboard" | "users" | "roles" | "permissions" | "access" | "settings" | "audit";

type UserFormState = {
  id?: number;
  fullName: string;
  email: string;
  companyId: number;
  roleId: string;
  roleName: string;
  status: string;
  password: string;
};

type RoleFormState = {
  name: string;
  permissions: string[];
};

const TAB_OPTIONS: Array<{ key: AdminTab; label: string }> = [
  { key: "dashboard", label: "Dashboard" },
  { key: "users", label: "Users" },
  { key: "roles", label: "Roles" },
  { key: "permissions", label: "Permissions" },
  { key: "access", label: "Access Reviews" },
  { key: "settings", label: "Settings" },
  { key: "audit", label: "Audit Logs" },
];

function csvValue(value: unknown) {
  const text = String(value ?? "");
  if (/[",\n]/.test(text)) return `"${text.replace(/"/g, '""')}"`;
  return text;
}

function downloadCsv(filename: string, rows: AnyRecord[], headers: string[]) {
  const lines = [
    headers.join(","),
    ...rows.map((row) => headers.map((key) => csvValue(row[key])).join(",")),
  ];
  const blob = new Blob([lines.join("\n")], { type: "text/csv;charset=utf-8;" });
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = filename;
  anchor.click();
  URL.revokeObjectURL(url);
}

function permissionsByGroup(permissions: string[]) {
  const groups: Array<{ title: string; prefix: string }> = [
    { title: "Dashboard", prefix: "dashboard:" },
    { title: "Fleet", prefix: "vehicles:" },
    { title: "Drivers", prefix: "drivers:" },
    { title: "Shipments / Jobs", prefix: "shipments:" },
    { title: "Dispatch", prefix: "dispatch:" },
    { title: "Customers", prefix: "customers:" },
    { title: "Safety", prefix: "safety:" },
    { title: "Maintenance", prefix: "maintenance:" },
    { title: "Compliance", prefix: "compliance:" },
    { title: "Alerts", prefix: "alerts:" },
    { title: "Reports", prefix: "reports:" },
    { title: "Admin", prefix: "users:" },
    { title: "Roles", prefix: "roles:" },
    { title: "Settings", prefix: "settings:" },
    { title: "Audit", prefix: "audit:" },
  ];
  const grouped = groups
    .map((group) => ({
      ...group,
      permissions: permissions.filter((permission) => permission.startsWith(group.prefix)),
    }))
    .filter((group) => group.permissions.length > 0);
  // Catch-all so permissions with unknown prefixes (access_review:, telematics:, …)
  // stay visible and grantable instead of silently disappearing from the editor.
  const known = new Set(grouped.flatMap((g) => g.permissions));
  const other = permissions.filter((p) => !known.has(p));
  if (other.length > 0) grouped.push({ title: "Other", prefix: "", permissions: other });
  return grouped;
}

function permissionList(value: unknown): string[] {
  if (Array.isArray(value)) return value.map(String);
  if (typeof value === "string") {
    try {
      const parsed = JSON.parse(value);
      return Array.isArray(parsed) ? parsed.map(String) : [value];
    } catch {
      return value.split(",").map((item) => item.trim()).filter(Boolean);
    }
  }
  return [];
}

function initials(name: string): string {
  return name
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0]?.toUpperCase() ?? "")
    .join("") || "?";
}

/** Deterministic avatar hue per name so the roster is scannable at a glance. */
function avatarHue(name: string): number {
  let h = 0;
  for (let i = 0; i < name.length; i++) h = (h * 31 + name.charCodeAt(i)) % 360;
  return h;
}

function Avatar({ name }: { name: string }) {
  const hue = avatarHue(name);
  return (
    <span
      aria-hidden="true"
      className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full text-[11px] font-bold text-white shadow-[inset_0_1px_0_rgba(255,255,255,.4),0_2px_5px_rgba(15,23,42,.25)]"
      style={{ background: `linear-gradient(145deg, hsl(${hue} 55% 52%), hsl(${hue} 60% 40%))` }}
    >
      {initials(name)}
    </span>
  );
}

function MfaBadge({ status }: { status: unknown }) {
  const enabled = String(status ?? "").toLowerCase() === "enabled";
  return (
    <span className={`iam-chip ${enabled ? "!text-emerald-700" : "!text-slate-400"}`} title={enabled ? "Multi-factor authentication enrolled" : "MFA not enrolled"}>
      <ShieldCheck className={`h-3 w-3 shrink-0 ${enabled ? "text-emerald-600" : "text-slate-300"}`} />
      <span>{enabled ? "MFA" : "No MFA"}</span>
    </span>
  );
}

export function AdminPage() {
  const { session } = useAuth();
  const hasPermission = useHasPermission();
  const canViewUsers = hasPermission(PERMISSIONS.USERS_VIEW);
  const canCreateUsers = hasPermission(PERMISSIONS.USERS_CREATE);
  const canUpdateUsers = hasPermission(PERMISSIONS.USERS_UPDATE);
  const canDeleteUsers = hasPermission(PERMISSIONS.USERS_DELETE);
  const canViewRoles = hasPermission(PERMISSIONS.ROLES_VIEW);
  const canCreateRoles = hasPermission(PERMISSIONS.ROLES_CREATE);
  const canUpdateRoles = hasPermission(PERMISSIONS.ROLES_UPDATE);
  const canViewSettings = hasPermission(PERMISSIONS.SETTINGS_VIEW);
  const canUpdateSettings = hasPermission(PERMISSIONS.SETTINGS_UPDATE);
  const canViewAudit = hasPermission(PERMISSIONS.AUDIT_VIEW);
  const canExportReports = hasPermission(PERMISSIONS.REPORTS_EXPORT);
  const canViewAccessReviews = hasPermission("access_review:view");
  const canManageAccessReviews = hasPermission("access_review:manage");

  const [tab, setTab] = useState<AdminTab>("dashboard");
  const [search, setSearch] = useState("");
  const [roleFilter, setRoleFilter] = useState("");
  const [statusFilter, setStatusFilter] = useState("");
  const [selectedUser, setSelectedUser] = useState<AnyRecord | null>(null);
  const [userModal, setUserModal] = useState<"create" | "edit" | null>(null);
  const [roleModal, setRoleModal] = useState<AnyRecord | null>(null);
  const [permissionsExportNotice, setPermissionsExportNotice] = useState<string | null>(null);
  const [selectedReviewId, setSelectedReviewId] = useState<number | null>(null);
  const [reviewForm, setReviewForm] = useState({ title: "", description: "", dueDate: "" });
  const [userForm, setUserForm] = useState<UserFormState>({
    fullName: "",
    email: "",
    companyId: Number(session?.company?.id ?? session?.company?.companyId ?? 0),
    roleId: "",
    roleName: "",
    status: "Active",
    password: "",
  });
  const [roleForm, setRoleForm] = useState<RoleFormState>({
    name: "",
    permissions: [],
  });

  const overviewQ = useAdminOverview();
  const usersQ = useAdminUsers({ search, role: roleFilter, status: statusFilter });
  const rolesQ = useAdminRoles();
  const permissionsQ = useAdminPermissions();
  const accessReviewsQ = useAccessReviews(canViewAccessReviews);
  const accessReviewQ = useAccessReview(selectedReviewId);
  const localeQ = useLocalizationSettings();
  const auditLogsQ = useAuditLogs(undefined, canViewAudit);
  const auditExportsQ = useAuditExportRequests(canViewAudit);
  const createAuditExport = useCreateAuditExport();

  const createUser = useCreateAdminUser();
  const updateUser = useUpdateAdminUser();
  const deleteUser = useDeleteAdminUser();
  const updateRole = useUpdateAdminRole();
  const createRole = useCreateAdminRole();
  const updateSettings = useUpdateLocaleSettings();
  const createAccessReview = useCreateAccessReview();
  const decideAccessReviewItem = useDecideAccessReviewItem();
  const completeAccessReview = useCompleteAccessReview();

  const users = usersQ.data ?? [];
  const roles = rolesQ.data ?? [];
  const permissions = permissionsQ.data ?? [];
  const roleOptions = roles;

  const filteredUsers = useMemo(() => users, [users]);

  if (!canViewUsers && !canViewRoles && !canViewSettings && !canViewAudit && !canViewAccessReviews) {
    return <PermissionDenied permission="users:view" />;
  }

  const openCreateUser = () => {
    setUserForm({
      fullName: "",
      email: "",
      companyId: Number(session?.company?.id ?? session?.company?.companyId ?? 0),
      roleId: "",
      roleName: String(roleOptions[0]?.name ?? ""),
      status: "Active",
      password: "",
    });
    setUserModal("create");
  };

  const openEditUser = (user: AnyRecord) => {
    setUserForm({
      id: Number(user.id),
      fullName: String(user.fullName ?? user.full_name ?? ""),
      email: String(user.email ?? ""),
      companyId: Number(user.companyId ?? user.company_id ?? session?.company?.id ?? 0),
      roleId: String(user.roleId ?? user.role_id ?? ""),
      roleName: String(user.roleName ?? user.role_name ?? ""),
      status: String(user.status ?? "Active"),
      password: "",
    });
    setUserModal("edit");
  };

  const saveUser = async () => {
    const body: Record<string, unknown> = {
      fullName: userForm.fullName,
      email: userForm.email,
      companyId: userForm.companyId,
      roleId: userForm.roleId ? Number(userForm.roleId) : undefined,
      roleName: userForm.roleName,
      status: userForm.status,
    };
    if (userModal === "create") {
      body.password = userForm.password;
      await createUser.mutateAsync(body);
    } else if (userModal === "edit" && userForm.id) {
      if (userForm.password.trim()) body.password = userForm.password;
      await updateUser.mutateAsync({ id: Number(userForm.id), body });
    }
    setUserModal(null);
  };

  const saveRole = async () => {
    if (roleModal?.id) {
      await updateRole.mutateAsync({ id: Number(roleModal.id), body: roleForm });
    } else {
      await createRole.mutateAsync(roleForm);
    }
    setRoleModal(null);
  };

  const exportUsers = async () => {
    downloadCsv("admin-users.csv", filteredUsers, ["fullName", "email", "companyName", "roleName", "status"]);
    setPermissionsExportNotice(`Exported ${filteredUsers.length} user rows to CSV.`);
    window.setTimeout(() => setPermissionsExportNotice(null), 3500);
  };

  const exportRoles = async () => {
    downloadCsv("admin-roles.csv", roles, ["name", "scope", "userCount"]);
  };

  const openRoleEditor = (role: AnyRecord) => {
    setRoleModal(role);
    setRoleForm({
      name: String(role.name ?? ""),
      permissions: permissionList(role.permissions ?? role.permissions_json),
    });
  };

  const openCreateRole = () => {
    setRoleModal({});
    setRoleForm({ name: "", permissions: [] });
  };

  return (
    <div className="iam flex h-full flex-col gap-6 overflow-y-auto">
      <PageHeader
        eyebrow="Governance"
        title="Users & Roles"
        description="Manage the people, roles, permissions and audit posture of this workspace."
        actions={
          <>
            <button className="btn-ghost" onClick={() => setTab("audit")} disabled={!canViewAudit} title={!canViewAudit ? "You do not have permission to perform this action." : undefined}>
              <KeyRound className="h-4 w-4" />
              Audit Logs
            </button>
            <button className="btn-ghost" onClick={() => setTab("settings")} disabled={!canViewSettings} title={!canViewSettings ? "You do not have permission to perform this action." : undefined}>
              <ShieldCheck className="h-4 w-4" />
              Settings
            </button>
            <button className="btn-primary" onClick={() => setTab("users")} disabled={!canViewUsers} title={!canViewUsers ? "You do not have permission to perform this action." : undefined}>
              <LayoutDashboard className="h-4 w-4" />
              Open Users
            </button>
          </>
        }
      />

      {permissionsExportNotice && <div className="rounded-xl border border-emerald-400/30 bg-emerald-50 px-4 py-3 text-sm text-emerald-700">{permissionsExportNotice}</div>}

      {overviewQ.isLoading ? <LoadingState /> : overviewQ.isError ? <ErrorState message="Could not load admin overview." /> : (
        <div className="grid gap-4 sm:grid-cols-2 md:grid-cols-3 xl:grid-cols-6">
          {[
            { label: "Total Users", value: overviewQ.data?.totalUsers ?? 0, icon: <Users className="h-4 w-4" /> },
            { label: "Active Users", value: overviewQ.data?.activeUsers ?? 0, icon: <Users className="h-4 w-4" /> },
            { label: "Tenant Admins", value: overviewQ.data?.tenantAdmins ?? 0, icon: <UserCog className="h-4 w-4" /> },
            { label: "Roles", value: overviewQ.data?.roles ?? 0, icon: <KeyRound className="h-4 w-4" /> },
            { label: "Audit Events Today", value: overviewQ.data?.recentAuditEvents ?? 0, icon: <ShieldCheck className="h-4 w-4" /> },
            { label: "Permissions", value: overviewQ.data?.permissionCoverage ?? permissions.length, icon: <ShieldCheck className="h-4 w-4" /> },
          ].map((card) => (
            <div key={card.label} className="iam-stat min-w-0">
              <div className="flex items-start justify-between gap-2">
                <p className="min-w-0 truncate text-[11px] font-bold uppercase tracking-[0.14em] text-slate-500" title={card.label}>{card.label}</p>
                <div className="shrink-0 rounded-xl border border-white/70 bg-white p-2 text-teal-600 shadow-[-2px_-2px_5px_rgba(255,255,255,.9),3px_4px_8px_rgba(141,157,184,.24)]">{card.icon}</div>
              </div>
              <div className="mt-2 text-3xl font-bold tracking-tight text-slate-900">{card.value}</div>
            </div>
          ))}
        </div>
      )}

      <div className="flex flex-wrap gap-2 border-b border-slate-200 pb-px">
        {TAB_OPTIONS.map((option) => (
          <button
            key={option.key}
            onClick={() => setTab(option.key)}
            disabled={
              (option.key === "users" && !canViewUsers) ||
              (option.key === "roles" && !canViewRoles) ||
              (option.key === "permissions" && !(canViewUsers || canViewRoles)) ||
              (option.key === "access" && !canViewAccessReviews) ||
              (option.key === "settings" && !canViewSettings) ||
              (option.key === "audit" && !canViewAudit)
            }
            title={
              (option.key === "users" && !canViewUsers) ||
              (option.key === "roles" && !canViewRoles) ||
              (option.key === "permissions" && !(canViewUsers || canViewRoles)) ||
              (option.key === "access" && !canViewAccessReviews) ||
              (option.key === "settings" && !canViewSettings) ||
              (option.key === "audit" && !canViewAudit)
                ? "You do not have permission to perform this action."
                : undefined
            }
            className={`rounded-t-lg px-4 py-2 text-sm font-semibold transition ${
              tab === option.key ? "border border-b-0 border-teal-300 bg-teal-50 text-teal-700" : "text-slate-500 hover:text-slate-700"
            } disabled:cursor-not-allowed disabled:opacity-40`}
          >
            {option.label}
          </button>
        ))}
      </div>

      {tab === "dashboard" && (
        <div className="grid gap-4 xl:grid-cols-[1.2fr_0.8fr]">
            <div className="iam-card space-y-3 p-5">
              <div className="flex items-center justify-between gap-3">
                <h2 className="text-lg font-bold text-slate-900">Admin Activity</h2>
                <button className="btn-ghost h-9 px-3 shrink-0" onClick={() => setTab("audit")} disabled={!canViewAudit}>Open audit trail</button>
              </div>
            {((Array.isArray(auditLogsQ.data) ? auditLogsQ.data : []) as AnyRecord[]).slice(0, 6).map((entry: AnyRecord) => (
              <div key={String(entry.id)} className="iam-kv">
                <div className="min-w-0">
                  <p className="font-semibold text-slate-900 truncate">{String(entry.actionName ?? entry.action_name ?? "Action")}</p>
                  <p className="mt-1 text-xs text-slate-500 truncate">{String(entry.actorName ?? entry.actor_name ?? "system")} • {String(entry.entityName ?? entry.entity_name ?? "Admin")}</p>
                </div>
                <div className="shrink-0"><StatusBadge status={String(entry.severity ?? "Info")} /></div>
              </div>
            ))}
            {((Array.isArray(auditLogsQ.data) ? auditLogsQ.data : []) as AnyRecord[]).length === 0 && !auditLogsQ.isLoading && (
              <EmptyState title="No recent admin activity" subtitle="IAM actions in this tenant will appear here as they happen." />
            )}
          </div>
          <div className="space-y-4">
            <div className="iam-card p-5">
              <h3 className="font-bold text-slate-900">Quick Actions</h3>
              <div className="mt-4 grid gap-2">
                <button className="btn-primary" onClick={() => setTab("users")} disabled={!canViewUsers}>Manage Users</button>
                <button className="btn-ghost" onClick={() => setTab("roles")} disabled={!canViewRoles}>Review Roles</button>
                <button className="btn-ghost" onClick={() => setTab("permissions")} disabled={!(canViewUsers || canViewRoles)}>View Permissions</button>
                <button className="btn-ghost" onClick={() => setTab("settings")} disabled={!canViewSettings}>Open Settings</button>
              </div>
            </div>
            <div className="iam-card p-5">
              <h3 className="font-bold text-slate-900">Current Tenant</h3>
              <p className="mt-2 text-sm text-slate-600 truncate">{String(session?.company?.name ?? "Tenant")}</p>
              <p className="mt-1 text-xs text-slate-500 truncate">Role: {String(session?.role ?? "Unknown")}</p>
            </div>
          </div>
        </div>
      )}

      {tab === "users" && (
        <div className="space-y-4">
          <div className="flex flex-wrap items-center gap-2">
            <div className="relative flex-1 min-w-[220px]">
              <Search className="absolute left-3 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-slate-500" />
              <input className="field w-full pl-9" placeholder="Search users..." value={search} onChange={(e) => setSearch(e.target.value)} />
            </div>
            <select className="field" value={roleFilter} onChange={(e) => setRoleFilter(e.target.value)}>
              <option value="">All roles</option>
              {roleOptions.map((role) => <option key={role.id} value={role.name}>{role.name}</option>)}
            </select>
            <select className="field" value={statusFilter} onChange={(e) => setStatusFilter(e.target.value)}>
              <option value="">All statuses</option>
              {["Active", "Inactive", "Pending"].map((status) => <option key={status} value={status}>{status}</option>)}
            </select>
            <button className="btn-ghost" onClick={() => { setSearch(""); setRoleFilter(""); setStatusFilter(""); }}>
              <X className="h-4 w-4" />
              Clear
            </button>
            <button className="btn-ghost" onClick={exportUsers} disabled={!canExportReports} title={!canExportReports ? "You do not have permission to perform this action." : undefined}>
              <Download className="h-4 w-4" />
              Export
            </button>
            <button className="btn-primary" onClick={openCreateUser} disabled={!canCreateUsers}>
              <Plus className="h-4 w-4" />
              Add User
            </button>
          </div>

          {usersQ.isLoading ? <LoadingState /> : usersQ.isError ? <ErrorState message="Could not load users." /> : users.length === 0 ? (
            <EmptyState title="No users found" subtitle="Try another filter or add a new user for this tenant." />
          ) : (
            <div className="iam-card overflow-x-auto">
              <table className="w-full text-sm">
                <thead>
                  <tr className="border-b border-slate-200">
                    {["User", "Company", "Role", "Security", "Status", "Last Login", "Actions"].map((header) => (
                      <th key={header} className="px-4 py-3 text-left text-[10px] font-bold uppercase tracking-widest text-slate-500 whitespace-nowrap">{header}</th>
                    ))}
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {filteredUsers.map((user: AnyRecord) => {
                    const name = String(user.fullName ?? user.full_name ?? "User");
                    return (
                    <tr key={String(user.id)} className="transition hover:bg-white/60">
                      <td className="px-4 py-3 max-w-[260px]">
                        <button className="flex w-full min-w-0 items-center gap-3 text-left" onClick={() => setSelectedUser(user)}>
                          <Avatar name={name} />
                          <span className="min-w-0">
                            <p className="font-semibold text-slate-900 truncate" title={name}>{name}</p>
                            <p className="text-xs text-slate-400 truncate" title={String(user.email ?? "")}>{String(user.email ?? "")}</p>
                          </span>
                        </button>
                      </td>
                      <td className="px-4 py-3 text-slate-700 max-w-[160px]"><span className="block truncate" title={String(user.companyName ?? user.company_name ?? "—")}>{String(user.companyName ?? user.company_name ?? "—")}</span></td>
                      <td className="px-4 py-3 max-w-[150px]">
                        <span className="iam-chip"><span>{String(user.roleName ?? user.role_name ?? "—")}</span></span>
                      </td>
                      <td className="px-4 py-3"><MfaBadge status={user.mfaStatus ?? user.mfa_status} /></td>
                      <td className="px-4 py-3"><StatusBadge status={String(user.status ?? "Active")} /></td>
                      <td className="px-4 py-3 text-xs text-slate-400 whitespace-nowrap">{user.lastLoginAt ? new Date(String(user.lastLoginAt)).toLocaleString() : "Never"}</td>
                      <td className="px-4 py-3">
                        <div className="flex items-center gap-2">
                          <button className="btn-ghost h-8 px-3" onClick={() => setSelectedUser(user)}>View</button>
                          <button className="btn-ghost h-8 px-3" onClick={() => openEditUser(user)} disabled={!canUpdateUsers}>Edit</button>
                          <button
                            className="btn-ghost h-8 px-3 text-rose-600 hover:text-rose-700"
                            onClick={async () => {
                              if (!window.confirm(`Deactivate ${String(user.fullName ?? user.email)}?`)) return;
                              await deleteUser.mutateAsync(Number(user.id));
                            }}
                            disabled={!canDeleteUsers}
                            title={!canDeleteUsers ? "You do not have permission to perform this action." : "Deactivate user"}
                          >
                            <Trash2 className="mr-1 h-3.5 w-3.5" />
                            Deactivate
                          </button>
                        </div>
                      </td>
                    </tr>
                  );})}
                </tbody>
              </table>
            </div>
          )}
        </div>
      )}

      {tab === "roles" && (
        <div className="space-y-4">
          <div className="flex items-center justify-between gap-3">
            <p className="text-sm text-slate-500">Roles list and permission bundles.</p>
            <div className="flex items-center gap-2">
              <button className="btn-ghost" onClick={exportRoles} disabled={!canExportReports} title={!canExportReports ? "You do not have permission to perform this action." : undefined}>Export</button>
              <button className="btn-primary" onClick={openCreateRole} disabled={!canCreateRoles}>
                <Plus className="h-4 w-4" /> Create role
              </button>
            </div>
          </div>
          {rolesQ.isLoading ? <LoadingState /> : rolesQ.isError ? <ErrorState message="Could not load roles." /> : (
            <div className="grid gap-4 xl:grid-cols-2">
              {roles.map((role: AnyRecord) => (
                <div key={String(role.id)} className="iam-card p-5 min-w-0">
                  <div className="flex items-start justify-between gap-3">
                    <div className="min-w-0">
                      <h3 className="font-bold text-slate-900 truncate" title={String(role.name)}>{String(role.name)}</h3>
                      <p className="mt-1 text-xs text-slate-500">{String(role.userCount ?? 0)} users assigned</p>
                    </div>
                    <button className="btn-ghost h-8 px-3 shrink-0" onClick={() => openRoleEditor(role)} disabled={!canUpdateRoles || Boolean(role.isSystem ?? role.is_system)} title={Boolean(role.isSystem ?? role.is_system) ? "Built-in templates are immutable; create a tenant role to customize access." : undefined}>
                      {Boolean(role.isSystem ?? role.is_system) ? "Protected" : "Edit"}
                    </button>
                  </div>
                  <div className="mt-4 flex flex-wrap gap-2">
                    {permissionList(role.permissions ?? role.permissions_json).slice(0, 8).map((permission) => (
                      <span key={permission} className="iam-chip"><span>{permission}</span></span>
                    ))}
                    {permissionList(role.permissions ?? role.permissions_json).length > 8 && (
                      <span className="iam-chip !text-slate-400">
                        <span>+{permissionList(role.permissions ?? role.permissions_json).length - 8} more</span>
                      </span>
                    )}
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      )}

      {tab === "permissions" && (
        <div className="space-y-4">
          {permissionsQ.isError ? (
            <ErrorState message="The live permissions endpoint failed, so the admin view is not showing a seed-backed replacement." />
          ) : permissionsQ.isLoading && permissions.length === 0 ? (
            <div className="space-y-3">
              <p className="text-sm text-slate-500">Fetching the live RBAC catalog.</p>
              <LoadingState />
            </div>
          ) : (
            <>
          <div className="flex flex-wrap items-center gap-3">
            <p className="text-sm text-slate-500">Canonical permission catalog used by the RBAC layer.</p>
          </div>
          <div className="grid gap-4 xl:grid-cols-2">
            {permissionsByGroup(permissions).map((group) => (
              <div key={group.title} className="iam-card p-5 min-w-0">
                <div className="flex items-center justify-between gap-3">
                  <h3 className="font-bold text-slate-900 truncate">{group.title}</h3>
                  <StatusBadge status={`${group.permissions.length} perms`} />
                </div>
                <div className="mt-4 flex flex-wrap gap-2">
                  {group.permissions.map((permission) => (
                    <span key={permission} className="iam-chip"><span>{permission}</span></span>
                  ))}
                </div>
              </div>
            ))}
          </div>
            </>
          )}
        </div>
      )}

      {tab === "access" && (
        <div className="grid gap-4 xl:grid-cols-[0.75fr_1.25fr]">
          <div className="space-y-4">
            {canManageAccessReviews && (
              <div className="iam-card space-y-3 p-5">
                <div>
                  <h2 className="font-bold text-slate-900">Start access certification</h2>
                  <p className="mt-1 text-xs text-slate-500">Snapshots every active user and their current role permissions for this tenant.</p>
                </div>
                <input className="field w-full" value={reviewForm.title} onChange={(e) => setReviewForm((f) => ({ ...f, title: e.target.value }))} placeholder="Quarterly privileged access review" />
                <textarea className="field min-h-20 w-full" value={reviewForm.description} onChange={(e) => setReviewForm((f) => ({ ...f, description: e.target.value }))} placeholder="Purpose and reviewer guidance" />
                <div><label className="label">Due date</label><input className="field w-full" type="date" value={reviewForm.dueDate} onChange={(e) => setReviewForm((f) => ({ ...f, dueDate: e.target.value }))} /></div>
                <button
                  className="btn-primary w-full"
                  disabled={!reviewForm.title.trim() || createAccessReview.isPending}
                  onClick={async () => {
                    const result = await createAccessReview.mutateAsync({ ...reviewForm, reviewerUserId: Number(session?.user?.id) });
                    setReviewForm({ title: "", description: "", dueDate: "" });
                    setSelectedReviewId(Number(result.id));
                  }}
                ><Plus className="h-4 w-4" />{createAccessReview.isPending ? "Creating…" : "Create review"}</button>
              </div>
            )}
            <div className="iam-card overflow-hidden">
              <div className="border-b border-slate-200 px-5 py-4"><h2 className="font-bold text-slate-900">Review campaigns</h2></div>
              {accessReviewsQ.isLoading ? <LoadingState /> : accessReviewsQ.isError ? <ErrorState message="Could not load access reviews." /> : (accessReviewsQ.data ?? []).length === 0 ? (
                <div className="p-5"><EmptyState title="No access reviews" subtitle="Create the first tenant access certification campaign." /></div>
              ) : (accessReviewsQ.data ?? []).map((review) => (
                <button key={review.id} className={`w-full border-b border-slate-100 px-5 py-4 text-left transition hover:bg-white/60 ${selectedReviewId === Number(review.id) ? "bg-teal-50/70" : ""}`} onClick={() => setSelectedReviewId(Number(review.id))}>
                  <div className="flex items-start justify-between gap-3"><p className="font-semibold text-slate-900 truncate min-w-0">{String(review.title)}</p><div className="shrink-0"><StatusBadge status={String(review.status)} /></div></div>
                  <p className="mt-2 text-xs text-slate-500">{Number(review.itemsPending ?? review.items_pending ?? 0)} pending · {Number(review.itemsApproved ?? review.items_approved ?? 0)} approved · {Number(review.itemsRevoked ?? review.items_revoked ?? 0)} revoked</p>
                </button>
              ))}
            </div>
          </div>
          <div className="iam-card min-h-80 overflow-hidden">
            {!selectedReviewId ? (
              <div className="flex min-h-80 flex-col items-center justify-center p-8 text-center"><ClipboardCheck className="h-10 w-10 text-teal-500" /><h2 className="mt-3 font-bold text-slate-900">Select a review</h2><p className="mt-1 text-sm text-slate-500">Inspect each user’s snapshotted role and make an explicit retain or revoke decision.</p></div>
            ) : accessReviewQ.isLoading ? <LoadingState /> : accessReviewQ.isError ? <ErrorState message="Could not load this access review." /> : (
              <div>
                <div className="flex flex-wrap items-start justify-between gap-3 border-b border-slate-200 p-5">
                  <div className="min-w-0"><h2 className="text-lg font-bold text-slate-900 truncate">{String(accessReviewQ.data?.title ?? "Access review")}</h2><p className="mt-1 text-sm text-slate-500">{String(accessReviewQ.data?.description ?? "Tenant access certification")}</p></div>
                  <button className="btn-primary shrink-0" disabled={!canManageAccessReviews || Number(accessReviewQ.data?.itemsPending ?? accessReviewQ.data?.items_pending ?? 0) > 0 || String(accessReviewQ.data?.status) === "completed" || completeAccessReview.isPending} onClick={() => completeAccessReview.mutateAsync(selectedReviewId)}><Check className="h-4 w-4" />Complete review</button>
                </div>
                <div className="overflow-x-auto">
                  <table className="w-full text-sm"><thead><tr className="border-b border-slate-200">{["User", "Role", "Permissions", "Decision"].map((h) => <th key={h} className="px-4 py-3 text-left text-[10px] font-bold uppercase tracking-widest text-slate-500">{h}</th>)}</tr></thead>
                    <tbody className="divide-y divide-slate-100">{(((accessReviewQ.data?.items as AnyRecord[] | undefined) ?? [])).map((item) => {
                      const pending = String(item.status) === "pending";
                      return <tr key={String(item.id)}><td className="px-4 py-3 max-w-[220px]"><p className="font-semibold text-slate-900 truncate">{String(item.targetUserName ?? item.target_user_name ?? "User")}</p><p className="text-xs text-slate-500 truncate">{String(item.targetUserEmail ?? item.target_user_email ?? "")}</p></td><td className="px-4 py-3 text-slate-700 max-w-[140px]"><span className="block truncate">{String(item.roleName ?? item.role_name ?? "—")}</span></td><td className="px-4 py-3 text-xs text-slate-500 whitespace-nowrap">{permissionList(item.permissionsSnapshot ?? item.permissions_snapshot).length} granted</td><td className="px-4 py-3">{pending ? <div className="flex gap-2"><button className="btn-ghost h-8 px-3" disabled={!canManageAccessReviews || decideAccessReviewItem.isPending} onClick={() => decideAccessReviewItem.mutateAsync({ reviewId: selectedReviewId, itemId: Number(item.id), decision: "approve" })}>Retain</button><button className="btn-ghost h-8 px-3 text-rose-600" disabled={!canManageAccessReviews || decideAccessReviewItem.isPending} onClick={() => decideAccessReviewItem.mutateAsync({ reviewId: selectedReviewId, itemId: Number(item.id), decision: "revoke", notes: "Access removal required by reviewer" })}>Revoke</button></div> : <StatusBadge status={String(item.status)} />}</td></tr>;
                    })}</tbody></table>
                </div>
              </div>
            )}
          </div>
        </div>
      )}

      {tab === "settings" && (
        <div className="iam-card p-5 space-y-4">
          <div className="flex items-center justify-between gap-3">
            <div className="min-w-0">
              <h2 className="text-lg font-bold text-slate-900">Tenant Settings</h2>
              <p className="mt-1 text-sm text-slate-500">Locale and operational preferences for this tenant.</p>
            </div>
            <button className="btn-ghost shrink-0" onClick={() => window.location.assign("/settings")}>Open Settings Page</button>
          </div>
          <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
            {Object.entries(
              // The endpoint returns an array of tenant rows — unwrap the first row,
              // otherwise Object.entries renders "[object Object]" tiles.
              ((Array.isArray(localeQ.data) ? (localeQ.data as AnyRecord[])[0] : localeQ.data) as AnyRecord | undefined) ?? {}
            )
              .filter(([key]) => !["id", "tenant_id", "company_id", "created_at", "updated_at"].includes(key))
              .slice(0, 6)
              .map(([key, value]) => (
              <div key={key} className="iam-kv flex-col !items-start gap-1 min-w-0">
                <p className="text-[10px] font-bold uppercase tracking-widest text-slate-500 truncate w-full">{key.replace(/_/g, " ")}</p>
                <p className="text-sm text-slate-700 break-words w-full">{String(value ?? "—")}</p>
              </div>
            ))}
          </div>
          <button className="btn-primary w-fit" onClick={() => window.location.assign("/settings")} disabled={!canViewSettings || !canUpdateSettings}>
            <ShieldCheck className="h-4 w-4" />
            Update settings
          </button>
        </div>
      )}

      {tab === "audit" && (
        <div className="space-y-4">
          <div className="flex items-center justify-between gap-3">
            <p className="text-sm text-slate-500">Recent audit activity and export requests.</p>
            <button className="btn-ghost shrink-0" onClick={() => window.location.assign("/audit-logs")}>Open Audit Logs</button>
          </div>
          <div className="grid gap-4 xl:grid-cols-[1.3fr_0.7fr]">
            <div className="iam-card overflow-x-auto">
              <table className="w-full text-sm">
                <thead>
                  <tr className="border-b border-slate-200">
                    {["Action", "Actor", "Entity", "Severity"].map((header) => (
                      <th key={header} className="px-4 py-3 text-left text-[10px] font-bold uppercase tracking-widest text-slate-500">{header}</th>
                    ))}
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {((Array.isArray(auditLogsQ.data) ? auditLogsQ.data : []) as AnyRecord[]).slice(0, 8).map((log: AnyRecord) => (
                    <tr key={String(log.id)}>
                      <td className="px-4 py-3 text-slate-900 max-w-[220px]"><span className="block truncate" title={String(log.actionName ?? log.action_name ?? "Action")}>{String(log.actionName ?? log.action_name ?? "Action")}</span></td>
                      <td className="px-4 py-3 text-slate-600 max-w-[160px]"><span className="block truncate">{String(log.actorName ?? log.actor_name ?? "system")}</span></td>
                      <td className="px-4 py-3 text-slate-600 max-w-[160px]"><span className="block truncate">{String(log.entityName ?? log.entity_name ?? "—")}</span></td>
                      <td className="px-4 py-3"><StatusBadge status={String(log.severity ?? "Info")} /></td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <div className="iam-card p-5 space-y-3">
              <h3 className="font-bold text-slate-900">Export Requests</h3>
              {((Array.isArray(auditExportsQ.data) ? auditExportsQ.data : []) as AnyRecord[]).slice(0, 4).map((entry: AnyRecord) => (
                <div key={String(entry.id)} className="iam-kv">
                  <p className="text-sm font-semibold text-slate-900 truncate min-w-0">{String(entry.requested_by_name ?? "—")}</p>
                  <p className="text-xs text-slate-500 shrink-0">{String(entry.status ?? "Pending")}</p>
                </div>
              ))}
              <button
                className="btn-ghost w-full"
                onClick={() => createAuditExport.mutate({
                  requestedByName: String(session?.user?.fullName ?? session?.user?.full_name ?? session?.user?.email ?? ""),
                  exportFormat: "CSV",
                })}
                disabled={!canExportReports}
                title={!canExportReports ? "You do not have permission to perform this action." : undefined}
              >
                Request audit export
              </button>
            </div>
          </div>
        </div>
      )}

      {selectedUser && (
        <div className="fixed inset-0 z-50 flex justify-end bg-slate-900/30 backdrop-blur-sm" onClick={() => setSelectedUser(null)}>
          <aside className="iam iam-drawer max-w-lg p-6" onClick={(e) => e.stopPropagation()} role="dialog" aria-label="User detail">
            <button className="float-right icon-btn" onClick={() => setSelectedUser(null)} aria-label="Close user detail"><X className="h-4 w-4" /></button>
            <p className="text-[11px] font-bold uppercase tracking-[0.2em] text-teal-700">User Detail</p>
            <div className="mt-4 flex items-center gap-3 min-w-0">
              <Avatar name={String(selectedUser.fullName ?? selectedUser.full_name ?? "User")} />
              <div className="min-w-0">
                <h2 className="text-xl font-bold text-slate-900 truncate">{String(selectedUser.fullName ?? selectedUser.full_name ?? "User")}</h2>
                <p className="text-xs text-slate-500 truncate">{String(selectedUser.email ?? "")}</p>
              </div>
            </div>
            <div className="mt-6 space-y-2">
              {([
                ["Email", selectedUser.email],
                ["Company", selectedUser.companyName ?? selectedUser.company_name],
                ["Role", selectedUser.roleDisplayName ?? selectedUser.roleName ?? selectedUser.role_name],
                ["Status", selectedUser.status],
                ["Last login", selectedUser.lastLoginAt ?? selectedUser.last_login_at],
                ["MFA", selectedUser.mfaStatus ?? selectedUser.mfa_status],
                ["Created", selectedUser.createdAt ?? selectedUser.created_at],
              ] as Array<[string, unknown]>).map(([key, value]) => (
                <div key={key} className="iam-kv">
                  <p className="text-[11px] uppercase tracking-[0.16em] text-slate-500 mt-0.5 shrink-0">{key}</p>
                  <p className="text-right text-sm font-medium text-slate-800 break-all min-w-0">{value == null ? "—" : typeof value === "object" ? JSON.stringify(value) : String(value)}</p>
                </div>
              ))}
            </div>
            {canUpdateUsers && (
              <button className="btn-primary mt-6 w-full" onClick={() => { openEditUser(selectedUser); setSelectedUser(null); }}>
                Edit User
              </button>
            )}
          </aside>
        </div>
      )}

      {userModal && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/35 backdrop-blur-sm p-4">
          <div className="iam iam-card w-full max-w-2xl space-y-4 p-6">
            <div className="flex items-center justify-between">
              <h2 className="font-bold text-slate-900">{userModal === "create" ? "Add User" : "Edit User"}</h2>
              <button className="icon-btn" onClick={() => setUserModal(null)} aria-label="Close"><X className="h-4 w-4" /></button>
            </div>
              <div className="grid gap-3 md:grid-cols-2">
              <div><label className="label">Full Name</label><input className="field w-full" value={String(userForm.fullName ?? "")} onChange={(e) => setUserForm((f) => ({ ...f, fullName: e.target.value }))} /></div>
              <div><label className="label">Email</label><input className="field w-full" value={String(userForm.email ?? "")} onChange={(e) => setUserForm((f) => ({ ...f, email: e.target.value }))} /></div>
              <div>
                <label className="label">Role</label>
                <select className="field w-full" value={String(userForm.roleId ?? "")} onChange={(e) => {
                  const role = roleOptions.find((option) => String(option.id) === e.target.value);
                  setUserForm((f) => ({ ...f, roleId: e.target.value, roleName: String(role?.name ?? "") }));
                }}>
                  <option value="" disabled>Select a role</option>
                  {roleOptions.map((role) => <option key={role.id} value={String(role.id)}>{role.name}</option>)}
                </select>
              </div>
              <div>
                <label className="label">Status</label>
                <select className="field w-full" value={String(userForm.status ?? "Active")} onChange={(e) => setUserForm((f) => ({ ...f, status: e.target.value }))}>
                  {["Active", "Inactive", "Pending"].map((status) => <option key={status} value={status}>{status}</option>)}
                </select>
              </div>
              <div>
                <label className="label">Password</label>
                <input className="field w-full" type="password" value={String(userForm.password ?? "")} onChange={(e) => setUserForm((f) => ({ ...f, password: e.target.value }))} placeholder={userModal === "create" ? "Set initial password" : "Leave blank to keep current password"} />
              </div>
              <div>
                <label className="label">Company ID</label>
                <input className="field w-full" type="number" value={Number(userForm.companyId || 0)} onChange={(e) => setUserForm((f) => ({ ...f, companyId: Number(e.target.value) }))} disabled={!String(session?.role ?? "").match(/super/i)} />
              </div>
            </div>
            <div className="flex gap-2 pt-2">
              <button type="button" className="btn-ghost flex-1" onClick={() => setUserModal(null)}>Cancel</button>
              <button type="button" className="btn-primary flex-1" onClick={saveUser} disabled={!userForm.fullName.trim() || !userForm.email.trim() || !userForm.roleId || (userModal === "create" ? !canCreateUsers || !String(userForm.password ?? "").trim() : !canUpdateUsers)}>
                Save User
              </button>
            </div>
          </div>
        </div>
      )}

      {roleModal && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/35 backdrop-blur-sm p-4">
          <div className="iam iam-card w-full max-w-3xl space-y-4 p-6">
            <div className="flex items-center justify-between">
              <h2 className="font-bold text-slate-900">{roleModal.id ? "Edit Role" : "Create Role"}</h2>
              <button className="icon-btn" onClick={() => setRoleModal(null)} aria-label="Close"><X className="h-4 w-4" /></button>
            </div>
            <div>
              <div><label className="label">Role Name</label><input className="field w-full" value={String(roleForm.name ?? "")} onChange={(e) => setRoleForm((f) => ({ ...f, name: e.target.value }))} /></div>
            </div>
            <div className="max-h-[48vh] overflow-y-auto rounded-2xl border border-slate-200 bg-slate-50/50 p-4">
              {permissionsByGroup(permissions).map((group) => (
                <div key={group.title} className="mb-4">
                  <p className="mb-2 text-[10px] font-bold uppercase tracking-[0.18em] text-slate-500">{group.title}</p>
                  <div className="grid gap-2 sm:grid-cols-2">
                    {group.permissions.map((permission) => (
                      <label key={permission} className="flex min-w-0 items-center gap-2 rounded-xl border border-slate-100 bg-white px-3 py-2 text-sm text-slate-700 shadow-[-2px_-2px_5px_rgba(255,255,255,.9),2px_3px_6px_rgba(141,157,184,.14)]">
                        <input
                          type="checkbox"
                          className="shrink-0 accent-teal-600"
                          checked={roleForm.permissions.includes(permission)}
                          onChange={(e) => setRoleForm((f) => ({
                            ...f,
                            permissions: e.target.checked
                              ? [...f.permissions, permission]
                              : f.permissions.filter((value) => value !== permission),
                          }))}
                        />
                        <span className="truncate" title={permission}>{permission}</span>
                      </label>
                    ))}
                  </div>
                </div>
              ))}
            </div>
            <div className="flex gap-2 pt-2">
              <button type="button" className="btn-ghost flex-1" onClick={() => setRoleModal(null)}>Cancel</button>
              <button type="button" className="btn-primary flex-1" onClick={saveRole} disabled={!roleForm.name.trim() || (roleModal.id ? !canUpdateRoles : !canCreateRoles)}>{roleModal.id ? "Save Role" : "Create Role"}</button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
