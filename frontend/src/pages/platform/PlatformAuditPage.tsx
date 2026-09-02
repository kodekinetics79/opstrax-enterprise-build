import { Fragment, useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Download, Search, X } from "lucide-react";
import type { AnyRecord } from "@/types";
import { platformApi } from "@/services/platformApi";
import { PHeader, PCard, PButton, PField, PInput, PSelect, PLoading, PError, PEmpty } from "./ui";

// Security & Audit — the investigation surface.
//
// The question this page exists to answer is "every privileged action taken
// against tenant X between two dates, by whom, from where" — so it filters on
// the server, pages by keyset, and exports exactly the filtered set. An
// un-exportable log reads to an auditor as an un-reviewed log.

type Filters = {
  actor: string;
  action: string;
  entityType: string;
  companyId: string;
  from: string;
  to: string;
};

const EMPTY: Filters = { actor: "", action: "", entityType: "", companyId: "", from: "", to: "" };

// Actions that change who can reach a tenant's data. Highlighted because these
// are what an investigator is almost always looking for.
const PRIVILEGED = /impersonat|password_reset|user\.updated|user\.created|deleted|offboard|entitlement|role|invite/i;

export function PlatformAuditPage() {
  const [filters, setFilters] = useState<Filters>(EMPTY);
  const [applied, setApplied] = useState<Filters>(EMPTY);
  const [cursors, setCursors] = useState<string[]>([]);
  const [expanded, setExpanded] = useState<number | null>(null);
  const [exporting, setExporting] = useState(false);

  const { data: tenants } = useQuery({ queryKey: ["platform", "tenants"], queryFn: platformApi.tenants });

  const params = useMemo(() => {
    const p: AnyRecord = {};
    for (const [k, v] of Object.entries(applied)) if (v) p[k] = v;
    if (cursors.length > 0) p.cursor = cursors[cursors.length - 1];
    return p;
  }, [applied, cursors]);

  const { data, isLoading, error, isFetching } = useQuery({
    queryKey: ["platform", "audit", params],
    queryFn: () => platformApi.audit(params),
  });

  const rows = ((data as AnyRecord)?.rows ?? []) as AnyRecord[];
  const knownActions = ((data as AnyRecord)?.actions ?? []) as string[];
  const nextCursor = (data as AnyRecord)?.nextCursor as string | undefined;
  const hasFilters = Object.values(applied).some(Boolean);

  const apply = () => { setApplied(filters); setCursors([]); };
  const clear = () => { setFilters(EMPTY); setApplied(EMPTY); setCursors([]); };

  const exportCsv = async () => {
    setExporting(true);
    try {
      const blob = await platformApi.auditExportCsv(params);
      const url = URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = url;
      a.download = `opstrax-platform-audit-${new Date().toISOString().slice(0, 10)}.csv`;
      a.click();
      URL.revokeObjectURL(url);
    } finally { setExporting(false); }
  };

  if (isLoading) return <PLoading />;
  if (error) return <PError message={(error as Error)?.message} />;

  return (
    <div className="space-y-7">
      <PHeader
        eyebrow="Security & Audit"
        title="Platform audit log"
        description="Every privileged platform action — tenant lifecycle, entitlements, billing, user identity changes and support access — with the operator, their role, the tenant and the source address."
        actions={
          <PButton variant="ghost" disabled={exporting} onClick={exportCsv}>
            <Download className="h-4 w-4" /> {exporting ? "Exporting…" : "Export CSV"}
          </PButton>
        }
      />

      <PCard className="p-4">
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-6">
          <PField label="Operator">
            <PInput value={filters.actor} placeholder="email contains…"
                    onChange={(e) => setFilters({ ...filters, actor: e.target.value })} />
          </PField>
          <PField label="Action">
            <PSelect value={filters.action} onChange={(e) => setFilters({ ...filters, action: e.target.value })}>
              <option value="">All actions</option>
              {knownActions.map((a) => <option key={a} value={a}>{a}</option>)}
            </PSelect>
          </PField>
          <PField label="Tenant">
            <PSelect value={filters.companyId} onChange={(e) => setFilters({ ...filters, companyId: e.target.value })}>
              <option value="">All tenants</option>
              {((tenants ?? []) as AnyRecord[]).map((t) => (
                <option key={String(t.id)} value={String(t.id)}>{String(t.name)}</option>
              ))}
            </PSelect>
          </PField>
          <PField label="Entity">
            <PInput value={filters.entityType} placeholder="Tenant, Invoice, User…"
                    onChange={(e) => setFilters({ ...filters, entityType: e.target.value })} />
          </PField>
          <PField label="From">
            <PInput type="date" value={filters.from} onChange={(e) => setFilters({ ...filters, from: e.target.value })} />
          </PField>
          <PField label="To">
            <PInput type="date" value={filters.to} onChange={(e) => setFilters({ ...filters, to: e.target.value })} />
          </PField>
        </div>
        <div className="mt-3 flex items-center gap-2">
          <PButton onClick={apply}><Search className="h-4 w-4" /> Search</PButton>
          {hasFilters && (
            <PButton variant="ghost" onClick={clear}><X className="h-4 w-4" /> Clear</PButton>
          )}
          <span className="ml-auto text-xs text-slate-500">
            {isFetching ? "Loading…" : `${rows.length} entr${rows.length === 1 ? "y" : "ies"}${nextCursor ? " · more available" : ""}`}
          </span>
        </div>
      </PCard>

      {rows.length === 0 ? (
        <PEmpty
          title={hasFilters ? "No entries match those filters" : "No audit entries yet"}
          subtitle={hasFilters ? "Widen the date range or clear a filter." : "Platform actions will appear here as they happen."}
        />
      ) : (
        <PCard className="overflow-hidden">
          <div className="overflow-x-auto">
            <table className="w-full min-w-[900px] text-left text-sm">
              <thead className="border-b border-slate-200 bg-slate-50">
                <tr className="text-xs uppercase tracking-wider text-slate-500">
                  {["When", "Operator", "Role", "Action", "Entity", "Tenant", "IP"].map((h) => (
                    <th key={h} className="px-5 py-3 font-semibold">{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-200">
                {rows.map((r) => {
                  const id = Number(r.id);
                  const action = String(r.action);
                  const privileged = PRIVILEGED.test(action);
                  return (
                    <Fragment key={id}>
                      <tr
                        onClick={() => setExpanded(expanded === id ? null : id)}
                        className="cursor-pointer hover:bg-slate-50"
                      >
                        <td className="px-5 py-3 font-mono text-xs text-slate-500">
                          {String(r.createdAt ?? "").slice(0, 19).replace("T", " ")}
                        </td>
                        <td className="px-5 py-3 text-slate-700">{String(r.actorEmail ?? "—")}</td>
                        <td className="px-5 py-3 text-slate-500">{String(r.actorRole ?? "—")}</td>
                        <td className="px-5 py-3">
                          <span className={`rounded-md px-2 py-0.5 font-mono text-xs ${
                            privileged ? "bg-amber-50 text-amber-800" : "bg-slate-100 text-slate-600"}`}>
                            {action}
                          </span>
                        </td>
                        <td className="px-5 py-3 text-slate-500">
                          {String(r.entityType)}{r.entityId ? ` #${String(r.entityId)}` : ""}
                        </td>
                        <td className="px-5 py-3 text-slate-600">
                          {r.tenantName ? String(r.tenantName) : r.targetCompanyId ? `#${String(r.targetCompanyId)}` : "—"}
                        </td>
                        <td className="px-5 py-3 font-mono text-xs text-slate-400">{String(r.ipAddress ?? "—")}</td>
                      </tr>
                      {expanded === id && r.detailsJson != null && (
                        <tr className="bg-slate-50">
                          <td colSpan={7} className="px-5 py-3">
                            <pre className="overflow-x-auto rounded-lg border border-slate-200 bg-white p-3 text-[11px] leading-5 text-slate-600">
{JSON.stringify(r.detailsJson, null, 2)}
                            </pre>
                          </td>
                        </tr>
                      )}
                    </Fragment>
                  );
                })}
              </tbody>
            </table>
          </div>

          {(nextCursor || cursors.length > 0) && (
            <div className="flex items-center justify-between border-t border-slate-200 px-5 py-3">
              <PButton
                variant="ghost"
                disabled={cursors.length === 0}
                onClick={() => setCursors((c) => c.slice(0, -1))}
              >
                Newer
              </PButton>
              <span className="text-xs text-slate-400">Page {cursors.length + 1}</span>
              <PButton
                variant="ghost"
                disabled={!nextCursor}
                onClick={() => nextCursor && setCursors((c) => [...c, nextCursor])}
              >
                Older
              </PButton>
            </div>
          )}
        </PCard>
      )}
    </div>
  );
}
