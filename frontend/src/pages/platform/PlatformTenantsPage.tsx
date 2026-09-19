import { useMemo, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { Plus, Building2, CheckCircle2, CircleAlert, Search } from "lucide-react";
import type { AnyRecord } from "@/types";
import { platformApi, formatMoney } from "@/services/platformApi";
import { usePlatformAuth } from "@/hooks/usePlatformAuth";
import { CreateTenantDrawer, GATED_MODULES, TenantDetailDrawer } from "./tenant-management/TenantManagementDrawers";
import {
  PHeader, PCard, PBadge, PButton, PSelect, PLoading, PError, PEmpty, PConfirm,
  PCheckbox, PBulkBar, useRowSelection,
} from "./ui";

// The modules a Platform Admin can turn on/off per tenant. `key` must match the
// module_key the API gate uses (Program.cs ModuleKeyForPath) — the blurb spells out
// exactly which API surface each toggle governs, so the control is self-explanatory.
export function PlatformTenantsPage() {
  const qc = useQueryClient();
  const { can } = usePlatformAuth();
  const canManage = can("platform:tenants:manage");
  const canOffboard = can("platform:tenants:offboard");
  const canEntitlements = can("platform:entitlements:manage");

  const { data: tenants, isLoading, error } = useQuery({ queryKey: ["platform", "tenants"], queryFn: platformApi.tenants });
  const { data: packages } = useQuery({ queryKey: ["platform", "packages"], queryFn: platformApi.packages });

  const [createOpen, setCreateOpen] = useState(false);
  const [selectedId, setSelectedId] = useState<number | null>(null);
  const [search, setSearch] = useState("");
  const [statusFilter, setStatusFilter] = useState("");

  const refresh = () => {
    qc.invalidateQueries({ queryKey: ["platform", "tenants"] });
    qc.invalidateQueries({ queryKey: ["platform", "command-center"] });
    if (selectedId) qc.invalidateQueries({ queryKey: ["platform", "tenant", selectedId] });
  };

  const all = (tenants ?? []) as AnyRecord[];
  const rows = useMemo(() => {
    const q = search.trim().toLowerCase();
    return all.filter((t) => {
      if (statusFilter && String(t.status ?? "") !== statusFilter) return false;
      if (!q) return true;
      return [t.name, t.companyCode, t.packageName, t.industry]
        .some((v) => String(v ?? "").toLowerCase().includes(q));
    });
  }, [all, search, statusFilter]);

  const pkgs = (packages ?? []) as AnyRecord[];
  const sel = useRowSelection(rows.map((t) => Number(t.id)));
  const [bulkBusy, setBulkBusy] = useState(false);
  const [bulkConfirm, setBulkConfirm] = useState<"suspend" | "cancel" | "delete" | null>(null);
  const [bulkNotice, setBulkNotice] = useState<string | null>(null);
  const [bulkPkg, setBulkPkg] = useState("");

  // R (bulk read/export): dump the selected tenants — the live, company-scoped rows
  // already rendered in the table — to a CSV the operator can open in a spreadsheet.
  // Pure client-side over data the /api/platform/tenants read returned; no new fetch.
  const exportSelected = () => {
    const chosen = new Set(sel.selectedIds.map(String));
    const picked = rows.filter((t) => chosen.has(String(t.id)));
    if (picked.length === 0) return;
    const cols: [string, string][] = [
      ["id", "id"], ["name", "name"], ["companyCode", "code"], ["status", "status"],
      ["packageName", "package"], ["seatLimit", "seat_limit"], ["mrrCents", "mrr_cents"],
      ["userCount", "users"], ["country", "country"], ["currency", "currency"],
      ["industry", "industry"], ["primaryContactEmail", "primary_contact_email"],
      ["billingEmail", "billing_email"], ["createdAt", "created_at"],
    ];
    const esc = (v: unknown) => {
      const s = v == null ? "" : String(v);
      return /[",\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s;
    };
    const csv = [
      cols.map(([, h]) => h).join(","),
      ...picked.map((t) => cols.map(([k]) => esc(t[k])).join(",")),
    ].join("\n");
    const url = URL.createObjectURL(new Blob([csv], { type: "text/csv;charset=utf-8" }));
    const a = document.createElement("a");
    a.href = url;
    a.download = `tenants-export-${new Date().toISOString().slice(0, 10)}.csv`;
    a.click();
    URL.revokeObjectURL(url);
    setBulkNotice(`Exported ${picked.length} tenant${picked.length === 1 ? "" : "s"} to CSV`);
  };

  const runBulk = async (action: string, extra?: AnyRecord) => {
    setBulkBusy(true); setBulkNotice(null);
    try {
      const res = await platformApi.bulkTenants({ ids: sel.selectedIds.map(Number), action, ...extra }) as AnyRecord;
      const failed = Number(res.failed ?? 0);
      setBulkNotice(
        `${action.replace(/-/g, " ")}: ${res.succeeded}/${res.requested} applied${failed > 0 ? ` · ${failed} failed` : ""}`,
      );
      sel.clear();
      refresh();
    } catch (e) {
      setBulkNotice(e instanceof Error ? e.message : "Bulk action failed");
    } finally {
      setBulkBusy(false); setBulkConfirm(null);
    }
  };

  if (isLoading) return <PLoading />;
  if (error) return <PError message={(error as Error)?.message} />;

  return (
    <div className="space-y-7">
      <PHeader
        eyebrow="Tenant Management"
        title="Tenants"
        description="Provision accounts, assign packages, control subscription status and feature entitlements."
        actions={canManage ? <PButton onClick={() => setCreateOpen(true)}><Plus className="h-4 w-4" /> New Tenant</PButton> : undefined}
      />

      <div className="flex flex-wrap items-center gap-3">
        <div className="relative min-w-64 flex-1 max-w-md">
          <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
          <input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Search by name, code, package…"
            className="w-full rounded-[14px] border border-slate-300 bg-white py-2.5 pl-9 pr-3 text-sm text-slate-900 placeholder:text-slate-400 outline-none shadow-sm focus:border-teal-400 focus:ring-2 focus:ring-teal-400/15"
          />
        </div>
        <select
          value={statusFilter}
          onChange={(e) => setStatusFilter(e.target.value)}
          aria-label="Filter by subscription status"
          className="rounded-[14px] border border-slate-300 bg-white px-3 py-2.5 text-sm text-slate-900 outline-none shadow-sm focus:border-teal-400"
        >
          <option value="">All statuses</option>
          {["trial", "active", "past_due", "suspended", "cancelled", "manual_contract"].map((s) => (
            <option key={s} value={s}>{s.replace(/_/g, " ")}</option>
          ))}
        </select>
      </div>

      {bulkNotice && (
        <div className="rounded-xl border border-teal-500/30 bg-teal-500/5 px-4 py-2.5 text-sm text-teal-700">{bulkNotice}</div>
      )}

      {rows.length === 0 ? (
        all.length === 0
          ? <PEmpty title="No tenants yet" subtitle="Create your first tenant to start provisioning the SaaS business." />
          : <PEmpty title="No tenants match" subtitle="Adjust the search or status filter." />
      ) : (
        <PCard className="overflow-hidden">
          <div className="overflow-x-auto">
            <table className="w-full min-w-[860px] text-left text-sm">
              <thead className="border-b border-slate-800 bg-slate-900/80">
                <tr className="text-xs uppercase tracking-wider text-slate-500">
                  {canManage && (
                    <th className="w-10 px-5 py-3">
                      <PCheckbox
                        checked={sel.allVisibleSelected}
                        indeterminate={sel.someVisibleSelected}
                        onToggle={() => sel.toggleAllVisible()}
                        ariaLabel="Select all tenants"
                      />
                    </th>
                  )}
                  {["Tenant", "Status", "Package", "Seats", "MRR", "Users", "Created"].map((h) => (
                    <th key={h} className="px-5 py-3 font-semibold">{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-800">
                {rows.map((t) => (
                  <tr
                    key={String(t.id)}
                    onClick={() => setSelectedId(Number(t.id))}
                    className={`cursor-pointer transition hover:bg-slate-800/40 ${sel.isSelected(t.id) ? "bg-teal-500/5" : ""}`}
                  >
                    {canManage && (
                      <td className="px-5 py-3.5" onClick={(e) => e.stopPropagation()}>
                        <PCheckbox
                          checked={sel.isSelected(t.id)}
                          onToggle={(shift) => sel.toggle(t.id, shift)}
                          ariaLabel={`Select ${String(t.name)}`}
                        />
                      </td>
                    )}
                    <td className="px-5 py-3.5">
                      <div className="flex items-center gap-3">
                        <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-slate-800 text-slate-400">
                          <Building2 className="h-4 w-4" />
                        </div>
                        <div>
                          <p className="font-semibold text-slate-100">{String(t.name)}</p>
                          <p className="text-xs text-slate-500">{String(t.companyCode ?? "—")}</p>
                        </div>
                      </div>
                    </td>
                    <td className="px-5 py-3.5"><PBadge value={t.status ?? "—"} /></td>
                    <td className="px-5 py-3.5 text-slate-300">{String(t.packageName ?? "—")}</td>
                    <td className="px-5 py-3.5 text-slate-300">{String(t.seatLimit ?? "—")}</td>
                    <td className="px-5 py-3.5 font-semibold text-emerald-400">{formatMoney(Number(t.mrrCents))}</td>
                    <td className="px-5 py-3.5 text-slate-300">{String(t.userCount ?? 0)}</td>
                    <td className="px-5 py-3.5 font-mono text-xs text-slate-500">{String(t.createdAt ?? "").slice(0, 10)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </PCard>
      )}

      {canManage && (
        <PBulkBar count={sel.count} onClear={sel.clear}>
          <PButton variant="ghost" disabled={bulkBusy} onClick={exportSelected}>Export CSV</PButton>
          <div className="flex items-center gap-1.5">
            <PSelect value={bulkPkg} onChange={(e) => setBulkPkg(e.target.value)} aria-label="Assign package to selected tenants">
              <option value="">Assign package…</option>
              {pkgs.map((p) => <option key={String(p.id)} value={String(p.id)}>{String(p.name)}</option>)}
            </PSelect>
            <PButton
              variant="ghost"
              disabled={bulkBusy || !bulkPkg}
              onClick={() => runBulk("assign-package", { packageId: Number(bulkPkg) }).then(() => setBulkPkg(""))}
            >
              Assign
            </PButton>
          </div>
          <PButton variant="ghost" disabled={bulkBusy} onClick={() => runBulk("activate")}>Activate</PButton>
          <PButton variant="ghost" disabled={bulkBusy} onClick={() => runBulk("extend-trial", { days: 14 })}>Extend trial +14d</PButton>
          <PButton variant="ghost" disabled={bulkBusy} onClick={() => runBulk("revoke-sessions")}>Revoke sessions</PButton>
          <PButton variant="ghost" disabled={bulkBusy} onClick={() => setBulkConfirm("suspend")}>Suspend</PButton>
          <PButton variant="danger" disabled={bulkBusy} onClick={() => setBulkConfirm("cancel")}>Cancel</PButton>
          {canOffboard && (
            <PButton variant="danger" disabled={bulkBusy} onClick={() => setBulkConfirm("delete")}>Delete</PButton>
          )}
        </PBulkBar>
      )}

      <PConfirm
        open={bulkConfirm === "suspend"}
        title={`Suspend ${sel.count} tenant${sel.count === 1 ? "" : "s"}?`}
        body={<>All users of the selected tenants will be locked out immediately and every active session revoked. You can reactivate at any time.</>}
        confirmLabel="Suspend tenants"
        busy={bulkBusy}
        onConfirm={() => runBulk("suspend")}
        onClose={() => setBulkConfirm(null)}
      />
      <PConfirm
        open={bulkConfirm === "cancel"}
        title={`Cancel ${sel.count} tenant subscription${sel.count === 1 ? "" : "s"}?`}
        body={<>Cancelling locks out all users and revokes every session for the selected tenants. Tenant data is retained. Reactivation requires platform action.</>}
        confirmLabel="Cancel subscriptions"
        busy={bulkBusy}
        onConfirm={() => runBulk("cancel")}
        onClose={() => setBulkConfirm(null)}
      />
      <PConfirm
        open={bulkConfirm === "delete"}
        title={`Permanently delete ${sel.count} tenant${sel.count === 1 ? "" : "s"}?`}
        body={<>This purges ALL data for the selected tenants — every job, vehicle, driver, user and record — and cannot be undone. Type <strong>DELETE</strong> to confirm.</>}
        confirmLabel={`Delete ${sel.count} tenant${sel.count === 1 ? "" : "s"}`}
        confirmText="DELETE"
        busy={bulkBusy}
        onConfirm={() => runBulk("delete", { confirm: "DELETE" })}
        onClose={() => setBulkConfirm(null)}
      />

      {createOpen && (
        <CreateTenantDrawer
          packages={(packages ?? []) as AnyRecord[]}
          onClose={() => setCreateOpen(false)}
          onCreated={() => { setCreateOpen(false); refresh(); }}
        />
      )}

      {selectedId !== null && (
        <TenantDetailDrawer
          id={selectedId}
          packages={(packages ?? []) as AnyRecord[]}
          canManage={canManage}
          canOffboard={canOffboard}
          canEntitlements={canEntitlements}
          gatedModules={GATED_MODULES}
          onClose={() => setSelectedId(null)}
          onChanged={refresh}
        />
      )}
    </div>
  );
}
