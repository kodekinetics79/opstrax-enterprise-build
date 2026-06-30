import { useMemo, useState } from "react";
import { Download, KeyRound, LayoutDashboard, Plus, Search, ShieldCheck, Trash2, UserCog, Users, X } from "lucide-react";
import { useAuth } from "@/hooks/useAuth";
import { useHasPermission, PermissionDenied } from "@/hooks/usePermission";
import {
  useAdminOverview,
  useAdminPermissions,
  useAdminRoles,
  useAdminUsers,
  useCreateAdminUser,
  useDeleteAdminUser,
  useUpdateAdminRole,
  useUpdateAdminUser,
} from "@/hooks/useAdmin";
import { useAuditExportRequests, useAuditLogs, useCreateAuditExport } from "@/hooks/useBatch7";
import { useLocalizationSettings, useUpdateLocaleSettings } from "@/hooks/useBatch6";
import { adminApi } from "@/services/adminApi";
import { PERMISSIONS } from "@/auth/rbacConfig";
import { EmptyState, ErrorState, LoadingState, PageHeader, StatusBadge } from "@/components/ui";
import type { AnyRecord } from "@/types";

type AdminTab = "dashboard" | "users" | "roles" | "permissions" | "settings" | "audit";

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
  scope: string;
  permissions: string[];
};

