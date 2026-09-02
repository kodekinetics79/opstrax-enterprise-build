import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { AlertTriangle, CheckCircle2, ChevronDown, ChevronRight, CircleAlert, Play } from "lucide-react";
import type { AnyRecord } from "@/types";
import { platformApi, formatAmount, formatRate } from "@/services/platformApi";
import { PCard, PButton, PField, PSelect, PLoading } from "./ui";

// Billing readiness pre-flight.
//
// The point is not to report status — it is to make every finding fixable where
// it is found. A blocker means an invoice cannot be raised, or would be raised
// with the wrong tax; a warning means it will be raised but a controller should
// have seen something first. Each row carries the action that closes it.

const SEVERITY_STYLE: Record<string, { chip: string; dot: string; icon: typeof AlertTriangle }> = {
  blocker: { chip: "border-red-200 bg-red-50 text-red-700", dot: "bg-red-500", icon: CircleAlert },
  warning: { chip: "border-amber-200 bg-amber-50 text-amber-700", dot: "bg-amber-500", icon: AlertTriangle },
  action: { chip: "border-sky-200 bg-sky-50 text-sky-700", dot: "bg-sky-500", icon: Play },
  ok: { chip: "border-emerald-200 bg-emerald-50 text-emerald-700", dot: "bg-emerald-500", icon: CheckCircle2 },
};

