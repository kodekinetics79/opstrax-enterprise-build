import { DataTable } from "@/components/ui";
import { useEffect, useMemo, useState } from "react";
import { useLocation, useSearchParams } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Check, ChevronDown, ChevronUp, ClipboardCheck, Download, KeyRound, LayoutDashboard, Plus, Search, ShieldCheck, Trash2, UserCog, Users, X } from "lucide-react";
import { useAuth } from "@/hooks/useAuth";
import { useHasPermission, PermissionDenied } from "@/hooks/usePermission";
import { useDialogFocus } from "@/hooks/useDialogFocus";
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
import { adminApi } from "@/services/adminApi";
import { customersApi } from "@/services/customersApi";
import { branchesApi } from "@/services/branchesApi";
import { PERMISSIONS } from "@/auth/rbacConfig";
import { EmptyState, ErrorState, KpiCard, LoadingState, PageHeader, PasswordInput, StatusBadge } from "@/components/ui";
import type { AnyRecord } from "@/types";
import {
  ActivationLinkPanel, Avatar, MfaBadge, TAB_OPTIONS, USER_COLUMNS, USER_PAGE_SIZE, USER_SORT_ACCESSORS,
  downloadCsv, extractApiError, permissionList, permissionsByGroup,
  type ActivationLink, type AdminTab, type RoleFormState, type UserFormState, type UserSortKey,
} from "./admin/AdminPagePrimitives";
import { AdminAccessReviewsPanel, AdminAuditPanel, AdminPermissionsPanel, AdminRolesPanel, AdminSettingsPanel } from "./admin/AdminTaskPanels";

