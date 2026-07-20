import { useQuery } from "@tanstack/react-query";
import { ArrowRight, AlertTriangle, ReceiptText, ShieldCheck, Sparkles } from "lucide-react";
import type { AnyRecord } from "@/types";
import { platformApi, formatMoney } from "@/services/platformApi";
import { PHeader, PCard, PKpi, PBadge, PButton, PLoading, PError, PEmpty } from "./ui";
import { useNavigate } from "react-router-dom";

function asRows(value: unknown): AnyRecord[] {
  return Array.isArray(value) ? value as AnyRecord[] : [];
}

function shortDate(value: unknown) {
  const text = String(value ?? "");
  return text ? text.slice(0, 10) : "—";
}

export function PlatformCommercialOpsPage() {
  const navigate = useNavigate();
  const { data, isLoading, error } = useQuery({
    queryKey: ["platform", "commercial-ops"],
    queryFn: platformApi.commercialOps,
  });

  if (isLoading) return <PLoading />;
  if (error) return <PError message={(error as Error)?.message} />;
  if (!data) return <PEmpty title="No commercial data" subtitle="Commercial operations data appears once tenants, packages and invoices exist." />;

  const tenantLifecycle = (data.tenantLifecycle ?? {}) as AnyRecord;
  const billing = (data.billing ?? {}) as AnyRecord;
  const packages = (data.packages ?? {}) as AnyRecord;
  const health = (data.health ?? {}) as AnyRecord;
  const audit = (data.audit ?? {}) as AnyRecord;
  const roles = (data.roles ?? {}) as AnyRecord;
  const recommendations = asRows(data.recommendedActions);

  const tenantItems = asRows(health.items);
  const packageItems = asRows(packages.items);
  const auditItems = asRows(audit.recent);
  const roleItems = asRows(roles.items);

  return (
    <div className="space-y-7">
      <PHeader
        eyebrow="Platform Commercial Ops"
        title="SaaS business cockpit"
        description="A single operator-grade view across tenant lifecycle, billing exposure, package posture, health, audit and role coverage."
        actions={
          <PButton variant="ghost" onClick={() => navigate("/platform/tenants")}>
            Open tenant controls <ArrowRight className="h-4 w-4" />
          </PButton>
        }
      />

      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <PKpi label="MRR" value={formatMoney(Number(data.mrrCents), String(data.currency ?? "USD"))} tone="good" sub="Recurring monthly revenue" />
        <PKpi label="ARR" value={formatMoney(Number(data.arrCents), String(data.currency ?? "USD"))} sub="Annualized run-rate" />
        <PKpi label="Active Tenants" value={Number(tenantLifecycle.active ?? 0)} sub={`${Number(tenantLifecycle.total ?? 0)} total accounts`} />
        <PKpi label="Open Billing" value={formatMoney(Number(billing.outstandingRevenueCents ?? 0))} tone={Number(billing.outstandingRevenueCents ?? 0) > 0 ? "warn" : "default"} sub={`${Number(billing.openInvoiceCount ?? 0)} invoices`} />
      </div>

      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <PKpi label="Trials" value={Number(tenantLifecycle.trial ?? 0)} />
        <PKpi label="Past Due" value={Number(tenantLifecycle.pastDue ?? 0)} tone={Number(tenantLifecycle.pastDue ?? 0) > 0 ? "bad" : "default"} />
        <PKpi label="Suspended" value={Number(tenantLifecycle.suspended ?? 0)} tone={Number(tenantLifecycle.suspended ?? 0) > 0 ? "bad" : "default"} />
        <PKpi label="Renewals 30d" value={Number(tenantLifecycle.renewalsDue ?? 0)} tone={Number(tenantLifecycle.renewalsDue ?? 0) > 0 ? "warn" : "default"} />
      </div>

      <div className="grid gap-5 xl:grid-cols-2">
        <PCard className="p-5">
          <div className="mb-4 flex items-center gap-2">
            <Sparkles className="h-4 w-4 text-teal-300" />
            <h2 className="text-sm font-bold uppercase tracking-wider text-slate-300">Recommended actions</h2>
          </div>
          {recommendations.length === 0 ? (
            <PEmpty title="No urgent commercial actions" subtitle="The current book of business is stable enough for the operator to focus on growth." />
          ) : (
            <div className="space-y-2">
              {recommendations.map((action, index) => (
                <div key={index} className="flex items-center gap-3 rounded-xl border border-slate-800 bg-slate-900/80 px-4 py-3">
                  <span className={`h-2.5 w-2.5 rounded-full ${
                    String(action.priority) === "Critical" ? "bg-red-500" : String(action.priority) === "High" ? "bg-amber-400" : "bg-teal-400"
                  }`} />
                  <div className="min-w-0 flex-1">
                    <p className="truncate text-sm font-semibold text-slate-100">{String(action.title ?? action.action ?? "Action")}</p>
                    <p className="text-xs text-slate-500">{String(action.action ?? "")}</p>
                  </div>
                  <PBadge value={action.priority} />
                </div>
              ))}
            </div>
          )}
        </PCard>

        <PCard className="p-5">
          <div className="mb-4 flex items-center gap-2">
            <ShieldCheck className="h-4 w-4 text-emerald-300" />
            <h2 className="text-sm font-bold uppercase tracking-wider text-slate-300">Tenant lifecycle</h2>
          </div>
          {tenantItems.length === 0 ? (
            <PEmpty title="No tenant health rows" subtitle="Tenant lifecycle rows appear once subscriptions are present." />
          ) : (
            <div className="space-y-2">
              {tenantItems.map((row) => (
                <div key={String(row.id)} className="flex items-center justify-between rounded-xl border border-slate-800 bg-slate-900/80 px-4 py-3">
                  <div className="min-w-0">
                    <p className="truncate text-sm font-semibold text-slate-100">{String(row.tenant ?? "Tenant")}</p>
                    <p className="text-xs text-slate-500">
                      {String(row.userCount ?? 0)} users · {String(row.openInvoices ?? 0)} open invoices
                    </p>
                  </div>
                  <div className="flex items-center gap-3">
                    <span className="text-xs text-slate-400">{shortDate(row.contractEnd)}</span>
                    <PBadge value={row.status} />
                  </div>
                </div>
              ))}
            </div>
          )}
        </PCard>
      </div>

      <div className="grid gap-5 xl:grid-cols-2">
        <PCard className="p-5">
          <div className="mb-4 flex items-center gap-2">
            <ReceiptText className="h-4 w-4 text-amber-300" />
            <h2 className="text-sm font-bold uppercase tracking-wider text-slate-300">Billing exposure</h2>
          </div>
          <div className="grid gap-3 sm:grid-cols-3">
            <PKpi label="Outstanding" value={formatMoney(Number(billing.outstandingRevenueCents ?? 0))} tone={Number(billing.outstandingRevenueCents ?? 0) > 0 ? "warn" : "default"} />
            <PKpi label="Collected" value={formatMoney(Number(billing.collectedRevenueCents ?? 0))} tone="good" />
            <PKpi label="Open invoices" value={Number(billing.openInvoiceCount ?? 0)} />
          </div>
        </PCard>

        <PCard className="p-5">
          <div className="mb-4 flex items-center gap-2">
            <AlertTriangle className="h-4 w-4 text-red-300" />
            <h2 className="text-sm font-bold uppercase tracking-wider text-slate-300">Health risks</h2>
          </div>
          {tenantItems.length === 0 ? (
            <PEmpty title="No health data yet" subtitle="Health rows are derived from live subscription and invoice data." />
          ) : (
            <div className="space-y-2">
              {tenantItems.slice(0, 5).map((row) => (
                <div key={`risk-${String(row.id)}`} className="flex items-center justify-between rounded-xl border border-slate-800 bg-slate-900/80 px-4 py-3">
                  <div className="min-w-0">
                    <p className="truncate text-sm font-semibold text-slate-100">{String(row.tenant ?? "Tenant")}</p>
                    <p className="text-xs text-slate-500">{String(row.userCount ?? 0)} users · {String(row.openInvoices ?? 0)} open invoices</p>
                  </div>
                  <PBadge value={row.status} />
                </div>
              ))}
            </div>
          )}
        </PCard>
      </div>

      <div className="grid gap-5 xl:grid-cols-2">
        <PCard className="overflow-hidden p-5">
          <div className="mb-4 flex items-center gap-2">
            <h2 className="text-sm font-bold uppercase tracking-wider text-slate-300">Package posture</h2>
          </div>
          {packageItems.length === 0 ? (
            <PEmpty title="No packages configured" subtitle="Package posture appears once pricing packages are created." />
          ) : (
            <div className="space-y-2">
              {packageItems.map((p) => (
                <div key={String(p.packageCode ?? p.name)} className="flex items-center justify-between rounded-xl border border-slate-800 bg-slate-900/80 px-4 py-3">
                  <div>
                    <p className="text-sm font-semibold text-slate-100">{String(p.name ?? p.packageCode)}</p>
                    <p className="text-xs text-slate-500">{String(p.packageCode ?? "—")} · {Number(p.tenantCount ?? 0)} tenant(s)</p>
                  </div>
                  <div className="flex items-center gap-2">
                    <PBadge value={p.isCustom ? "manual_contract" : p.active ? "active" : "cancelled"} />
                    <span className="text-sm font-semibold text-emerald-300">{formatMoney(Number(p.mrrCents ?? 0))}</span>
                  </div>
                </div>
              ))}
            </div>
          )}
        </PCard>

        <PCard className="overflow-hidden p-5">
          <div className="mb-4 flex items-center gap-2">
            <h2 className="text-sm font-bold uppercase tracking-wider text-slate-300">Audit and roles</h2>
          </div>
          <div className="grid gap-4 sm:grid-cols-2">
            <div>
              <p className="mb-2 text-xs font-semibold uppercase tracking-wider text-slate-500">Recent audit</p>
              {auditItems.length === 0 ? (
                <PEmpty title="No audit events" subtitle="Platform actions will appear here after operator activity." />
              ) : (
                <div className="space-y-2">
                  {auditItems.map((row) => (
                    <div key={String(row.id)} className="rounded-xl border border-slate-800 bg-slate-900/80 px-4 py-3">
                      <p className="text-sm font-semibold text-slate-100">{String(row.action)}</p>
                      <p className="text-xs text-slate-500">{String(row.actorEmail ?? "—")} · {shortDate(row.createdAt)}</p>
                    </div>
                  ))}
                </div>
              )}
            </div>
            <div>
              <p className="mb-2 text-xs font-semibold uppercase tracking-wider text-slate-500">Role coverage</p>
              {roleItems.length === 0 ? (
                <PEmpty title="No role rows" subtitle="Role coverage appears once the platform schema is seeded." />
              ) : (
                <div className="space-y-2">
                  {roleItems.map((row) => (
                    <div key={String(row.roleKey)} className="flex items-center justify-between rounded-xl border border-slate-800 bg-slate-900/80 px-4 py-3">
                      <div>
                        <p className="text-sm font-semibold text-slate-100">{String(row.name)}</p>
                        <p className="text-xs text-slate-500">{String(row.roleKey)}</p>
                      </div>
                      <div className="text-right">
                        <p className="text-sm font-semibold text-slate-200">{Number(row.adminCount ?? 0)} admins</p>
                        <p className="text-xs text-slate-500">{Number(row.permissionCount ?? 0)} permissions</p>
                      </div>
                    </div>
                  ))}
                </div>
              )}
            </div>
          </div>
        </PCard>
      </div>
    </div>
  );
}