export function BillingReadiness({ canManage, packages, onOpenTaxSetup, onNotice }: {
  canManage: boolean;
  packages: AnyRecord[];
  onOpenTaxSetup: () => void;
  onNotice: (msg: string) => void;
}) {
  const qc = useQueryClient();
  const { data, isLoading } = useQuery({ queryKey: ["platform", "billing-readiness"], queryFn: platformApi.billingReadiness });
  const [expanded, setExpanded] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [bulkPackage, setBulkPackage] = useState("");
  const [err, setErr] = useState<string | null>(null);

  const readiness = data as AnyRecord | undefined;
  const checks = (readiness?.checks ?? []) as AnyRecord[];
  const summary = (readiness?.summary ?? {}) as AnyRecord;

  const refreshAll = () => {
    qc.invalidateQueries({ queryKey: ["platform", "billing-readiness"] });
    qc.invalidateQueries({ queryKey: ["platform", "invoices"] });
    qc.invalidateQueries({ queryKey: ["platform", "revenue-summary"] });
  };

  const run = async (fn: () => Promise<unknown>, done: string) => {
    setBusy(true); setErr(null);
    try { await fn(); onNotice(done); refreshAll(); }
    catch (e) { setErr(e instanceof Error ? e.message : "Action failed"); }
    finally { setBusy(false); }
  };

  const assignPackageToAll = async (items: AnyRecord[]) => {
    if (!bulkPackage) return;
    const ids = items.map((i) => Number(i.id));
    await run(
      () => platformApi.bulkTenants({ ids, action: "assign-package", packageId: Number(bulkPackage) }),
      `Package assigned to ${ids.length} tenant${ids.length === 1 ? "" : "s"}`);
    setBulkPackage("");
  };

  const runBatch = async (issue: boolean) => {
    await run(async () => {
      const res = await platformApi.generateInvoiceBatch({ issue }) as AnyRecord;
      // The batch run reports money grouped by currency — a single total would be
      // meaningless across tenants billing in different currencies, and formatting one
      // sum as USD silently misstated every non-USD run.
      const byCurrency = Array.isArray(res.byCurrency) ? (res.byCurrency as AnyRecord[]) : [];
      const billed = byCurrency.length
        ? byCurrency.map((entry) => formatAmount(Number(entry.totalCents ?? 0), String(entry.currency ?? "USD"))).join(" + ")
        : "nothing";
      onNotice(`${res.generated} ${issue ? "issued" : "drafted"} · ${res.skipped} skipped${Number(res.failed ?? 0) > 0 ? ` · ${res.failed} failed` : ""} · ${billed} billed`);
    }, "Billing run complete");
  };

  const issueAllDrafts = async (items: AnyRecord[]) => {
    await run(async () => {
      for (const d of items) await platformApi.issueInvoice(Number(d.id));
    }, `${items.length} draft${items.length === 1 ? "" : "s"} issued`);
  };

  if (isLoading) return <PCard className="p-5"><PLoading /></PCard>;
  if (!readiness) return null;

  const blockers = Number(summary.blockers ?? 0);
  const warnings = Number(summary.warnings ?? 0);
  const actionable = checks.filter((c) => String(c.severity) !== "ok");
  const passed = checks.filter((c) => String(c.severity) === "ok");

  return (
    <PCard className="overflow-hidden">
      <div className="flex flex-wrap items-center justify-between gap-3 px-5 py-4">
        <div className="flex items-center gap-3">
          <span className={`inline-flex h-9 w-9 items-center justify-center rounded-full ${blockers > 0 ? "bg-red-50" : warnings > 0 ? "bg-amber-50" : "bg-emerald-50"}`}>
            {blockers > 0
              ? <CircleAlert className="h-5 w-5 text-red-600" />
              : warnings > 0
                ? <AlertTriangle className="h-5 w-5 text-amber-600" />
                : <CheckCircle2 className="h-5 w-5 text-emerald-600" />}
          </span>
          <div>
            <h2 className="text-sm font-bold text-slate-900">
              Billing readiness — {String(readiness.period)}
            </h2>
            <p className="text-xs text-slate-500">
              {blockers > 0
                ? `${blockers} blocker${blockers === 1 ? "" : "s"} would make an invoice wrong or impossible`
                : "No blockers — the period can be billed"}
              {warnings > 0 && ` · ${warnings} warning${warnings === 1 ? "" : "s"}`}
              {passed.length > 0 && ` · ${passed.length} check${passed.length === 1 ? "" : "s"} passing`}
            </p>
          </div>
        </div>
        {canManage && (
          <div className="flex gap-2">
            <PButton variant="ghost" disabled={busy} onClick={() => runBatch(false)}>Draft the period</PButton>
            <PButton disabled={busy} onClick={() => runBatch(true)}>
              <Play className="h-4 w-4" /> Run billing
            </PButton>
          </div>
        )}
      </div>

      {err && <p className="border-t border-slate-200 bg-red-50 px-5 py-2 text-xs font-medium text-red-700">{err}</p>}

      <div className="divide-y divide-slate-200 border-t border-slate-200">
        {actionable.map((check) => {
          const id = String(check.id);
          const severity = String(check.severity);
          const style = SEVERITY_STYLE[severity] ?? SEVERITY_STYLE.ok;
          const items = (check.items ?? []) as AnyRecord[];
          const open = expanded === id;
          return (
            <div key={id}>
              <button
                type="button"
                onClick={() => setExpanded(open ? null : id)}
                className="flex w-full items-center gap-3 px-5 py-3 text-left hover:bg-slate-50"
              >
                {open ? <ChevronDown className="h-4 w-4 shrink-0 text-slate-400" /> : <ChevronRight className="h-4 w-4 shrink-0 text-slate-400" />}
                <span className={`h-2 w-2 shrink-0 rounded-full ${style.dot}`} />
                <span className="flex-1 text-sm font-medium text-slate-800">{String(check.title)}</span>
                <span className={`rounded-full border px-2 py-0.5 text-[10px] font-bold uppercase ${style.chip}`}>
                  {Number(check.count)} · {severity}
                </span>
              </button>

              {open && (
                <div className="space-y-3 bg-slate-50/60 px-5 pb-4 pl-12">
                  <p className="text-xs leading-5 text-slate-500">{String(check.detail)}</p>

                  {/* Fix-in-place: each finding carries the action that closes it. */}
                  {canManage && String(check.action) === "assign_package" && (
                    <div className="flex flex-wrap items-end gap-2">
                      <div className="w-64">
                        <PField label="Assign package to all listed">
                          <PSelect value={bulkPackage} onChange={(e) => setBulkPackage(e.target.value)}>
                            <option value="">— Select package —</option>
                            {packages.map((p) => (
                              <option key={String(p.id)} value={String(p.id)}>
                                {String(p.name)} · {formatAmount(Number(p.basePriceCents ?? 0), String(p.currency ?? "USD"))}/mo
                              </option>
                            ))}
                          </PSelect>
                        </PField>
                      </div>
                      <PButton disabled={busy || !bulkPackage} onClick={() => assignPackageToAll(items)}>
                        Apply to {items.length}
                      </PButton>
                    </div>
                  )}

                  {canManage && String(check.action) === "register_tax" && (
                    <PButton disabled={busy} onClick={onOpenTaxSetup}>Record registrations</PButton>
                  )}

                  {canManage && String(check.action) === "generate_batch" && (
                    <div className="flex gap-2">
                      <PButton disabled={busy} onClick={() => runBatch(false)}>Draft {items.length}</PButton>
                      <PButton variant="ghost" disabled={busy} onClick={() => runBatch(true)}>Draft &amp; issue {items.length}</PButton>
                    </div>
                  )}

                  {canManage && String(check.action) === "issue_invoice" && (
                    <PButton disabled={busy} onClick={() => issueAllDrafts(items)}>Issue all {items.length}</PButton>
                  )}

                  <FindingList checkId={id} items={items} />
                </div>
              )}
            </div>
          );
        })}

        {passed.length > 0 && (
          <div className="flex flex-wrap gap-x-4 gap-y-1 bg-emerald-50/40 px-5 py-2.5 text-[11px] text-emerald-700">
            {passed.map((c) => (
              <span key={String(c.id)} className="inline-flex items-center gap-1">
                <CheckCircle2 className="h-3 w-3" /> {String(c.title)}
              </span>
            ))}
          </div>
        )}
      </div>
    </PCard>
  );
}