export function AdminPage() {
  const location = useLocation();
  const [searchParams, setSearchParams] = useSearchParams();
  const { session } = useAuth();
  const hasPermission = useHasPermission();
  const canViewUsers = hasPermission(PERMISSIONS.USERS_VIEW);
  const canManageUsers = hasPermission(PERMISSIONS.USERS_MANAGE);
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

  const routeDefaultTab: AdminTab = location.pathname === "/user-management" ? "users" : "dashboard";
  const requestedTab = searchParams.get("tab") as AdminTab | null;
  const initialTab = TAB_OPTIONS.some((option) => option.key === requestedTab) ? requestedTab as AdminTab : routeDefaultTab;
  const [tab, setTab] = useState<AdminTab>(initialTab);
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
    customerId: "",
    branchId: "",
    status: "Active",
    password: "",
  });
  const [roleForm, setRoleForm] = useState<RoleFormState>({
    name: "",
    permissions: [],
  });
  const [modalError, setModalError] = useState<string | null>(null);
  const [inviteResult, setInviteResult] = useState<ActivationLink | null>(null);
  const [drawerLink, setDrawerLink] = useState<ActivationLink | null>(null);
  const [drawerAccessError, setDrawerAccessError] = useState<string | null>(null);
  const [drawerAccessNotice, setDrawerAccessNotice] = useState<string | null>(null);
  const [passwordResetTarget, setPasswordResetTarget] = useState<AnyRecord | null>(null);
  const [passwordResetForm, setPasswordResetForm] = useState({ password: "", confirm: "" });
  const [passwordResetError, setPasswordResetError] = useState<string | null>(null);
  const [copiedLinkKey, setCopiedLinkKey] = useState<"invite" | "drawer" | null>(null);
  const [userSort, setUserSort] = useState<{ key: UserSortKey; dir: "asc" | "desc" }>({ key: "fullName", dir: "asc" });
  const [userPage, setUserPage] = useState(1);
  const [selectedIds, setSelectedIds] = useState<number[]>([]);
  const [bulkNotice, setBulkNotice] = useState<{ tone: "success" | "error"; text: string } | null>(null);
  const [rolesView, setRolesView] = useState<"list" | "matrix">("list");

  useEffect(() => {
    const next = TAB_OPTIONS.some((option) => option.key === requestedTab) ? requestedTab as AdminTab : routeDefaultTab;
    setTab(next);
  }, [requestedTab, routeDefaultTab]);

  const selectTab = (nextTab: AdminTab) => {
    setTab(nextTab);
    const next = new URLSearchParams(searchParams);
    if (nextTab === routeDefaultTab) next.delete("tab");
    else next.set("tab", nextTab);
    setSearchParams(next, { replace: true });
  };

  const queryClient = useQueryClient();

  const overviewQ = useAdminOverview();
  const usersQ = useAdminUsers({ search, role: roleFilter, status: statusFilter });
  const rolesQ = useAdminRoles();
  const permissionsQ = useAdminPermissions();
  const accessReviewsQ = useAccessReviews(canViewAccessReviews);
  const accessReviewQ = useAccessReview(selectedReviewId);
  const branchesQ = useQuery({
    queryKey: ["branches"],
    queryFn: branchesApi.list,
    enabled: userModal !== null,
    staleTime: 60_000,
  });
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

  // Client-side sort + pagination (search/role/status filtering stays server-side).
  const sortedUsers = useMemo(() => {
    const accessor = USER_SORT_ACCESSORS[userSort.key];
    return [...users].sort((a, b) => {
      const va = accessor(a);
      const vb = accessor(b);
      const cmp = typeof va === "number" && typeof vb === "number"
        ? va - vb
        : String(va).localeCompare(String(vb), undefined, { numeric: true, sensitivity: "base" });
      return userSort.dir === "asc" ? cmp : -cmp;
    });
  }, [users, userSort]);
  const userPageCount = Math.max(1, Math.ceil(sortedUsers.length / USER_PAGE_SIZE));
  const safeUserPage = Math.min(userPage, userPageCount);
  const pagedUsers = useMemo(
    () => sortedUsers.slice((safeUserPage - 1) * USER_PAGE_SIZE, safeUserPage * USER_PAGE_SIZE),
    [sortedUsers, safeUserPage],
  );

  // DEF-027: the customer-scope select is only meaningful for portal roles, and the
  // customer book needs customers:view — load lazily and degrade to a plain id input
  // when the list is unavailable rather than silently hiding the binding control.
  //
  // "Is this a portal role?" is decided by the role's PERMISSIONS, never by its display
  // name. `/portal/i.test(roleName)` missed the two roles that most need binding —
  // `Customer` (shipments:view, customer_portal:view, alerts:view) and
  // `Customer Viewer` — because neither name contains "Portal", so an admin could
  // create those accounts but never bind them: the portal APIs then 403 for want of a
  // customer, and the UI offered no control to repair it. It also matched internal
  // roles whose names happen to contain "portal".
  //
  // The signature is: holds customer_portal:view, does NOT hold dashboard:view (an
  // internal supervisor previewing the portal must not be bindable — a binding turns
  // the principal into a customer principal and locks it out of every internal
  // endpoint), and is not a wildcard admin.
  const selectedRole = useMemo(
    () => (roleOptions as AnyRecord[]).find((option) => String(option.id) === String(userForm.roleId)),
    [roleOptions, userForm.roleId],
  );
  const selectedRolePermissions = useMemo(
    () => permissionList(selectedRole?.permissions ?? selectedRole?.permissionsJson ?? selectedRole?.permissions_json)
      .map((permission) => permission.trim().toLowerCase().replace(/\./g, ":").replace(/-/g, "_")),
    [selectedRole],
  );
  const isPortalRole = selectedRolePermissions.length > 0
    ? selectedRolePermissions.includes("customer_portal:view")
      && !selectedRolePermissions.includes("dashboard:view")
      && !selectedRolePermissions.includes("*")
    // The roles endpoint did not return permissions for this role, so the signature
    // cannot be evaluated. Fall back to the legacy name test rather than hiding the
    // control outright — an unbindable account is the worse failure.
    : /portal/i.test(userForm.roleName);
  const customerOptionsQ = useQuery({
    queryKey: ["admin-customer-options"],
    queryFn: () => customersApi.list(),
    enabled: userModal !== null && isPortalRole,
    staleTime: 60_000,
  });
  const customerOptions = useMemo(
    () =>
      ((customerOptionsQ.data ?? []) as AnyRecord[])
        .filter((c) => String(c.status ?? "Active").toLowerCase() !== "deleted")
        .map((c) => ({ id: Number(c.id), name: String(c.name ?? c.customerName ?? `Customer ${c.id}`) }))
        .filter((c) => Number.isFinite(c.id) && c.id > 0),
    [customerOptionsQ.data],
  );

  // Client-side shape check for the degrade-to-id-input path. "abc" / "0" / "-3" /
  // "2.5" all round-tripped to a server 400 with no on-screen explanation.
  const customerIdError = (() => {
    if (!isPortalRole) return null;
    const raw = userForm.customerId.trim();
    if (!raw) return null;
    return /^\d+$/.test(raw) && Number(raw) > 0
      ? null
      : "Enter a positive whole-number customer ID.";
  })();

  const selectedUserId = selectedUser ? Number(selectedUser.id) : null;
  const sessionsQ = useQuery({
    queryKey: ["admin-user-sessions", selectedUserId],
    queryFn: () => adminApi.userSessions(Number(selectedUserId)),
    enabled: selectedUserId != null && Number.isFinite(selectedUserId),
  });
  const generateLink = useMutation({ mutationFn: (id: number) => adminApi.activationLink(id) });
  const resetPassword = useMutation({
    mutationFn: ({ id, newPassword }: { id: number; newPassword: string }) => adminApi.resetUserPassword(id, newPassword),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["admin-user-sessions"] });
    },
  });
  const revokeSessions = useMutation({
    mutationFn: (id: number) => adminApi.revokeUserSessions(id),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["admin-user-sessions"] });
    },
  });

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
      customerId: "",
      branchId: "",
      status: "Active",
      password: "",
    });
    setModalError(null);
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
      customerId: user.customerId != null || user.customer_id != null ? String(user.customerId ?? user.customer_id) : "",
      branchId: user.branchId != null || user.branch_id != null ? String(user.branchId ?? user.branch_id) : "",
      status: String(user.status ?? "Active"),
      password: "",
    });
    setModalError(null);
    setUserModal("edit");
  };

  const saveUser = async () => {
    const body: Record<string, unknown> = {
      fullName: userForm.fullName,
      email: userForm.email,
      companyId: userForm.companyId,
      roleId: userForm.roleId ? Number(userForm.roleId) : undefined,
      roleName: userForm.roleName,
      // DEF-027: customer-scope binding. The key's presence is the API's intent
      // signal — null clears the binding, a number binds a portal user to a customer.
      customerId: userForm.customerId ? Number(userForm.customerId) : null,
      branchId: userForm.branchId ? Number(userForm.branchId) : null,
      status: userForm.status,
    };
    try {
      setModalError(null);
      if (userModal === "create") {
        // Password is optional: leaving it blank creates a Pending user and
        // returns a one-time activation link to hand to the new teammate.
        if (userForm.password.trim()) body.password = userForm.password;
        const result = await createUser.mutateAsync(body);
        if (result?.activationLink) {
          setInviteResult({ link: String(result.activationLink), expiresAt: result.activationExpiresAt });
        }
      } else if (userModal === "edit" && userForm.id) {
        await updateUser.mutateAsync({ id: Number(userForm.id), body });
      }
      setUserModal(null);
    } catch (err) {
      // Surface API validation failures (duplicate email, seat limit, weak
      // password) instead of leaving the modal open with no explanation.
      setModalError(extractApiError(err, "Could not save the user."));
    }
  };

  const saveRole = async () => {
    try {
      setModalError(null);
      if (roleModal?.id) {
        await updateRole.mutateAsync({ id: Number(roleModal.id), body: roleForm });
      } else {
        await createRole.mutateAsync(roleForm);
      }
      setRoleModal(null);
    } catch (err) {
      setModalError(extractApiError(err, "Could not save the role."));
    }
  };

  const exportUsers = async () => {
    downloadCsv("admin-users.csv", sortedUsers, ["fullName", "email", "companyName", "roleName", "status"]);
    setPermissionsExportNotice(`Exported ${sortedUsers.length} user rows to CSV.`);
    window.setTimeout(() => setPermissionsExportNotice(null), 3500);
  };

  const copyActivationLink = async (key: "invite" | "drawer", link: string) => {
    try {
      await navigator.clipboard.writeText(link);
    } catch {
      // Clipboard API can be unavailable (insecure context); the <code> block stays select-all.
    }
    setCopiedLinkKey(key);
    window.setTimeout(() => setCopiedLinkKey((current) => (current === key ? null : current)), 2000);
  };

  // Drawer open/close resets the per-user Access state so a previous user's
  // one-time link or error never bleeds into the next drawer.
  const openUserDrawer = (user: AnyRecord) => {
    setDrawerLink(null);
    setDrawerAccessError(null);
    setDrawerAccessNotice(null);
    setSelectedUser(user);
  };

  const closeUserDrawer = () => {
    setDrawerLink(null);
    setDrawerAccessError(null);
    setDrawerAccessNotice(null);
    setSelectedUser(null);
  };

  const generateActivationLink = async (id: number) => {
    try {
      setDrawerAccessError(null);
      const result = await generateLink.mutateAsync(id);
      setDrawerLink(result);
    } catch (err) {
      setDrawerAccessError(extractApiError(err, "Could not generate an activation link."));
    }
  };

  const signOutAllSessions = async (id: number, name: string) => {
    if (!window.confirm(`Sign out all active sessions for ${name}?`)) return;
    try {
      setDrawerAccessError(null);
      await revokeSessions.mutateAsync(id);
    } catch (err) {
      setDrawerAccessError(extractApiError(err, "Could not revoke this user's sessions."));
    }
  };

  const openPasswordReset = (user: AnyRecord) => {
    setPasswordResetForm({ password: "", confirm: "" });
    setPasswordResetError(null);
    setPasswordResetTarget(user);
  };

  const submitPasswordReset = async () => {
    if (!passwordResetTarget) return;
    if (passwordResetForm.password !== passwordResetForm.confirm) {
      setPasswordResetError("The two passwords do not match.");
      return;
    }
    try {
      setPasswordResetError(null);
      const result = await resetPassword.mutateAsync({
        id: Number(passwordResetTarget.id),
        newPassword: passwordResetForm.password,
      });
      const name = String(passwordResetTarget.fullName ?? passwordResetTarget.full_name ?? passwordResetTarget.email ?? "User");
      setPasswordResetTarget(null);
      setPasswordResetForm({ password: "", confirm: "" });
      setDrawerAccessNotice(`${name}'s password was reset and ${result.sessionsRevoked} active session${result.sessionsRevoked === 1 ? " was" : "s were"} revoked. No email was sent.`);
    } catch (err) {
      setPasswordResetError(extractApiError(err, "Could not reset this user's password."));
    }
  };

  const toggleUserSort = (key: UserSortKey) => {
    setUserSort((current) => (current.key === key ? { key, dir: current.dir === "asc" ? "desc" : "asc" } : { key, dir: "asc" }));
    setUserPage(1);
  };

  const toggleSelected = (id: number) => {
    setSelectedIds((ids) => (ids.includes(id) ? ids.filter((value) => value !== id) : [...ids, id]));
  };

  const bulkDeactivate = async () => {
    const ownUserId = Number(session?.user?.id);
    const targets = selectedIds.filter((id) => id !== ownUserId);
    const skippedSelf = targets.length !== selectedIds.length;
    if (targets.length === 0) {
      setBulkNotice({ tone: "error", text: "You cannot deactivate your own account." });
      return;
    }
    const confirmText = `Deactivate ${targets.length} selected user${targets.length === 1 ? "" : "s"}?${skippedSelf ? " Your own account will be skipped." : ""}`;
    if (!window.confirm(confirmText)) return;
    let failures = 0;
    for (const id of targets) {
      try {
        await deleteUser.mutateAsync(id);
      } catch {
        failures += 1;
      }
    }
    setSelectedIds([]);
    if (failures > 0) {
      setBulkNotice({ tone: "error", text: `Could not deactivate ${failures} of ${targets.length} selected user${targets.length === 1 ? "" : "s"}.` });
    } else {
      setBulkNotice({ tone: "success", text: `Deactivated ${targets.length} user${targets.length === 1 ? "" : "s"}.` });
      window.setTimeout(() => setBulkNotice(null), 3500);
    }
  };

  const exportRoles = async () => {
    downloadCsv("admin-roles.csv", roles, ["name", "scope", "userCount"]);
  };

  const openRoleEditor = (role: AnyRecord) => {
    setModalError(null);
    setRoleModal(role);
    setRoleForm({
      name: String(role.name ?? ""),
      permissions: permissionList(role.permissions ?? role.permissionsJson ?? role.permissions_json),
    });
  };

  const openCreateRole = () => {
    setModalError(null);
    setRoleModal({});
    setRoleForm({ name: "", permissions: [] });
  };

  const userDrawerRef = useDialogFocus<HTMLElement>(selectedUser != null, closeUserDrawer);
  const passwordResetDialogRef = useDialogFocus<HTMLDivElement>(passwordResetTarget != null, () => setPasswordResetTarget(null));
  const userDialogRef = useDialogFocus<HTMLDivElement>(userModal != null, () => setUserModal(null));
  const roleDialogRef = useDialogFocus<HTMLDivElement>(roleModal != null, () => setRoleModal(null));

  return (
    <div className="iam page-stack min-w-0">
      <PageHeader
        eyebrow="Governance"
        title="Users & Roles"
        description="Manage the people, roles, permissions and audit posture of this workspace."
        actions={
          <>
            <button className="btn-ghost" onClick={() => selectTab("audit")} disabled={!canViewAudit} title={!canViewAudit ? "You do not have permission to perform this action." : undefined}>
              <KeyRound className="h-4 w-4" />
              Audit Logs
            </button>
            <button className="btn-ghost" onClick={() => selectTab("settings")} disabled={!canViewSettings} title={!canViewSettings ? "You do not have permission to perform this action." : undefined}>
              <ShieldCheck className="h-4 w-4" />
              Settings
            </button>
            <button className="btn-primary" onClick={() => selectTab("users")} disabled={!canViewUsers} title={!canViewUsers ? "You do not have permission to perform this action." : undefined}>
              <LayoutDashboard className="h-4 w-4" />
              Open Users
            </button>
          </>
        }
      />

      {permissionsExportNotice && <div className="rounded-xl border border-emerald-400/30 bg-emerald-50 px-4 py-3 text-sm text-emerald-700">{permissionsExportNotice}</div>}

      {overviewQ.isLoading ? <LoadingState /> : overviewQ.isError ? <ErrorState message="Could not load admin overview." /> : (
        <div className="flex min-w-0 flex-wrap divide-x divide-slate-100 rounded-xl border border-slate-200 bg-white">
          {[
            { label: "Total Users", value: overviewQ.data?.totalUsers ?? 0, icon: <Users className="h-4 w-4" /> },
            { label: "Active Users", value: overviewQ.data?.activeUsers ?? 0, icon: <Users className="h-4 w-4" /> },
            { label: "Tenant Admins", value: overviewQ.data?.tenantAdmins ?? 0, icon: <UserCog className="h-4 w-4" /> },
            { label: "Roles", value: overviewQ.data?.roles ?? 0, icon: <KeyRound className="h-4 w-4" /> },
            { label: "Audit Events Today", value: overviewQ.data?.recentAuditEvents ?? 0, icon: <ShieldCheck className="h-4 w-4" /> },
            { label: "Permissions", value: overviewQ.data?.permissionCoverage ?? permissions.length, icon: <ShieldCheck className="h-4 w-4" /> },
          ].map((card) => (
            <KpiCard compact key={card.label} label={card.label} value={String(card.value)} />
          ))}
        </div>
      )}

      <div className="overflow-x-auto border-b border-slate-200 pb-px">
        <div className="flex min-w-max gap-1" role="group" aria-label="Administration sections">
          {TAB_OPTIONS.map((option) => (
            <button
            key={option.key}
            aria-pressed={tab === option.key}
            onClick={() => selectTab(option.key)}
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
            className={`rounded-t-lg px-3 py-2 text-sm font-semibold transition ${
              tab === option.key ? "border border-b-0 border-teal-300 bg-teal-50 text-teal-700" : "text-slate-500 hover:text-slate-700"
            } disabled:cursor-not-allowed disabled:opacity-40`}
          >
            {option.label}
            </button>
          ))}
        </div>
      </div>

      {tab === "dashboard" && (
        <div className="grid gap-4 xl:grid-cols-[1.2fr_0.8fr]">
            <div className="iam-card space-y-3 p-5">
              <div className="flex items-center justify-between gap-3">
                <h2 className="text-lg font-bold text-slate-900">Admin Activity</h2>
                <button className="btn-ghost h-9 px-3 shrink-0" onClick={() => selectTab("audit")} disabled={!canViewAudit}>Open audit trail</button>
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
                <button className="btn-primary" onClick={() => selectTab("users")} disabled={!canViewUsers}>Manage Users</button>
                <button className="btn-ghost" onClick={() => selectTab("roles")} disabled={!canViewRoles}>Review Roles</button>
                <button className="btn-ghost" onClick={() => selectTab("permissions")} disabled={!(canViewUsers || canViewRoles)}>View Permissions</button>
                <button className="btn-ghost" onClick={() => selectTab("settings")} disabled={!canViewSettings}>Open Settings</button>
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
              <input aria-label="Search users" className="field w-full pl-9" placeholder="Search users..." value={search} onChange={(e) => { setSearch(e.target.value); setUserPage(1); }} />
            </div>
            <select aria-label="Filter users by role" className="field w-full sm:w-44" value={roleFilter} onChange={(e) => { setRoleFilter(e.target.value); setUserPage(1); }}>
              <option value="">All roles</option>
              {roleOptions.map((role) => <option key={role.id} value={role.name}>{role.name}</option>)}
            </select>
            <select aria-label="Filter users by status" className="field w-full sm:w-36" value={statusFilter} onChange={(e) => { setStatusFilter(e.target.value); setUserPage(1); }}>
              <option value="">All statuses</option>
              {["Active", "Inactive", "Pending"].map((status) => <option key={status} value={status}>{status}</option>)}
            </select>
            <button className="btn-ghost" onClick={() => { setSearch(""); setRoleFilter(""); setStatusFilter(""); setUserPage(1); }}>
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

          {inviteResult && (
            <ActivationLinkPanel
              result={inviteResult}
              copied={copiedLinkKey === "invite"}
              onCopy={() => copyActivationLink("invite", inviteResult.link)}
              onDismiss={() => setInviteResult(null)}
            />
          )}

          {bulkNotice && (
            <div
              role={bulkNotice.tone === "error" ? "alert" : undefined}
              className={`rounded-xl border px-4 py-3 text-sm ${bulkNotice.tone === "error" ? "border-rose-400/30 bg-rose-50 text-rose-700" : "border-emerald-400/30 bg-emerald-50 text-emerald-700"}`}
            >
              {bulkNotice.text}
            </div>
          )}

          {selectedIds.length > 0 && (
            <div className="flex flex-wrap items-center justify-between gap-3 rounded-xl border border-slate-200 bg-white/80 px-4 py-2.5">
              <p className="min-w-0 truncate text-sm font-semibold text-slate-700">{selectedIds.length} selected</p>
              <div className="flex shrink-0 items-center gap-2">
                <button className="btn-ghost h-8 px-3" onClick={() => setSelectedIds([])}>Clear selection</button>
                <button
                  className="btn-ghost h-8 px-3 text-rose-600 hover:text-rose-700"
                  onClick={bulkDeactivate}
                  disabled={!canDeleteUsers || deleteUser.isPending}
                  title={!canDeleteUsers ? "You do not have permission to perform this action." : "Deactivate the selected users"}
                >
                  <Trash2 className="mr-1 h-3.5 w-3.5" />
                  Deactivate selected
                </button>
              </div>
            </div>
          )}

          {usersQ.isLoading ? <LoadingState /> : usersQ.isError ? <ErrorState message="Could not load users." /> : users.length === 0 ? (
            <EmptyState title="No users found" subtitle="Try another filter or add a new user for this tenant." />
          ) : (
            <div className="iam-card overflow-x-auto">
              <table className="w-full text-sm">
                <thead>
                  <tr className="border-b border-slate-200">
                    <th className="w-10 px-4 py-3">
                      <input
                        type="checkbox"
                        className="accent-teal-600"
                        aria-label="Select all users on this page"
                        checked={pagedUsers.length > 0 && pagedUsers.every((user) => selectedIds.includes(Number(user.id)))}
                        onChange={() => {
                          const pageIds = pagedUsers.map((user) => Number(user.id));
                          const allSelected = pageIds.length > 0 && pageIds.every((id) => selectedIds.includes(id));
                          setSelectedIds((ids) => (allSelected ? ids.filter((id) => !pageIds.includes(id)) : Array.from(new Set([...ids, ...pageIds]))));
                        }}
                      />
                    </th>
                    {USER_COLUMNS.map((column) => {
                      const isActive = column.sortKey != null && userSort.key === column.sortKey;
                      return (
                        <th
                          key={column.label}
                          aria-sort={column.sortKey ? (isActive ? (userSort.dir === "asc" ? "ascending" : "descending") : "none") : undefined}
                          className="px-4 py-3 text-left text-[10px] font-bold uppercase tracking-widest text-slate-500 whitespace-nowrap"
                        >
                          {column.sortKey ? (
                            <button
                              type="button"
                              className={`inline-flex items-center gap-1 uppercase tracking-widest transition ${isActive ? "text-teal-700" : "hover:text-slate-700"}`}
                              onClick={() => toggleUserSort(column.sortKey!)}
                            >
                              {column.label}
                              {isActive
                                ? (userSort.dir === "asc" ? <ChevronUp className="h-3 w-3 shrink-0" /> : <ChevronDown className="h-3 w-3 shrink-0" />)
                                : <ChevronUp className="h-3 w-3 shrink-0 opacity-25" />}
                            </button>
                          ) : column.label}
                        </th>
                      );
                    })}
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {pagedUsers.map((user: AnyRecord) => {
                    const name = String(user.fullName ?? user.full_name ?? "User");
                    return (
                    <tr key={String(user.id)} className="transition hover:bg-white/60">
                      <td className="w-10 px-4 py-3">
                        <input
                          type="checkbox"
                          className="accent-teal-600"
                          aria-label={`Select ${name}`}
                          checked={selectedIds.includes(Number(user.id))}
                          onChange={() => toggleSelected(Number(user.id))}
                        />
                      </td>
                      <td className="px-4 py-3 max-w-[260px]">
                        <button className="flex w-full min-w-0 items-center gap-3 text-left" onClick={() => openUserDrawer(user)}>
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
                          <button className="btn-ghost h-8 px-3" onClick={() => openUserDrawer(user)}>View</button>
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
              <div className="flex flex-wrap items-center justify-between gap-3 border-t border-slate-100 px-4 py-2.5">
                <p className="min-w-0 truncate text-xs text-slate-500">{sortedUsers.length} user{sortedUsers.length === 1 ? "" : "s"}</p>
                <div className="flex shrink-0 items-center gap-2">
                  <button className="btn-ghost h-8 px-3" onClick={() => setUserPage(safeUserPage - 1)} disabled={safeUserPage <= 1}>Prev</button>
                  <span className="text-xs text-slate-600 whitespace-nowrap">page {safeUserPage} of {userPageCount}</span>
                  <button className="btn-ghost h-8 px-3" onClick={() => setUserPage(safeUserPage + 1)} disabled={safeUserPage >= userPageCount}>Next</button>
                </div>
              </div>
            </div>
          )}
        </div>
      )}

      {tab === "roles" && (
        <AdminRolesPanel
          view={rolesView}
          setView={setRolesView}
          exportRoles={exportRoles}
          canExportReports={canExportReports}
          openCreateRole={openCreateRole}
          canCreateRoles={canCreateRoles}
          isLoading={rolesQ.isLoading}
          isError={rolesQ.isError}
          roles={roles}
          permissions={permissions}
          openRoleEditor={openRoleEditor}
          canUpdateRoles={canUpdateRoles}
        />
      )}

      {tab === "permissions" && (
        <AdminPermissionsPanel
          isError={permissionsQ.isError}
          isLoading={permissionsQ.isLoading}
          permissions={permissions}
        />
      )}

      {tab === "access" && (
        <AdminAccessReviewsPanel
          canManage={canManageAccessReviews}
          form={reviewForm}
          setForm={setReviewForm}
          creating={createAccessReview.isPending}
          onCreate={async () => {
            const result = await createAccessReview.mutateAsync({ ...reviewForm, reviewerUserId: Number(session?.user?.id) });
            setReviewForm({ title: "", description: "", dueDate: "" });
            setSelectedReviewId(Number(result.id));
          }}
          reviews={(accessReviewsQ.data ?? []) as AnyRecord[]}
          reviewsLoading={accessReviewsQ.isLoading}
          reviewsError={accessReviewsQ.isError}
          selectedId={selectedReviewId}
          setSelectedId={setSelectedReviewId}
          review={accessReviewQ.data as AnyRecord | undefined}
          reviewLoading={accessReviewQ.isLoading}
          reviewError={accessReviewQ.isError}
          completing={completeAccessReview.isPending}
          onComplete={async () => { await completeAccessReview.mutateAsync(selectedReviewId!); }}
          deciding={decideAccessReviewItem.isPending}
          onDecide={async (itemId, decision, notes) => { await decideAccessReviewItem.mutateAsync({ reviewId: selectedReviewId!, itemId, decision, notes }); }}
        />
      )}

      {tab === "settings" && (
        <AdminSettingsPanel
          localeData={localeQ.data}
          canViewSettings={canViewSettings}
          canUpdateSettings={canUpdateSettings}
        />
      )}

      {tab === "audit" && (
        <AdminAuditPanel
          auditLogs={(Array.isArray(auditLogsQ.data) ? auditLogsQ.data : []) as AnyRecord[]}
          auditExports={(Array.isArray(auditExportsQ.data) ? auditExportsQ.data : []) as AnyRecord[]}
          canExportReports={canExportReports}
          requestExport={() => createAuditExport.mutate({
            requestedByName: String(session?.user?.fullName ?? session?.user?.full_name ?? session?.user?.email ?? ""),
            exportFormat: "CSV",
          })}
        />
      )}

      {selectedUser && (
        <div className="fixed inset-0 z-50 flex justify-end bg-slate-900/30 backdrop-blur-sm" onClick={closeUserDrawer}>
          <aside ref={userDrawerRef} className="iam iam-drawer max-w-lg p-4 sm:p-5" onClick={(e) => e.stopPropagation()} role="dialog" aria-modal="true" aria-labelledby="admin-user-detail-title">
            <button className="float-right icon-btn" onClick={closeUserDrawer} aria-label="Close user detail"><X className="h-4 w-4" /></button>
            <p className="text-[11px] font-bold uppercase tracking-[0.2em] text-teal-700">User Detail</p>
            <div className="mt-4 flex items-center gap-3 min-w-0">
              <Avatar name={String(selectedUser.fullName ?? selectedUser.full_name ?? "User")} />
              <div className="min-w-0">
                <h2 id="admin-user-detail-title" className="text-xl font-bold text-slate-900 truncate">{String(selectedUser.fullName ?? selectedUser.full_name ?? "User")}</h2>
                <p className="text-xs text-slate-500 truncate">{String(selectedUser.email ?? "")}</p>
              </div>
            </div>
            <div className="mt-6 space-y-2">
              {([
                ["Email", selectedUser.email],
                ["Company", selectedUser.companyName ?? selectedUser.company_name],
                ["Branch scope", selectedUser.branchId ?? selectedUser.branch_id ?? "Tenant-wide"],
                ["Customer scope", selectedUser.customerId ?? selectedUser.customer_id ?? "Not assigned"],
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
            <div className="mt-6">
              <p className="text-[11px] font-bold uppercase tracking-[0.2em] text-teal-700">Access</p>
              <div className="mt-3 space-y-3">
                <div className="iam-kv">
                  <p className="text-[11px] uppercase tracking-[0.16em] text-slate-500 mt-0.5 shrink-0">Sessions</p>
                  <p className="text-right text-sm font-medium text-slate-800 min-w-0 truncate">
                    {sessionsQ.isLoading
                      ? "Loading…"
                      : sessionsQ.isError
                      ? "Unavailable"
                      : `${(sessionsQ.data ?? []).length} active session${(sessionsQ.data ?? []).length === 1 ? "" : "s"}`}
                  </p>
                </div>
                {canUpdateUsers && /^(active|pending)$/i.test(String(selectedUser.status ?? "")) && (
                  <button
                    className="btn-ghost w-full"
                    onClick={() => generateActivationLink(Number(selectedUser.id))}
                    disabled={generateLink.isPending}
                  >
                    <KeyRound className="h-4 w-4" />
                    {generateLink.isPending ? "Generating…" : "Generate activation link"}
                  </button>
                )}
                {drawerLink && (
                  <ActivationLinkPanel
                    result={drawerLink}
                    copied={copiedLinkKey === "drawer"}
                    onCopy={() => copyActivationLink("drawer", drawerLink.link)}
                    onDismiss={() => setDrawerLink(null)}
                  />
                )}
                {canManageUsers
                  && Number(selectedUser.id) !== Number(session?.user?.id)
                  && /^active$/i.test(String(selectedUser.status ?? "")) && (
                  <button
                    className="btn-ghost w-full"
                    onClick={() => openPasswordReset(selectedUser)}
                  >
                    <KeyRound className="h-4 w-4" />
                    Set new password
                  </button>
                )}
                {canUpdateUsers && (
                  <button
                    className="btn-ghost w-full text-rose-600 hover:text-rose-700"
                    onClick={() => signOutAllSessions(Number(selectedUser.id), String(selectedUser.fullName ?? selectedUser.full_name ?? selectedUser.email ?? "this user"))}
                    disabled={(sessionsQ.data ?? []).length === 0 || revokeSessions.isPending}
                    title={(sessionsQ.data ?? []).length === 0 ? "This user has no active sessions." : "Revoke every active session for this user"}
                  >
                    {revokeSessions.isPending ? "Signing out…" : "Sign out all sessions"}
                  </button>
                )}
                {drawerAccessError && (
                  <p role="alert" className="rounded-lg border border-rose-200 bg-rose-50 px-3 py-2 text-xs font-medium text-rose-700">{drawerAccessError}</p>
                )}
                {drawerAccessNotice && (
                  <p role="status" className="rounded-lg border border-emerald-200 bg-emerald-50 px-3 py-2 text-xs font-medium text-emerald-700">{drawerAccessNotice}</p>
                )}
                {canViewAudit && (
                  <button
                    className="btn-ghost w-full"
                    onClick={() => window.location.assign(`/audit-logs?actor=${encodeURIComponent(String(selectedUser.fullName ?? selectedUser.full_name ?? ""))}`)}
                    title="Open the audit trail filtered to this user's actions"
                  >
                    <ShieldCheck className="h-4 w-4" />
                    View audit trail
                  </button>
                )}
              </div>
            </div>
            {canUpdateUsers && (
              <button className="btn-primary mt-6 w-full" onClick={() => { openEditUser(selectedUser); closeUserDrawer(); }}>
                Edit User
              </button>
            )}
          </aside>
        </div>
      )}

      {passwordResetTarget && (
        <div className="fixed inset-0 z-[60] flex items-center justify-center bg-slate-900/40 p-2 backdrop-blur-sm sm:p-4">
          <div ref={passwordResetDialogRef} className="iam iam-card max-h-[calc(100dvh-1rem)] w-full max-w-md space-y-4 overflow-y-auto p-4 sm:p-5" role="dialog" aria-modal="true" aria-labelledby="admin-password-reset-title">
            <div className="flex items-center justify-between gap-3">
              <div>
                <h2 id="admin-password-reset-title" className="font-bold text-slate-900">Set new password</h2>
                <p className="mt-1 text-xs text-slate-500">
                  {String(passwordResetTarget.fullName ?? passwordResetTarget.full_name ?? passwordResetTarget.email)} · no email will be sent
                </p>
              </div>
              <button className="icon-btn" onClick={() => setPasswordResetTarget(null)} aria-label="Close"><X className="h-4 w-4" /></button>
            </div>
            <div>
              <label className="label">New password</label>
              <PasswordInput
                value={passwordResetForm.password}
                onChange={(event) => setPasswordResetForm((current) => ({ ...current, password: event.target.value }))}
                autoComplete="new-password"
                placeholder="Enter a policy-compliant password"
              />
            </div>
            <div>
              <label className="label">Confirm password</label>
              <PasswordInput
                value={passwordResetForm.confirm}
                onChange={(event) => setPasswordResetForm((current) => ({ ...current, confirm: event.target.value }))}
                autoComplete="new-password"
                placeholder="Repeat the new password"
              />
            </div>
            <p className="rounded-lg border border-amber-200 bg-amber-50 px-3 py-2 text-xs leading-5 text-amber-800">
              This replaces the current credential, clears the lockout, revokes every active session and invalidates outstanding reset links. Share the new password through a secure channel.
            </p>
            {passwordResetError && <p role="alert" className="rounded-lg border border-rose-200 bg-rose-50 px-3 py-2 text-xs font-medium text-rose-700">{passwordResetError}</p>}
            <div className="flex gap-2">
              <button type="button" className="btn-ghost flex-1" onClick={() => setPasswordResetTarget(null)}>Cancel</button>
              <button
                type="button"
                className="btn-primary flex-1"
                onClick={submitPasswordReset}
                disabled={resetPassword.isPending || !passwordResetForm.password || !passwordResetForm.confirm}
              >
                {resetPassword.isPending ? "Resetting…" : "Reset password"}
              </button>
            </div>
          </div>
        </div>
      )}

      {userModal && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/35 p-2 backdrop-blur-sm sm:p-4">
          <div ref={userDialogRef} className="iam iam-card max-h-[calc(100dvh-1rem)] w-full max-w-2xl space-y-4 overflow-y-auto p-4 sm:p-5" role="dialog" aria-modal="true" aria-labelledby="admin-user-editor-title">
            <div className="flex items-center justify-between">
              <h2 id="admin-user-editor-title" className="font-bold text-slate-900">{userModal === "create" ? "Add User" : "Edit User"}</h2>
              <button className="icon-btn" onClick={() => setUserModal(null)} aria-label="Close"><X className="h-4 w-4" /></button>
            </div>
              <div className="grid gap-3 md:grid-cols-2">
              <div><label className="label">Full Name</label><input className="field w-full" value={String(userForm.fullName ?? "")} onChange={(e) => setUserForm((f) => ({ ...f, fullName: e.target.value }))} /></div>
              <div><label className="label">Email</label><input className="field w-full" value={String(userForm.email ?? "")} onChange={(e) => setUserForm((f) => ({ ...f, email: e.target.value }))} /></div>
              <div>
                <label className="label">Role</label>
                <select className="field w-full" value={String(userForm.roleId ?? "")} onChange={(e) => {
                  const role = roleOptions.find((option) => String(option.id) === e.target.value);
                  const nextPermissions = permissionList(
                    (role as AnyRecord | undefined)?.permissions
                    ?? (role as AnyRecord | undefined)?.permissionsJson
                    ?? (role as AnyRecord | undefined)?.permissions_json,
                  ).map((permission) => permission.trim().toLowerCase().replace(/\./g, ":").replace(/-/g, "_"));
                  const nextIsPortal = nextPermissions.length > 0
                    ? nextPermissions.includes("customer_portal:view")
                      && !nextPermissions.includes("dashboard:view")
                      && !nextPermissions.includes("*")
                    : /portal/i.test(String(role?.name ?? ""));
                  // Switching to a non-portal role hides the Customer scope control but
                  // used to KEEP the value, which the form then submitted — the server
                  // rejected the binding and the admin saw a 400 about a field that was
                  // no longer on screen. Drop the binding with the control.
                  setUserForm((f) => ({
                    ...f,
                    roleId: e.target.value,
                    roleName: String(role?.name ?? ""),
                    customerId: nextIsPortal ? f.customerId : "",
                  }));
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
              <div className="md:col-span-2">
                <label className="label">Branch scope</label>
                <select className="field w-full" value={userForm.branchId} disabled={branchesQ.isLoading || branchesQ.isError} onChange={(e) => setUserForm((f) => ({ ...f, branchId: e.target.value }))}>
                  <option value="">Tenant-wide (all branches)</option>
                  {(branchesQ.data ?? []).filter((branch) => String(branch.status ?? "Active") === "Active").map((branch) => (
                    <option key={String(branch.id)} value={String(branch.id)}>{String(branch.branchCode)} — {String(branch.name)}</option>
                  ))}
                </select>
                {branchesQ.isError && <p role="alert" className="mt-1 text-xs text-rose-600">Branch scopes could not be loaded. Close this form and retry.</p>}
                <p className="mt-1 text-xs text-slate-500">Branch-bound accounts can access only operational records owned by that branch.</p>
              </div>
              {isPortalRole && (
                <div className="md:col-span-2">
                  <label className="label">Customer scope</label>
                  {customerOptionsQ.isError ? (
                    <>
                      <input
                        className="field w-full"
                        type="number"
                        min={1}
                        placeholder="Customer ID"
                        aria-invalid={customerIdError ? true : undefined}
                        value={userForm.customerId}
                        onChange={(e) => setUserForm((f) => ({ ...f, customerId: e.target.value }))}
                      />
                      <p role="alert" className="mt-1 text-xs text-amber-600">
                        Customer list could not be loaded — enter the customer ID directly.
                      </p>
                      {/* Without the list we cannot confirm the id EXISTS, but we can
                          reject a shape the server will certainly refuse, here instead
                          of after a round trip. */}
                      {customerIdError && <p role="alert" className="mt-1 text-xs text-red-600">{customerIdError}</p>}
                    </>
                  ) : customerOptionsQ.isLoading ? (
                    <select className="field w-full" value="" disabled>
                      <option value="">Loading customers…</option>
                    </select>
                  ) : customerOptions.length === 0 ? (
                    // An honest empty state: a tenant with no customers on file cannot
                    // bind anyone. A select whose only entry is "No customer binding"
                    // looked like a working control that simply did nothing.
                    <div className="rounded-xl border border-amber-200 bg-amber-50 p-3">
                      <p className="text-xs font-semibold text-amber-800">No customers on file</p>
                      <p className="mt-1 text-xs text-amber-700">
                        This organization has no customer records yet, so a portal user cannot be bound to
                        one. Create the customer under Customers first, then edit this user to bind it.
                      </p>
                    </div>
                  ) : (
                    <select
                      className="field w-full"
                      value={userForm.customerId}
                      onChange={(e) => setUserForm((f) => ({ ...f, customerId: e.target.value }))}
                    >
                      <option value="">No customer binding</option>
                      {customerOptions.map((c) => (
                        <option key={c.id} value={String(c.id)}>{c.name}</option>
                      ))}
                    </select>
                  )}
                  <p className="mt-1 text-xs text-slate-500">
                    Portal users see only the bound customer's shipments, invoices and proofs.
                    Without a binding, portal sign-in is denied — nothing renders as an empty page.
                  </p>
                </div>
              )}
              {userModal === "create" ? (
                <div>
                  <label className="label">Password (optional)</label>
                  <PasswordInput value={String(userForm.password ?? "")} onChange={(e) => setUserForm((f) => ({ ...f, password: e.target.value }))} placeholder="Set initial password" />
                  <p className="mt-1 text-xs text-slate-500">Leave blank to invite: you'll get a one-time activation link to share.</p>
                </div>
              ) : (
                <div className="rounded-lg border border-slate-200 bg-slate-50 p-3 text-xs text-slate-600">
                  Passwords are not changed from this form. Open User Detail and choose Set new password for direct recovery, or generate an activation link for the user to choose it.
                </div>
              )}
              <div>
                <label className="label">Company ID</label>
                <input className="field w-full" type="number" value={Number(userForm.companyId || 0)} onChange={(e) => setUserForm((f) => ({ ...f, companyId: Number(e.target.value) }))} disabled={!String(session?.role ?? "").match(/super/i)} />
              </div>
            </div>
            {modalError && <p role="alert" className="rounded-lg border border-rose-200 bg-rose-50 px-3 py-2 text-xs font-medium text-rose-700">{modalError}</p>}
            <div className="flex gap-2 pt-2">
              <button type="button" className="btn-ghost flex-1" onClick={() => setUserModal(null)}>Cancel</button>
              <button type="button" className="btn-primary flex-1" onClick={saveUser} disabled={!userForm.fullName.trim() || !userForm.email.trim() || !userForm.roleId || Boolean(customerIdError) || (userModal === "create" ? !canCreateUsers : !canUpdateUsers)}>
                Save User
              </button>
            </div>
          </div>
        </div>
      )}

      {roleModal && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/35 p-2 backdrop-blur-sm sm:p-4">
          <div ref={roleDialogRef} className="iam iam-card max-h-[calc(100dvh-1rem)] w-full max-w-3xl space-y-4 overflow-y-auto p-4 sm:p-5" role="dialog" aria-modal="true" aria-labelledby="admin-role-editor-title">
            <div className="flex items-center justify-between">
              <h2 id="admin-role-editor-title" className="font-bold text-slate-900">{roleModal.id ? "Edit Role" : "Create Role"}</h2>
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
            {modalError && <p role="alert" className="rounded-lg border border-rose-200 bg-rose-50 px-3 py-2 text-xs font-medium text-rose-700">{modalError}</p>}
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