const TAB_OPTIONS: Array<{ key: AdminTab; label: string }> = [
  { key: "dashboard", label: "Dashboard" },
  { key: "users", label: "Users" },
  { key: "roles", label: "Roles" },
  { key: "permissions", label: "Permissions" },
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
  return groups
    .map((group) => ({
      ...group,
      permissions: permissions.filter((permission) => permission.startsWith(group.prefix)),
    }))
    .filter((group) => group.permissions.length > 0);
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

export function AdminPage() {
  const { session } = useAuth();
  const hasPermission = useHasPermission();
  const canViewUsers = hasPermission(PERMISSIONS.USERS_VIEW);
  const canCreateUsers = hasPermission(PERMISSIONS.USERS_CREATE);
  const canUpdateUsers = hasPermission(PERMISSIONS.USERS_UPDATE);
  const canDeleteUsers = hasPermission(PERMISSIONS.USERS_DELETE);
  const canViewRoles = hasPermission(PERMISSIONS.ROLES_VIEW);
  const canUpdateRoles = hasPermission(PERMISSIONS.ROLES_UPDATE);
  const canViewSettings = hasPermission(PERMISSIONS.SETTINGS_VIEW);
  const canUpdateSettings = hasPermission(PERMISSIONS.SETTINGS_UPDATE);
  const canViewAudit = hasPermission(PERMISSIONS.AUDIT_VIEW);
  const canExportReports = hasPermission(PERMISSIONS.REPORTS_EXPORT);

  const [tab, setTab] = useState<AdminTab>("dashboard");
  const [search, setSearch] = useState("");
  const [roleFilter, setRoleFilter] = useState("");
  const [statusFilter, setStatusFilter] = useState("");
  const [selectedUser, setSelectedUser] = useState<AnyRecord | null>(null);
  const [userModal, setUserModal] = useState<"create" | "edit" | null>(null);
  const [roleModal, setRoleModal] = useState<AnyRecord | null>(null);
  const [permissionsExportNotice, setPermissionsExportNotice] = useState<string | null>(null);
  const [userForm, setUserForm] = useState<UserFormState>({
    fullName: "",
    email: "",
    companyId: Number(session?.company?.id ?? session?.company?.companyId ?? 1),
    roleId: "",
    roleName: "Tenant Admin",
    status: "Active",
    password: "",
  });
  const [roleForm, setRoleForm] = useState<RoleFormState>({
    name: "",
    scope: "Tenant",
    permissions: [],
  });

  const overviewQ = useAdminOverview();
  const usersQ = useAdminUsers({ search, role: roleFilter, status: statusFilter });
  const rolesQ = useAdminRoles();
  const permissionsQ = useAdminPermissions();
  const localeQ = useLocalizationSettings();
  const auditLogsQ = useAuditLogs(undefined, canViewAudit);
  const auditExportsQ = useAuditExportRequests(canViewAudit);
  const createAuditExport = useCreateAuditExport();

  const createUser = useCreateAdminUser();
  const updateUser = useUpdateAdminUser();
  const deleteUser = useDeleteAdminUser();
  const updateRole = useUpdateAdminRole();
  const updateSettings = useUpdateLocaleSettings();

  const users = usersQ.data ?? [];
  const roles = rolesQ.data ?? [];
  const permissions = permissionsQ.data ?? [];
  const roleOptions = roles;

  const filteredUsers = useMemo(() => users, [users]);

  if (!canViewUsers && !canViewRoles && !canViewSettings && !canViewAudit) {
    return <PermissionDenied permission="users:view" />;
  }

  const openCreateUser = () => {
    setUserForm({
      fullName: "",
      email: "",
      companyId: Number(session?.company?.id ?? session?.company?.companyId ?? 1),
      roleId: "",
      roleName: "Tenant Admin",
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
      companyId: Number(user.companyId ?? user.company_id ?? session?.company?.id ?? 1),
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
      permissionsJson: JSON.stringify(permissionList(roleModal?.permissionsJson ?? [])),
    };
    if (userModal === "create") {
      body.password = userForm.password;
      await createUser.mutateAsync(body);
      await adminApi.auditLog({ actionName: "user.created", entityName: "User", detailsJson: JSON.stringify({ email: userForm.email }) });
    } else if (userModal === "edit" && userForm.id) {
      if (userForm.password.trim()) body.password = userForm.password;
      await updateUser.mutateAsync({ id: Number(userForm.id), body });
      await adminApi.auditLog({ actionName: "user.updated", entityName: "User", entityId: Number(userForm.id), detailsJson: JSON.stringify({ email: userForm.email }) });
    }
    setUserModal(null);
  };

  const saveRole = async () => {
    if (!roleModal?.id) return;
    await updateRole.mutateAsync({
      id: Number(roleModal.id),
      body: {
        name: roleForm.name,
        scope: roleForm.scope,
        permissions: roleForm.permissions,
      },
    });
    await adminApi.auditLog({ actionName: "role.updated", entityName: "Role", entityId: Number(roleModal.id), detailsJson: JSON.stringify({ permissions: roleForm.permissions }) });
    setRoleModal(null);
  };

  const exportUsers = async () => {
    downloadCsv("admin-users.csv", filteredUsers, ["fullName", "email", "companyName", "roleName", "status"]);
    await adminApi.auditLog({
      actionName: "export.action",
      entityName: "Users",
      detailsJson: JSON.stringify({ rows: filteredUsers.length, format: "CSV" }),
    });
    setPermissionsExportNotice(`Exported ${filteredUsers.length} user rows to CSV.`);
    window.setTimeout(() => setPermissionsExportNotice(null), 3500);
  };

  const exportRoles = async () => {
    downloadCsv("admin-roles.csv", roles, ["name", "scope", "userCount"]);
    await adminApi.auditLog({
      actionName: "export.action",
      entityName: "Roles",
      detailsJson: JSON.stringify({ rows: roles.length, format: "CSV" }),
    });
  };

  const openRoleEditor = (role: AnyRecord) => {
    setRoleModal(role);
    setRoleForm({
      name: String(role.name ?? ""),
      scope: String(role.scope ?? "Tenant"),
      permissions: permissionList(role.permissions ?? role.permissions_json),
    });
  };

  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow="Governance"
        title="Admin Console"
        description="Manage users, roles, permissions, settings and audit posture from a single commercial-grade control surface."
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
        <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-6">
          {[
            { label: "Total Users", value: overviewQ.data?.totalUsers ?? 0, status: "Active", icon: <Users className="h-4 w-4" /> },
            { label: "Active Users", value: overviewQ.data?.activeUsers ?? 0, status: "Healthy", icon: <Users className="h-4 w-4" /> },
            { label: "Tenant Admins", value: overviewQ.data?.tenantAdmins ?? 0, status: "Review", icon: <UserCog className="h-4 w-4" /> },
            { label: "Roles", value: overviewQ.data?.roles ?? 0, status: "Active", icon: <KeyRound className="h-4 w-4" /> },
            { label: "Audit Events Today", value: overviewQ.data?.recentAuditEvents ?? 0, status: "Info", icon: <ShieldCheck className="h-4 w-4" /> },
            { label: "Permissions", value: overviewQ.data?.permissionCoverage ?? permissions.length, status: "Active", icon: <ShieldCheck className="h-4 w-4" /> },
          ].map((card) => (
            <div key={card.label} className="panel p-5">
              <div className="flex items-start justify-between">
                <p className="text-[11px] font-bold uppercase tracking-[0.18em] text-slate-500">{card.label}</p>
                <div className="rounded-xl border border-slate-200 bg-slate-100 p-2 text-teal-600">{card.icon}</div>
              </div>
              <div className="mt-3 text-3xl font-bold tracking-tight text-slate-900">{card.value}</div>
              <div className="mt-3"><StatusBadge status={card.status} /></div>
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
              (option.key === "settings" && !canViewSettings) ||
              (option.key === "audit" && !canViewAudit)
            }
            title={
              (option.key === "users" && !canViewUsers) ||
              (option.key === "roles" && !canViewRoles) ||
              (option.key === "permissions" && !(canViewUsers || canViewRoles)) ||
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
            <div className="panel space-y-3 p-5">
              <div className="flex items-center justify-between">
                <h2 className="text-lg font-bold text-slate-900">Admin Activity</h2>
                <button className="btn-ghost h-9 px-3" onClick={() => setTab("audit")} disabled={!canViewAudit}>Open audit trail</button>
              </div>
            {((Array.isArray(auditLogsQ.data) ? auditLogsQ.data : []) as AnyRecord[]).slice(0, 6).map((entry: AnyRecord) => (
              <div key={String(entry.id)} className="rounded-xl border border-slate-100 bg-slate-50 px-4 py-3">
                <div className="flex items-center justify-between gap-3">
                  <p className="font-semibold text-slate-900">{String(entry.actionName ?? entry.action_name ?? "Action")}</p>
                  <StatusBadge status={String(entry.severity ?? "Info")} />
                </div>
                <p className="mt-1 text-xs text-slate-500">{String(entry.actorName ?? entry.actor_name ?? "system")} • {String(entry.entityName ?? entry.entity_name ?? "Admin")}</p>
              </div>
            ))}
          </div>
          <div className="space-y-4">
            <div className="panel p-5">
              <h3 className="font-bold text-slate-900">Quick Actions</h3>
              <div className="mt-4 grid gap-2">
                <button className="btn-primary" onClick={() => setTab("users")} disabled={!canViewUsers}>Manage Users</button>
                <button className="btn-ghost" onClick={() => setTab("roles")} disabled={!canViewRoles}>Review Roles</button>
                <button className="btn-ghost" onClick={() => setTab("permissions")} disabled={!(canViewUsers || canViewRoles)}>View Permissions</button>
                <button className="btn-ghost" onClick={() => setTab("settings")} disabled={!canViewSettings}>Open Settings</button>
              </div>
            </div>
            <div className="panel p-5">
              <h3 className="font-bold text-slate-900">Current Tenant</h3>
              <p className="mt-2 text-sm text-slate-600">{String(session?.company?.name ?? "Tenant")}</p>
              <p className="mt-1 text-xs text-slate-500">Role: {String(session?.role ?? "Unknown")}</p>
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
            <div className="panel overflow-x-auto">
              <table className="w-full text-sm">
                <thead>
                  <tr className="border-b border-slate-200">
                    {["User", "Company", "Role", "Status", "Last Login", "Actions"].map((header) => (
                      <th key={header} className="px-4 py-3 text-left text-[10px] font-bold uppercase tracking-widest text-slate-500">{header}</th>
                    ))}
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {filteredUsers.map((user: AnyRecord) => (
                    <tr key={String(user.id)} className="transition hover:bg-slate-50">
                      <td className="px-4 py-3">
                        <button className="text-left" onClick={() => setSelectedUser(user)}>
                          <p className="font-semibold text-slate-900">{String(user.fullName ?? user.full_name ?? "User")}</p>
                          <p className="text-xs text-slate-400">{String(user.email ?? "")}</p>
                        </button>
                      </td>
                      <td className="px-4 py-3 text-slate-700">{String(user.companyName ?? user.company_name ?? "—")}</td>
                      <td className="px-4 py-3 text-slate-700">{String(user.roleName ?? user.role_name ?? "—")}</td>
                      <td className="px-4 py-3"><StatusBadge status={String(user.status ?? "Active")} /></td>
                      <td className="px-4 py-3 text-xs text-slate-400">{user.lastLoginAt ? new Date(String(user.lastLoginAt)).toLocaleString() : "—"}</td>
                      <td className="px-4 py-3">
                        <div className="flex items-center gap-2">
                          <button className="btn-ghost h-8 px-3" onClick={() => setSelectedUser(user)}>View</button>
                          <button className="btn-ghost h-8 px-3" onClick={() => openEditUser(user)} disabled={!canUpdateUsers}>Edit</button>
                          <button
                            className="btn-ghost h-8 px-3 text-rose-600 hover:text-rose-700"
                            onClick={async () => {
                              if (!window.confirm(`Deactivate ${String(user.fullName ?? user.email)}?`)) return;
                              await deleteUser.mutateAsync(Number(user.id));
                              await adminApi.auditLog({ actionName: "user.deleted", entityName: "User", entityId: Number(user.id), detailsJson: JSON.stringify({ status: "Inactive" }) });
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
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      )}

      {tab === "roles" && (
        <div className="space-y-4">
          <div className="flex items-center justify-between gap-3">
            <p className="text-sm text-slate-400">Roles list and permission bundles.</p>
            <button className="btn-ghost" onClick={exportRoles} disabled={!canExportReports} title={!canExportReports ? "You do not have permission to perform this action." : undefined}>Export</button>
          </div>
          {rolesQ.isLoading ? <LoadingState /> : rolesQ.isError ? <ErrorState message="Could not load roles." /> : (
            <div className="grid gap-4 xl:grid-cols-2">
              {roles.map((role: AnyRecord) => (
                <div key={String(role.id)} className="panel p-5">
                  <div className="flex items-start justify-between gap-3">
                    <div>
                      <h3 className="font-bold text-slate-900">{String(role.name)}</h3>
                      <p className="mt-1 text-xs text-slate-500">{String(role.userCount ?? 0)} users assigned</p>
                    </div>
                    <button className="btn-ghost h-8 px-3" onClick={() => openRoleEditor(role)} disabled={!canUpdateRoles}>Edit</button>
                  </div>
                  <div className="mt-4 flex flex-wrap gap-2">
                    {permissionList(role.permissions ?? role.permissions_json).slice(0, 8).map((permission) => (
                      <span key={permission} className="rounded-full border border-slate-200 bg-slate-50 px-2.5 py-1 text-[10px] font-semibold text-slate-600">
                        {permission}
                      </span>
                    ))}
                    {permissionList(role.permissions ?? role.permissions_json).length > 8 && (
                      <span className="rounded-full border border-white/[0.08] bg-white/[0.03] px-2.5 py-1 text-[10px] font-semibold text-slate-500">
                        +{permissionList(role.permissions ?? role.permissions_json).length - 8} more
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
              <p className="text-sm text-slate-400">Fetching the live RBAC catalog.</p>
              <LoadingState />
            </div>
          ) : (
            <>
          <div className="flex flex-wrap items-center gap-3">
            <p className="text-sm text-slate-400">Canonical permission catalog used by the RBAC layer.</p>
            <button className="btn-ghost" onClick={async () => {
              await adminApi.auditLog({ actionName: "permissions.viewed", entityName: "Permissions", detailsJson: JSON.stringify({ total: permissions.length }) });
            }}>
              Audit view
            </button>
          </div>
          <div className="grid gap-4 xl:grid-cols-2">
            {permissionsByGroup(permissions).map((group) => (
              <div key={group.title} className="panel p-5">
                <div className="flex items-center justify-between gap-3">
                  <h3 className="font-bold text-slate-900">{group.title}</h3>
                  <StatusBadge status={`${group.permissions.length} perms`} />
                </div>
                <div className="mt-4 flex flex-wrap gap-2">
                  {group.permissions.map((permission) => (
                    <span key={permission} className="rounded-full border border-slate-200 bg-slate-50 px-2.5 py-1 text-[10px] font-semibold text-slate-600">{permission}</span>
                  ))}
                </div>
              </div>
            ))}
          </div>
            </>
          )}
        </div>
      )}

      {tab === "settings" && (
        <div className="panel p-5 space-y-4">
          <div className="flex items-center justify-between gap-3">
            <div>
              <h2 className="text-lg font-bold text-slate-900">Tenant Settings</h2>
              <p className="mt-1 text-sm text-slate-500">Locale and operational preferences for this tenant.</p>
            </div>
            <button className="btn-ghost" onClick={() => window.location.assign("/settings")}>Open Settings Page</button>
          </div>
          <div className="grid gap-3 md:grid-cols-2">
            {Object.entries((localeQ.data as AnyRecord | undefined) ?? []).slice(0, 6).map(([key, value]) => (
              <div key={key} className="rounded-xl border border-slate-100 bg-slate-50 px-4 py-3">
                <p className="text-[10px] font-bold uppercase tracking-widest text-slate-500">{key.replace(/_/g, " ")}</p>
                <p className="mt-1 text-sm text-slate-700">{String(value ?? "—")}</p>
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
            <p className="text-sm text-slate-400">Recent audit activity and export requests.</p>
            <button className="btn-ghost" onClick={() => window.location.assign("/audit-logs")}>Open Audit Logs</button>
          </div>
          <div className="grid gap-4 xl:grid-cols-[1.3fr_0.7fr]">
            <div className="panel overflow-x-auto">
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
                      <td className="px-4 py-3 text-slate-900">{String(log.actionName ?? log.action_name ?? "Action")}</td>
                      <td className="px-4 py-3 text-slate-600">{String(log.actorName ?? log.actor_name ?? "system")}</td>
                      <td className="px-4 py-3 text-slate-600">{String(log.entityName ?? log.entity_name ?? "—")}</td>
                      <td className="px-4 py-3"><StatusBadge status={String(log.severity ?? "Info")} /></td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <div className="panel p-5 space-y-3">
              <h3 className="font-bold text-slate-900">Export Requests</h3>
              {((Array.isArray(auditExportsQ.data) ? auditExportsQ.data : []) as AnyRecord[]).slice(0, 4).map((entry: AnyRecord) => (
                <div key={String(entry.id)} className="rounded-xl border border-slate-100 bg-slate-50 px-4 py-3">
                  <p className="text-sm font-semibold text-slate-900">{String(entry.requested_by_name ?? "Admin")}</p>
                  <p className="text-xs text-slate-500">{String(entry.status ?? "Pending")}</p>
                </div>
              ))}
              <button
                className="btn-ghost w-full"
                onClick={() => createAuditExport.mutate({ requestedByName: "Admin", exportFormat: "CSV" })}
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
        <div className="fixed inset-0 z-50 flex justify-end bg-black/55 backdrop-blur-sm" onClick={() => setSelectedUser(null)}>
          <aside className="h-full w-full max-w-lg overflow-y-auto border-l border-white/[0.09] bg-slate-950 p-6 shadow-2xl" onClick={(e) => e.stopPropagation()}>
            <button className="float-right icon-btn" onClick={() => setSelectedUser(null)}><X className="h-4 w-4" /></button>
            <p className="section-title text-teal-300">User Detail</p>
            <h2 className="mt-3 text-2xl font-bold text-white">{String(selectedUser.fullName ?? selectedUser.full_name ?? "User")}</h2>
            <div className="mt-6 space-y-2">
              {Object.entries(selectedUser).slice(0, 20).map(([key, value]) => (
                <div key={key} className="flex items-start justify-between gap-3 rounded-xl border border-white/[0.06] bg-white/[0.02] px-4 py-2.5">
                  <p className="text-[11px] uppercase tracking-[0.16em] text-slate-500 mt-0.5">{key.replace(/_/g, " ")}</p>
                  <p className="text-right text-sm text-slate-200 break-all">{value == null ? "—" : typeof value === "object" ? JSON.stringify(value) : String(value)}</p>
                </div>
              ))}
            </div>
          </aside>
        </div>
      )}

      {userModal && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-sm">
          <div className="panel w-full max-w-2xl space-y-4 p-6">
            <div className="flex items-center justify-between">
              <h2 className="font-bold text-slate-900">{userModal === "create" ? "Add User" : "Edit User"}</h2>
              <button className="icon-btn" onClick={() => setUserModal(null)}><X className="h-4 w-4" /></button>
            </div>
              <div className="grid gap-3 md:grid-cols-2">
              <div><label className="label">Full Name</label><input className="field w-full" value={String(userForm.fullName ?? "")} onChange={(e) => setUserForm((f) => ({ ...f, fullName: e.target.value }))} /></div>
              <div><label className="label">Email</label><input className="field w-full" value={String(userForm.email ?? "")} onChange={(e) => setUserForm((f) => ({ ...f, email: e.target.value }))} /></div>
              <div>
                <label className="label">Role</label>
                <select className="field w-full" value={String(userForm.roleName ?? "")} onChange={(e) => setUserForm((f) => ({ ...f, roleName: e.target.value, roleId: "" }))}>
                  {roleOptions.map((role) => <option key={role.id} value={role.name}>{role.name}</option>)}
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
                <input className="field w-full" type="number" value={Number(userForm.companyId ?? 1)} onChange={(e) => setUserForm((f) => ({ ...f, companyId: Number(e.target.value) }))} disabled={!String(session?.role ?? "").match(/super/i)} />
              </div>
            </div>
            <div className="flex gap-2 pt-2">
              <button type="button" className="btn-ghost flex-1" onClick={() => setUserModal(null)}>Cancel</button>
              <button type="button" className="btn-primary flex-1" onClick={saveUser} disabled={userModal === "create" ? !canCreateUsers || !String(userForm.password ?? "").trim() : !canUpdateUsers}>
                Save User
              </button>
            </div>
          </div>
        </div>
      )}

      {roleModal && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-sm">
          <div className="panel w-full max-w-3xl space-y-4 p-6">
            <div className="flex items-center justify-between">
              <h2 className="font-bold text-slate-900">Edit Role</h2>
              <button className="icon-btn" onClick={() => setRoleModal(null)}><X className="h-4 w-4" /></button>
            </div>
            <div className="grid gap-3 md:grid-cols-2">
              <div><label className="label">Role Name</label><input className="field w-full" value={String(roleForm.name ?? "")} onChange={(e) => setRoleForm((f) => ({ ...f, name: e.target.value }))} /></div>
              <div>
                <label className="label">Scope</label>
                <select className="field w-full" value={String(roleForm.scope ?? "Tenant")} onChange={(e) => setRoleForm((f) => ({ ...f, scope: e.target.value }))}>
                  {["Platform", "Tenant", "Customer", "Partner"].map((scope) => <option key={scope} value={scope}>{scope}</option>)}
                </select>
              </div>
            </div>
            <div className="max-h-[48vh] overflow-y-auto rounded-2xl border border-slate-200 bg-slate-50/50 p-4">
              {permissionsByGroup(permissions).map((group) => (
                <div key={group.title} className="mb-4">
                  <p className="mb-2 text-[10px] font-bold uppercase tracking-[0.18em] text-slate-500">{group.title}</p>
                  <div className="grid gap-2 sm:grid-cols-2">
                    {group.permissions.map((permission) => (
                      <label key={permission} className="flex items-center gap-2 rounded-xl border border-slate-100 bg-white px-3 py-2 text-sm text-slate-700">
                        <input
                          type="checkbox"
                          checked={roleForm.permissions.includes(permission)}
                          onChange={(e) => setRoleForm((f) => ({
                            ...f,
                            permissions: e.target.checked
                              ? [...f.permissions, permission]
                              : f.permissions.filter((value) => value !== permission),
                          }))}
                        />
                        <span>{permission}</span>
                      </label>
                    ))}
                  </div>
                </div>
              ))}
            </div>
            <div className="flex gap-2 pt-2">
              <button type="button" className="btn-ghost flex-1" onClick={() => setRoleModal(null)}>Cancel</button>
              <button type="button" className="btn-primary flex-1" onClick={saveRole} disabled={!canUpdateRoles}>Save Role</button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
