import { useQuery } from "@tanstack/react-query";
import type { AnyRecord } from "@/types";
import { platformApi } from "@/services/platformApi";
import { PHeader, PCard, PLoading, PError, PEmpty } from "./ui";

export function PlatformAuditPage() {
  const { data, isLoading, error } = useQuery({ queryKey: ["platform", "audit"], queryFn: platformApi.audit });
  if (isLoading) return <PLoading />;
  if (error) return <PError message={(error as Error)?.message} />;

  const rows = (data ?? []) as AnyRecord[];

  return (
    <div className="space-y-7">
      <PHeader
        eyebrow="Security & Audit"
        title="Platform audit log"
        description="Every platform action — create, update, status, billing, entitlement and impersonation — is recorded here."
      />

      {rows.length === 0 ? (
        <PEmpty title="No audit entries yet" subtitle="Platform actions will appear here as they happen." />
      ) : (
        <PCard className="overflow-hidden">
          <div className="overflow-x-auto">
            <table className="w-full min-w-[860px] text-left text-sm">
              <thead className="border-b border-slate-800 bg-slate-900/80">
                <tr className="text-xs uppercase tracking-wider text-slate-500">
                  {["When", "Actor", "Role", "Action", "Entity", "Tenant", "IP"].map((h) => <th key={h} className="px-5 py-3 font-semibold">{h}</th>)}
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-800">
                {rows.map((r) => (
                  <tr key={String(r.id)} className="hover:bg-slate-800/40">
                    <td className="px-5 py-3 font-mono text-xs text-slate-500">{String(r.createdAt ?? "").slice(0, 19).replace("T", " ")}</td>
                    <td className="px-5 py-3 text-slate-200">{String(r.actorEmail ?? "—")}</td>
                    <td className="px-5 py-3 text-slate-400">{String(r.actorRole ?? "—")}</td>
                    <td className="px-5 py-3"><span className="rounded-md bg-slate-800 px-2 py-0.5 font-mono text-xs text-teal-300">{String(r.action)}</span></td>
                    <td className="px-5 py-3 text-slate-400">{String(r.entityType)}{r.entityId ? ` #${String(r.entityId)}` : ""}</td>
                    <td className="px-5 py-3 text-slate-400">{r.targetCompanyId ? `#${String(r.targetCompanyId)}` : "—"}</td>
                    <td className="px-5 py-3 font-mono text-xs text-slate-600">{String(r.ipAddress ?? "—")}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </PCard>
      )}
    </div>
  );
}
