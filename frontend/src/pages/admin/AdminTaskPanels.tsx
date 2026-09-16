import type { Dispatch, SetStateAction } from "react";
import { Check, ClipboardCheck, Plus, ShieldCheck } from "lucide-react";
import { DataTable, EmptyState, ErrorState, LoadingState, StatusBadge } from "@/components/ui";
import type { AnyRecord } from "@/types";
import { permissionList, permissionsByGroup } from "./AdminPagePrimitives";

type AccessReviewForm = { title: string; description: string; dueDate: string };

export function AdminAccessReviewsPanel({
  canManage,
  form,
  setForm,
  creating,
  onCreate,
  reviews,
  reviewsLoading,
  reviewsError,
  selectedId,
  setSelectedId,
  review,
  reviewLoading,
  reviewError,
  completing,
  onComplete,
  deciding,
  onDecide,
}: {
  canManage: boolean;
  form: AccessReviewForm;
  setForm: Dispatch<SetStateAction<AccessReviewForm>>;
  creating: boolean;
  onCreate: () => Promise<void>;
  reviews: AnyRecord[];
  reviewsLoading: boolean;
  reviewsError: boolean;
  selectedId: number | null;
  setSelectedId: Dispatch<SetStateAction<number | null>>;
  review: AnyRecord | undefined;
  reviewLoading: boolean;
  reviewError: boolean;
  completing: boolean;
  onComplete: () => Promise<void>;
  deciding: boolean;
  onDecide: (itemId: number, decision: "approve" | "revoke", notes?: string) => Promise<void>;
}) {
  return (
    <div className="grid gap-4 xl:grid-cols-[0.75fr_1.25fr]">
      <div className="space-y-4">
        {canManage && (
          <div className="iam-card space-y-3 p-5">
            <div><h2 className="font-bold text-slate-900">Start access certification</h2><p className="mt-1 text-xs text-slate-500">Snapshots every active user and their current role permissions for this tenant.</p></div>
            <input className="field w-full" value={form.title} onChange={(event) => setForm((current) => ({ ...current, title: event.target.value }))} placeholder="Quarterly privileged access review" />
            <textarea className="field min-h-20 w-full" value={form.description} onChange={(event) => setForm((current) => ({ ...current, description: event.target.value }))} placeholder="Purpose and reviewer guidance" />
            <div><label className="label">Due date</label><input className="field w-full" type="date" value={form.dueDate} onChange={(event) => setForm((current) => ({ ...current, dueDate: event.target.value }))} /></div>
            <button className="btn-primary w-full" disabled={!form.title.trim() || creating} onClick={() => void onCreate()}><Plus className="h-4 w-4" />{creating ? "Creating…" : "Create review"}</button>
          </div>
        )}
        <div className="iam-card overflow-hidden">
          <div className="border-b border-slate-200 px-5 py-4"><h2 className="font-bold text-slate-900">Review campaigns</h2></div>
          {reviewsLoading ? <LoadingState /> : reviewsError ? <ErrorState message="Could not load access reviews." /> : reviews.length === 0 ? (
            <div className="p-5"><EmptyState title="No access reviews" subtitle="Create the first tenant access certification campaign." /></div>
          ) : reviews.map((item) => (
            <button key={String(item.id)} className={`w-full border-b border-slate-100 px-5 py-4 text-left transition hover:bg-white/60 ${selectedId === Number(item.id) ? "bg-teal-50/70" : ""}`} onClick={() => setSelectedId(Number(item.id))}>
              <div className="flex items-start justify-between gap-3"><p className="min-w-0 truncate font-semibold text-slate-900">{String(item.title)}</p><div className="shrink-0"><StatusBadge status={String(item.status)} /></div></div>
              <p className="mt-2 text-xs text-slate-500">{Number(item.itemsPending ?? item.items_pending ?? 0)} pending · {Number(item.itemsApproved ?? item.items_approved ?? 0)} approved · {Number(item.itemsRevoked ?? item.items_revoked ?? 0)} revoked</p>
            </button>
          ))}
        </div>
      </div>
      <div className="iam-card min-h-80 overflow-hidden">
        {!selectedId ? (
          <div className="flex min-h-80 flex-col items-center justify-center p-8 text-center"><ClipboardCheck className="h-10 w-10 text-teal-500" /><h2 className="mt-3 font-bold text-slate-900">Select a review</h2><p className="mt-1 text-sm text-slate-500">Inspect each user’s snapshotted role and make an explicit retain or revoke decision.</p></div>
        ) : reviewLoading ? <LoadingState /> : reviewError ? <ErrorState message="Could not load this access review." /> : (
          <div>
            <div className="flex flex-wrap items-start justify-between gap-3 border-b border-slate-200 p-5">
              <div className="min-w-0"><h2 className="truncate text-lg font-bold text-slate-900">{String(review?.title ?? "Access review")}</h2><p className="mt-1 text-sm text-slate-500">{String(review?.description ?? "Tenant access certification")}</p></div>
              <button className="btn-primary shrink-0" disabled={!canManage || Number(review?.itemsPending ?? review?.items_pending ?? 0) > 0 || String(review?.status) === "completed" || completing} onClick={() => void onComplete()}><Check className="h-4 w-4" />Complete review</button>
            </div>
            <div className="overflow-x-auto">
              <table className="w-full text-sm"><thead><tr className="border-b border-slate-200">{["User", "Role", "Permissions", "Decision"].map((heading) => <th key={heading} className="px-4 py-3 text-left text-[10px] font-bold uppercase tracking-widest text-slate-500">{heading}</th>)}</tr></thead>
                <tbody className="divide-y divide-slate-100">{((review?.items as AnyRecord[] | undefined) ?? []).map((item) => {
                  const pending = String(item.status) === "pending";
                  return <tr key={String(item.id)}><td className="max-w-[220px] px-4 py-3"><p className="truncate font-semibold text-slate-900">{String(item.targetUserName ?? item.target_user_name ?? "User")}</p><p className="truncate text-xs text-slate-500">{String(item.targetUserEmail ?? item.target_user_email ?? "")}</p></td><td className="max-w-[140px] px-4 py-3 text-slate-700"><span className="block truncate">{String(item.roleName ?? item.role_name ?? "—")}</span></td><td className="px-4 py-3 text-xs text-slate-500 whitespace-nowrap">{permissionList(item.permissionsSnapshot ?? item.permissions_snapshot).length} granted</td><td className="px-4 py-3">{pending ? <div className="flex gap-2"><button className="btn-ghost h-8 px-3" disabled={!canManage || deciding} onClick={() => void onDecide(Number(item.id), "approve")}>Retain</button><button className="btn-ghost h-8 px-3 text-rose-600" disabled={!canManage || deciding} onClick={() => void onDecide(Number(item.id), "revoke", "Access removal required by reviewer")}>Revoke</button></div> : <StatusBadge status={String(item.status)} />}</td></tr>;
                })}</tbody></table>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}

export function AdminRolesPanel({
  view,
  setView,
  exportRoles,
  canExportReports,
  openCreateRole,
  canCreateRoles,
  isLoading,
  isError,
  roles,
  permissions,
  openRoleEditor,
  canUpdateRoles,
}: {
  view: "list" | "matrix";
  setView: Dispatch<SetStateAction<"list" | "matrix">>;
  exportRoles: () => Promise<void>;
  canExportReports: boolean;
  openCreateRole: () => void;
  canCreateRoles: boolean;
  isLoading: boolean;
  isError: boolean;
  roles: AnyRecord[];
  permissions: string[];
  openRoleEditor: (role: AnyRecord) => void;
  canUpdateRoles: boolean;
}) {
  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <p className="min-w-0 truncate text-sm text-slate-500">Roles list and permission bundles.</p>
        <div className="flex shrink-0 items-center gap-2">
          <div className="flex rounded-xl border border-slate-200 bg-white p-1" role="group" aria-label="Roles view">
            {([['list', 'List'], ['matrix', 'Matrix']] as Array<["list" | "matrix", string]>).map(([key, label]) => (
              <button key={key} type="button" aria-pressed={view === key} className={`rounded-lg px-3 py-1 text-xs font-semibold transition ${view === key ? "bg-teal-50 text-teal-700" : "text-slate-500 hover:text-slate-700"}`} onClick={() => setView(key)}>
                {label}
              </button>
            ))}
          </div>
          <button className="btn-ghost" onClick={() => void exportRoles()} disabled={!canExportReports} title={!canExportReports ? "You do not have permission to perform this action." : undefined}>Export</button>
          <button className="btn-primary" onClick={openCreateRole} disabled={!canCreateRoles}><Plus className="h-4 w-4" /> Create role</button>
        </div>
      </div>
      {isLoading ? <LoadingState /> : isError ? <ErrorState message="Could not load roles." /> : view === "matrix" ? (
        <div className="iam-card overflow-x-auto">
          <table className="w-full text-sm">
            <thead><tr className="border-b border-slate-200">
              <th className="px-4 py-3 text-left text-[10px] font-bold uppercase tracking-widest text-slate-500 whitespace-nowrap">Permission group</th>
              {roles.map((role) => <th key={String(role.id)} className="max-w-[110px] px-4 py-3 text-left text-[10px] font-bold uppercase tracking-widest text-slate-500"><span className="block truncate" title={String(role.name)}>{String(role.name)}</span></th>)}
            </tr></thead>
            <tbody className="divide-y divide-slate-100">
              {permissionsByGroup(permissions).map((group) => (
                <tr key={group.title}>
                  <td className="px-4 py-3 font-semibold text-slate-900 whitespace-nowrap">{group.title}</td>
                  {roles.map((role) => {
                    const grantedList = permissionList(role.permissions ?? role.permissionsJson ?? role.permissions_json);
                    const granted = group.permissions.filter((permission) => grantedList.includes(permission)).length;
                    const total = group.permissions.length;
                    const tone = granted === total ? "bg-teal-500/10 font-semibold text-teal-700" : granted === 0 ? "text-slate-300" : "text-slate-600";
                    return <td key={String(role.id)} className={`px-4 py-3 whitespace-nowrap tabular-nums ${tone}`} title={`${granted} of ${total} ${group.title} permissions granted`}>{granted}/{total}</td>;
                  })}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : (
        <DataTable
          rows={roles}
          columns={["name", "userCount", "permissions"]}
          columnLabels={{ userCount: "Users", permissions: "Permissions" }}
          cellRenderers={{ permissions: (role) => `${permissionList(role.permissions ?? role.permissionsJson ?? role.permissions_json).length} permissions` }}
          actions={(role) => (
            <button type="button" className="btn-ghost btn-compact" onClick={() => openRoleEditor(role)} disabled={!canUpdateRoles || Boolean(role.isSystem ?? role.is_system)} title={Boolean(role.isSystem ?? role.is_system) ? "Built-in templates are immutable; create a tenant role to customize access." : undefined}>
              {Boolean(role.isSystem ?? role.is_system) ? "Protected" : "Edit"}
            </button>
          )}
        />
      )}
    </div>
  );
}

export function AdminPermissionsPanel({
  isError,
  isLoading,
  permissions,
}: {
  isError: boolean;
  isLoading: boolean;
  permissions: string[];
}) {
  if (isError) {
    return <ErrorState message="The live permissions endpoint failed, so the admin view is not showing a seed-backed replacement." />;
  }
  if (isLoading && permissions.length === 0) {
    return (
      <div className="space-y-3">
        <p className="text-sm text-slate-500">Fetching the live RBAC catalog.</p>
        <LoadingState />
      </div>
    );
  }
  return (
    <div className="space-y-4">
      <p className="text-sm text-slate-500">Canonical permission catalog used by the RBAC layer.</p>
      <div className="grid gap-4 xl:grid-cols-2">
        {permissionsByGroup(permissions).map((group) => (
          <div key={group.title} className="iam-card min-w-0 p-5">
            <div className="flex items-center justify-between gap-3">
              <h3 className="truncate font-bold text-slate-900">{group.title}</h3>
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
    </div>
  );
}

export function AdminSettingsPanel({
  localeData,
  canViewSettings,
  canUpdateSettings,
}: {
  localeData: unknown;
  canViewSettings: boolean;
  canUpdateSettings: boolean;
}) {
  const settings = ((Array.isArray(localeData) ? (localeData as AnyRecord[])[0] : localeData) as AnyRecord | undefined) ?? {};
  const openSettings = () => window.location.assign("/settings");
  return (
    <div className="iam-card space-y-4 p-5">
      <div className="flex items-center justify-between gap-3">
        <div className="min-w-0">
          <h2 className="text-lg font-bold text-slate-900">Tenant Settings</h2>
          <p className="mt-1 text-sm text-slate-500">Locale and operational preferences for this tenant.</p>
        </div>
        <button className="btn-ghost shrink-0" onClick={openSettings}>Open Settings Page</button>
      </div>
      <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
        {Object.entries(settings)
          .filter(([key]) => !["id", "tenant_id", "company_id", "created_at", "updated_at"].includes(key))
          .slice(0, 6)
          .map(([key, value]) => (
            <div key={key} className="iam-kv min-w-0 flex-col !items-start gap-1">
              <p className="w-full truncate text-[10px] font-bold uppercase tracking-widest text-slate-500">{key.replace(/_/g, " ")}</p>
              <p className="w-full break-words text-sm text-slate-700">{String(value ?? "—")}</p>
            </div>
          ))}
      </div>
      <button className="btn-primary w-fit" onClick={openSettings} disabled={!canViewSettings || !canUpdateSettings}>
        <ShieldCheck className="h-4 w-4" />
        Update settings
      </button>
    </div>
  );
}

export function AdminAuditPanel({
  auditLogs,
  auditExports,
  requestExport,
  canExportReports,
}: {
  auditLogs: AnyRecord[];
  auditExports: AnyRecord[];
  requestExport: () => void;
  canExportReports: boolean;
}) {
  return (
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
              {auditLogs.slice(0, 8).map((log) => (
                <tr key={String(log.id)}>
                  <td className="max-w-[220px] px-4 py-3 text-slate-900"><span className="block truncate" title={String(log.actionName ?? log.action_name ?? "Action")}>{String(log.actionName ?? log.action_name ?? "Action")}</span></td>
                  <td className="max-w-[160px] px-4 py-3 text-slate-600"><span className="block truncate">{String(log.actorName ?? log.actor_name ?? "system")}</span></td>
                  <td className="max-w-[160px] px-4 py-3 text-slate-600"><span className="block truncate">{String(log.entityName ?? log.entity_name ?? "—")}</span></td>
                  <td className="px-4 py-3"><StatusBadge status={String(log.severity ?? "Info")} /></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        <div className="iam-card space-y-3 p-5">
          <h3 className="font-bold text-slate-900">Export Requests</h3>
          {auditExports.slice(0, 4).map((entry) => (
            <div key={String(entry.id)} className="iam-kv">
              <p className="min-w-0 truncate text-sm font-semibold text-slate-900">{String(entry.requestedByName ?? entry.requested_by_name ?? "—")}</p>
              <p className="shrink-0 text-xs text-slate-500">{String(entry.status ?? "Pending")}</p>
            </div>
          ))}
          <button className="btn-ghost w-full" onClick={requestExport} disabled={!canExportReports} title={!canExportReports ? "You do not have permission to perform this action." : undefined}>
            Request audit export
          </button>
        </div>
      </div>
    </div>
  );
}
