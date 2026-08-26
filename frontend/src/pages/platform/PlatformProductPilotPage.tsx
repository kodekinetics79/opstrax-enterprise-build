import { useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ExternalLink, ShieldCheck } from "lucide-react";
import type { AnyRecord } from "@/types";
import { platformApi } from "@/services/platformApi";
import { apiErrorMessage } from "@/utils/apiErrorMessage";
import { PButton, PCard, PError, PField, PHeader, PInput, PLoading } from "./ui";

const TENANT_CODE = "CERT-LARGE-20260825";

export function PlatformProductPilotPage() {
  const queryClient = useQueryClient();
  const [confirmation, setConfirmation] = useState("");
  const [acknowledged, setAcknowledged] = useState(false);
  const [requestId] = useState(() => crypto.randomUUID());
  const [actionError, setActionError] = useState("");
  const query = useQuery({ queryKey: ["platform", "product-pilot"], queryFn: platformApi.productPilot });
  const mutation = useMutation({
    mutationFn: () => platformApi.enableProductPilotCrm({ tenantCode: confirmation, requestId, acknowledgeStagingOnly: acknowledged }),
    onSuccess: async () => { setActionError(""); await queryClient.invalidateQueries({ queryKey: ["platform", "product-pilot"] }); },
    onError: (error: unknown) => setActionError(apiErrorMessage(error, "The pilot action could not be completed safely.")),
  });

  const data = query.data as AnyRecord | undefined;
  const tenant = (data?.tenant ?? {}) as AnyRecord;
  const entitlements = (data?.entitlements ?? {}) as AnyRecord;
  const records = (data?.records ?? {}) as AnyRecord;
  const eligible = data?.eligible === true;
  const ready = eligible && confirmation === TENANT_CODE && acknowledged && !mutation.isPending;
  const cards = useMemo<Array<[string, unknown]>>(() => [
    ["Customers", records.customers ?? 0],
    ["Jobs", records.jobs ?? 0],
    ["Routes", records.routes ?? 0],
  ], [records.customers, records.jobs, records.routes]);

  if (query.isLoading) return <PLoading />;
  if (query.error) return <PError message={apiErrorMessage(query.error, "Pilot readiness could not be loaded safely.")} />;

  return <div className="space-y-7">
    <PHeader eyebrow="Staging certification" title="Product Pilot Harness" description="Governed preparation for the isolated customer-pilot tenant. This control can enable CRM only; all customer, job and route records must be created through the tenant product." />

    <div role="status" className="rounded-2xl border border-amber-300 bg-amber-50 px-5 py-4 text-sm text-amber-950">
      <strong>STAGING ONLY</strong> · Fixed tenant {TENANT_CODE} · SHA {String(data?.deployedSha ?? "unknown")} · No SQL seeding · No telemetry simulation
    </div>

    <PCard className="p-6">
      <div className="flex items-start gap-3"><ShieldCheck className="mt-0.5 h-5 w-5 text-teal-400" /><div><h2 className="text-lg font-bold text-white">Tenant control state</h2><p className="mt-1 text-sm text-slate-400">{String(tenant.name ?? "Certification tenant")} · {String(tenant.code ?? TENANT_CODE)} · {String(tenant.status ?? "Unknown")} · policy {String(tenant.entitlementPolicy ?? "Unknown")}</p></div></div>
      <div className="mt-5 grid gap-3 sm:grid-cols-2">
        <State label="CRM / Customer Master" value={entitlements.crm === true ? "Enabled" : "Disabled"} good={entitlements.crm === true} />
        <State label="Dispatch / Jobs & Routes" value={entitlements.dispatch === true ? "Enabled" : "Disabled"} good={entitlements.dispatch === true} />
      </div>
      {!eligible ? <p role="alert" className="mt-4 text-sm font-semibold text-rose-300">Pilot activation is blocked: the tenant must be Active and use the package_allowlist entitlement policy.</p> : null}
    </PCard>

    <PCard className="p-6">
      <h2 className="text-lg font-bold text-white">Enable the governed customer workflow</h2>
      <p className="mt-1 text-sm text-slate-400">Type the immutable tenant code. This audited, replay-safe action only enables CRM for this tenant.</p>
      <div className="mt-5 max-w-xl space-y-4">
        <PField label={`Type ${TENANT_CODE}`}><PInput value={confirmation} onChange={(event) => setConfirmation(event.target.value)} autoComplete="off" /></PField>
        <label className="flex items-start gap-3 text-sm text-slate-300"><input className="mt-1" type="checkbox" checked={acknowledged} onChange={(event) => setAcknowledged(event.target.checked)} /><span>I confirm this is the isolated staging certification tenant and not a production customer.</span></label>
        {actionError ? <p role="alert" className="text-sm font-semibold text-rose-300">{actionError}</p> : null}
        <PButton disabled={!ready || entitlements.crm === true} onClick={() => mutation.mutate()}>{mutation.isPending ? "Enabling…" : entitlements.crm === true ? "CRM already enabled" : "Enable CRM for certification tenant"}</PButton>
        <p className="text-xs text-slate-500">Request ID: {requestId}</p>
      </div>
    </PCard>

    <PCard className="p-6">
      <h2 className="text-lg font-bold text-white">Customer-facing workflow</h2>
      <p className="mt-1 text-sm text-slate-400">Sign out of Platform Admin, then sign in separately as the certification Tenant Administrator. Create and verify data in this order.</p>
      <div className="mt-5 grid gap-3 sm:grid-cols-3">{cards.map(([label, count]) => <div key={label} className="rounded-xl border border-slate-700 bg-slate-900/60 p-4"><p className="text-xs uppercase tracking-wider text-slate-500">{label}</p><p className="mt-1 text-2xl font-bold text-white">{String(count)}</p></div>)}</div>
      <div className="mt-5 flex flex-wrap gap-3">
        {eligible ? <><WorkflowLink href="/customers" label="1. Customer Master" /><WorkflowLink href="/jobs" label="2. Jobs" /><WorkflowLink href="/route-planning" label="3. Route Planning" /></> : <p className="text-sm text-rose-300">Workflow links remain unavailable until the tenant control state is eligible.</p>}
      </div>
    </PCard>
  </div>;
}

function State({ label, value, good }: { label: string; value: string; good: boolean }) {
  return <div className="rounded-xl border border-slate-700 bg-slate-900/60 p-4"><p className="text-xs uppercase tracking-wider text-slate-500">{label}</p><p className={`mt-1 font-bold ${good ? "text-emerald-300" : "text-amber-300"}`}>{value}</p></div>;
}

function WorkflowLink({ href, label }: { href: string; label: string }) {
  return <a className="inline-flex items-center gap-2 rounded-xl border border-slate-700 px-4 py-2 text-sm font-semibold text-slate-200 hover:border-teal-400" href={href} target="_blank" rel="noreferrer">{label}<ExternalLink className="h-4 w-4" /></a>;
}
