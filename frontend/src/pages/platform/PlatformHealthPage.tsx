import { useQuery } from "@tanstack/react-query";
import type { AnyRecord } from "@/types";
import { platformApi } from "@/services/platformApi";
import { PHeader, PCard, PKpi, PBadge, PLoading, PError, PEmpty } from "./ui";

const ACTION_LABELS: Record<string, string> = {
  payment_follow_up: "Payment follow-up",
  schedule_training: "Schedule training",
  trial_conversion: "Trial conversion",
  upsell: "Upsell opportunity",
  renewal_follow_up: "Renewal follow-up",
};

export function PlatformHealthPage() {
  const { data, isLoading, error } = useQuery({ queryKey: ["platform", "health"], queryFn: platformApi.health });
  if (isLoading) return <PLoading />;
  if (error) return <PError message={(error as Error)?.message} />;

  const rows = (data ?? []) as AnyRecord[];
  const count = (h: string) => rows.filter((r) => String(r.health) === h).length;

  return (
    <div className="space-y-7">
      <PHeader
        eyebrow="Customer Success"
        title="Tenant health"
        description="Health scores derived from subscription status, active users, payment standing and renewal proximity."
      />

      <div className="grid gap-4 sm:grid-cols-3">
        <PKpi label="Healthy" value={count("green")} tone="good" />
        <PKpi label="At Risk" value={count("yellow")} tone="warn" />
        <PKpi label="Critical" value={count("red")} tone="bad" />
      </div>

      {rows.length === 0 ? (
        <PEmpty title="No tenants to score" subtitle="Health scores appear once tenants are provisioned." />
      ) : (
        <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
          {rows.map((r) => {
            const actions = (r.recommendedActions ?? []) as string[];
            const ring = String(r.health) === "green" ? "text-emerald-400" : String(r.health) === "yellow" ? "text-amber-400" : "text-red-400";
            return (
              <PCard key={String(r.id)} className="p-5">
                <div className="flex items-start justify-between">
                  <div>
                    <h3 className="font-bold text-white">{String(r.tenant)}</h3>
                    <PBadge value={r.status} />
                  </div>
                  <div className="text-right">
                    <p className={`text-3xl font-bold ${ring}`}>{String(r.healthScore)}</p>
                    <p className="text-[10px] uppercase tracking-wider text-slate-500">health</p>
                  </div>
                </div>
                <div className="mt-3 flex gap-4 text-xs text-slate-500">
                  <span>{String(r.userCount)} users</span>
                  <span>{String(r.openInvoices)} open invoices</span>
                </div>
                {actions.length > 0 && (
                  <div className="mt-4 flex flex-wrap gap-1.5">
                    {actions.map((a) => (
                      <span key={a} className="rounded-full border border-slate-700 bg-slate-800/60 px-2 py-0.5 text-[10px] text-slate-300">
                        {ACTION_LABELS[a] ?? a}
                      </span>
                    ))}
                  </div>
                )}
              </PCard>
            );
          })}
        </div>
      )}
    </div>
  );
}
