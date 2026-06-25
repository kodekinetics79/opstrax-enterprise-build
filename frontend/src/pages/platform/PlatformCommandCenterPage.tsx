import { useQuery } from "@tanstack/react-query";
import { AlertTriangle, TrendingUp } from "lucide-react";
import type { AnyRecord } from "@/types";
import { platformApi, formatMoney } from "@/services/platformApi";
import { PHeader, PCard, PKpi, PBadge, PLoading, PError } from "./ui";

export function PlatformCommandCenterPage() {
  const { data, isLoading, error } = useQuery({
    queryKey: ["platform", "command-center"],
    queryFn: platformApi.commandCenter,
  });

  if (isLoading) return <PLoading />;
  if (error || !data) return <PError message={(error as Error)?.message} />;

  const tenants = (data.tenants ?? {}) as AnyRecord;
  const risks = (data.topRisks ?? []) as AnyRecord[];
  const actions = (data.recommendedActions ?? []) as AnyRecord[];

  return (
    <div className="space-y-7">
      <PHeader
        eyebrow="Platform Command Center"
        title="Business at a glance"
        description="Revenue, tenants, risk and recommended actions across the entire SaaS business."
      />

      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <PKpi label="MRR" value={formatMoney(Number(data.mrrCents), String(data.currency ?? "USD"))} tone="good" sub="Recurring monthly revenue" />
        <PKpi label="ARR" value={formatMoney(Number(data.arrCents), String(data.currency ?? "USD"))} sub="Annualised run-rate" />
        <PKpi label="Active Tenants" value={Number(tenants.active ?? 0)} sub={`${Number(tenants.total ?? 0)} total accounts`} />
        <PKpi
          label="Past-Due Revenue"
          value={formatMoney(Number(data.pastDueRevenueCents))}
          tone={Number(data.pastDueRevenueCents) > 0 ? "bad" : "default"}
          sub="Open + overdue invoices"
        />
      </div>

      <div className="grid gap-4 sm:grid-cols-3 lg:grid-cols-6">
        <PKpi label="Trials" value={Number(tenants.trial ?? 0)} tone="default" />
        <PKpi label="Past Due" value={Number(tenants.pastDue ?? 0)} tone={Number(tenants.pastDue ?? 0) > 0 ? "warn" : "default"} />
        <PKpi label="Suspended" value={Number(tenants.suspended ?? 0)} tone={Number(tenants.suspended ?? 0) > 0 ? "bad" : "default"} />
        <PKpi label="Cancelled" value={Number(tenants.cancelled ?? 0)} />
        <PKpi label="Trials Ending ≤7d" value={Number(data.trialEndingSoon ?? 0)} tone={Number(data.trialEndingSoon ?? 0) > 0 ? "warn" : "default"} />
        <PKpi label="Renewals ≤30d" value={Number(data.renewalsDue ?? 0)} tone={Number(data.renewalsDue ?? 0) > 0 ? "warn" : "default"} />
      </div>

      <div className="grid gap-5 lg:grid-cols-2">
        {/* Top risks */}
        <PCard className="p-5">
          <div className="mb-4 flex items-center gap-2">
            <AlertTriangle className="h-4 w-4 text-amber-400" />
            <h2 className="text-sm font-bold uppercase tracking-wider text-slate-300">Top Risks</h2>
          </div>
          {risks.length === 0 ? (
            <p className="py-6 text-center text-sm text-slate-500">No active risks. Healthy book of business.</p>
          ) : (
            <div className="space-y-2">
              {risks.map((r, i) => (
                <div key={i} className="flex items-center justify-between rounded-xl border border-slate-800 bg-slate-900/80 px-4 py-3">
                  <div className="min-w-0">
                    <p className="truncate text-sm font-semibold text-slate-100">{String(r.tenant)}</p>
                    <p className="text-xs text-slate-500">{formatMoney(Number(r.mrrCents))} MRR</p>
                  </div>
                  <PBadge value={r.status} />
                </div>
              ))}
            </div>
          )}
        </PCard>

        {/* Recommended actions */}
        <PCard className="p-5">
          <div className="mb-4 flex items-center gap-2">
            <TrendingUp className="h-4 w-4 text-teal-400" />
            <h2 className="text-sm font-bold uppercase tracking-wider text-slate-300">Recommended Actions</h2>
          </div>
          {actions.length === 0 ? (
            <p className="py-6 text-center text-sm text-slate-500">Nothing requires attention right now.</p>
          ) : (
            <div className="space-y-2">
              {actions.map((a, i) => (
                <div key={i} className="flex items-center gap-3 rounded-xl border border-slate-800 bg-slate-900/80 px-4 py-3">
                  <span className={`h-2 w-2 shrink-0 rounded-full ${
                    String(a.priority) === "Critical" ? "bg-red-500" : String(a.priority) === "High" ? "bg-amber-400" : "bg-teal-400"
                  }`} />
                  <div className="min-w-0 flex-1">
                    <p className="truncate text-sm font-semibold text-slate-100">{String(a.title)}</p>
                    <p className="text-xs text-slate-500">{String(a.action)}</p>
                  </div>
                  <PBadge value={a.priority} />
                </div>
              ))}
            </div>
          )}
        </PCard>
      </div>
    </div>
  );
}