// Each check returns a different row shape; render the two or three columns that
// actually help an operator decide, rather than a generic key/value dump.
function FindingList({ checkId, items }: { checkId: string; items: AnyRecord[] }) {
  if (items.length === 0) return null;
  const shown = items.slice(0, 12);

  return (
    <div className="overflow-hidden rounded-lg border border-slate-200 bg-white">
      <table className="w-full text-left text-xs">
        <tbody className="divide-y divide-slate-100">
          {shown.map((i, idx) => (
            <tr key={idx}>
              <td className="px-3 py-1.5 font-medium text-slate-700">
                {String(i.name ?? i.tenant ?? i.countryName ?? i.invoiceNumber ?? "—")}
              </td>
              <td className="px-3 py-1.5 text-right text-slate-500">{describe(checkId, i)}</td>
            </tr>
          ))}
        </tbody>
      </table>
      {items.length > shown.length && (
        <p className="border-t border-slate-100 px-3 py-1.5 text-[11px] text-slate-400">
          and {items.length - shown.length} more
        </p>
      )}
    </div>
  );
}

function describe(checkId: string, i: AnyRecord): string {
  switch (checkId) {
    case "tenants_without_package":
      return `${String(i.status ?? "")}${i.country ? ` · ${String(i.country)}` : ""}`;
    case "countries_without_registration":
      return `${Number(i.tenantCount ?? 0)} tenant(s) · ${String(i.taxLabel ?? "Tax")} ${formatRate(Number(i.standardRate ?? 0))} uncharged`;
    case "tenants_without_tax_id":
      return `missing ${String(i.taxIdLabel ?? "tax ID")}`;
    case "consumption_terms_on_idle_meters":
      return `${String(i.featureKey)} → ${String(i.meterKey)} · no usage recorded`;
    case "uninvoiced_this_period":
      return formatAmount(Number(i.mrrCents ?? 0), String(i.billingCurrency ?? "USD")) + " MRR";
    case "drafts_pending_issue":
      return formatAmount(Number(i.totalCents ?? 0), String(i.currency ?? "USD"));
    case "overdue_invoices":
      return `${formatAmount(Number(i.totalCents ?? 0), String(i.currency ?? "USD"))} · ${Number(i.daysOverdue ?? 0)}d overdue`;
    default:
      return "";
  }
}
