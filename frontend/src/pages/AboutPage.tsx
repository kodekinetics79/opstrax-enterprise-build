import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import {
  BadgeCheck, Building2, Copy, Globe, LifeBuoy, Mail, Phone, ShieldCheck,
} from "lucide-react";
import { aboutApi } from "@/services/aboutApi";
import { frontendBuild, useRuntimeDiagnostics } from "@/services/runtimeDiagnostics";
import { useAuth } from "@/hooks/useAuth";

type AnyRecord = Record<string, unknown>;

// ─────────────────────────────────────────────────────────────────────────────
// Tenant-facing About page — identity & assurance surface, not a debug console.
//
// This page deliberately shows NO runtime internals: no git SHAs, no API base
// URL, no environment names, no worker/database health detail, no table counts.
// Those are operator diagnostics (SOC 2 data-classification: internal) and
// exposing them to tenant users was flagged in security review — they fingerprint
// the deployment and generate support tickets ("what is 'Not verified / stale'?").
// Operators get the full picture from the authenticated /health/deep endpoint.
//
// What a tenant needs here: what am I running (version + a short, speakable
// build reference for support calls), who is it licensed to, is the service
// operational (one coarse line — never assumed green), how do I reach support,
// what's included, and the standing legal text.
// ─────────────────────────────────────────────────────────────────────────────

const FALLBACK_MODULES = [
  "Fleet Command Center", "Live Control Tower", "Dispatch Board", "Jobs & Orders",
  "Route Planning", "Driver & Vehicle Management", "Maintenance & Work Orders",
  "DVIR / Inspections", "Safety & AI Dashcam", "Compliance & HOS/ELD Framework",
  "Fuel, Expenses & Cost Intelligence", "Reports & Analytics",
  "Integrations & API Readiness", "AI Copilot",
];

// Coarse, plain-language service status. Never assumes green: unknown states
// resolve to a neutral "unavailable", not "operational".
function serviceStatus(state: string | undefined, isError: boolean): { label: string; dot: string; text: string } {
  if (isError || !state || state === "Unavailable" || state === "Disconnected")
    return { label: "Status unavailable", dot: "bg-slate-300", text: "text-slate-600" };
  if (state === "Live" || state === "Staging")
    return { label: "All systems operational", dot: "bg-emerald-500", text: "text-emerald-700" };
  if (state === "Starting")
    return { label: "Services are starting up", dot: "bg-amber-400", text: "text-amber-700" };
  if (state === "Demo Data")
    return { label: "Demo environment", dot: "bg-slate-400", text: "text-slate-600" };
  // "Stale" and anything unrecognized: degraded, in words a fleet admin can act on.
  return { label: "Partially degraded — monitored automatically", dot: "bg-amber-400", text: "text-amber-700" };
}

// Chip tone for the subscription status. Trial is a deliberate amber — it should
// read as "temporary", not as an error and not as a settled state.
function licenseStatusTone(status: string): string {
  const s = status.toLowerCase();
  if (s === "active") return "border-emerald-200 bg-emerald-50 text-emerald-700";
  if (s === "trial") return "border-amber-200 bg-amber-50 text-amber-700";
  if (s === "suspended" || s === "cancelled" || s === "canceled" || s === "expired")
    return "border-red-200 bg-red-50 text-red-700";
  return "border-slate-200 bg-slate-50 text-slate-600";
}

export function AboutPage() {
  const { data: platformRaw } = useQuery({ queryKey: ["about-platform"], queryFn: aboutApi.platform, staleTime: 300_000 });
  const { data: licenseRaw } = useQuery({ queryKey: ["about-license"], queryFn: aboutApi.license, staleTime: 300_000 });
  const runtimeQuery = useRuntimeDiagnostics();
  const { session } = useAuth();
  const [copied, setCopied] = useState(false);

  const platform = platformRaw as AnyRecord | undefined;
  const license = licenseRaw as AnyRecord | undefined;
  const support = platform?.support as AnyRecord | undefined;
  const company = (session?.company ?? {}) as AnyRecord;

  const version = String(platform?.version ?? "Enterprise");
  // Short build reference: speakable over the phone, enough for support to
  // correlate to an exact build — without publishing full commit hashes.
  const buildRef = (frontendBuild.sha || "").slice(0, 7) || "local";
  const supportReference = `OpsTrax ${version} · build ${buildRef}`;
  const status = serviceStatus(runtimeQuery.data?.state, runtimeQuery.isError);

  const modules = Array.isArray(platform?.modules) && (platform!.modules as unknown[]).length > 0
    ? (platform!.modules as unknown[]).map(String)
    : FALLBACK_MODULES;

  const plan = [company.planName, company.plan_name, company.subscriptionPlan, company.subscription_plan]
    .map((v) => (v == null ? "" : String(v)))
    .find((v) => v.trim().length > 0);

  const copyReference = async () => {
    try {
      await navigator.clipboard.writeText(supportReference);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch { /* clipboard unavailable — the reference is still readable on screen */ }
  };

  return (
    <div className="flex h-full flex-col gap-6 overflow-y-auto pb-8">

      {/* ── Product identity ── */}
      <div className="panel p-6">
        <div className="flex flex-wrap items-start justify-between gap-4">
          <div className="flex items-center gap-4">
            <div className="flex h-12 w-12 items-center justify-center rounded-xl bg-slate-900">
              <BadgeCheck className="h-6 w-6 text-teal-400" />
            </div>
            <div>
              <h1 className="text-xl font-extrabold text-slate-900">
                {String(platform?.fullProductName ?? "OpsTrax Transport Management Solution")}
              </h1>
              <p className="mt-0.5 text-sm text-slate-500">
                Enterprise platform for fleet operations, dispatch, maintenance, safety, and compliance.
              </p>
            </div>
          </div>
          <div className="text-right">
            <p className="text-[10px] font-bold uppercase tracking-widest text-slate-500">Version</p>
            <p className="mt-0.5 font-semibold text-slate-800">{version}</p>
            <button
              type="button"
              onClick={copyReference}
              className="mt-1.5 inline-flex items-center gap-1.5 rounded-lg border border-slate-200 px-2.5 py-1 text-xs font-semibold text-slate-600 hover:border-teal-300 hover:text-teal-700"
              title="Copy the support reference for tickets and calls"
            >
              <Copy className="h-3 w-3" />
              {copied ? "Copied" : `Build ${buildRef}`}
            </button>
          </div>
        </div>
      </div>

      {/* ── Licensed to + Service status ── */}
      <div className="grid gap-6 lg:grid-cols-2">
        <div className="panel p-5">
          <div className="mb-3 flex items-center justify-between gap-3">
            <div className="flex items-center gap-2">
              <Building2 className="h-4 w-4 text-slate-500" />
              <p className="text-[10px] font-bold uppercase tracking-widest text-slate-500">Licensed to</p>
            </div>
            {typeof license?.status === "string" && license.status && (
              <span className={`rounded-full border px-2.5 py-0.5 text-xs font-bold capitalize ${licenseStatusTone(String(license.status))}`}>
                {String(license.status)}
              </span>
            )}
          </div>
          <p className="text-lg font-bold text-slate-900">{String(company.name ?? "—")}</p>
          <div className="mt-2 flex flex-wrap gap-x-6 gap-y-1 text-sm text-slate-600">
            {company.id != null && <span>Org ID: <span className="font-semibold">{String(company.id)}</span></span>}
            {(license?.plan ?? plan) && <span>Plan: <span className="font-semibold">{String(license?.plan ?? plan)}</span></span>}
            {license?.billingCycle != null && <span>Billing: <span className="font-semibold capitalize">{String(license.billingCycle)}</span></span>}
          </div>

          {/* Seats — usage bar only when a real limit exists; never a made-up quota */}
          {license?.seatsUsed != null && (
            <div className="mt-3">
              <p className="text-sm text-slate-600">
                <span className="font-semibold text-slate-800">{String(license.seatsUsed)}</span>
                {license.seatLimit != null ? ` of ${String(license.seatLimit)} seats in use` : " active seats in use"}
              </p>
              {license.seatLimit != null && Number(license.seatLimit) > 0 && (
                <div className="mt-1.5 h-1.5 w-full max-w-xs overflow-hidden rounded-full bg-slate-100">
                  <div
                    className={`h-full rounded-full ${Number(license.seatsUsed) >= Number(license.seatLimit) ? "bg-amber-400" : "bg-teal-500"}`}
                    style={{ width: `${Math.min(100, (Number(license.seatsUsed) / Number(license.seatLimit)) * 100)}%` }}
                  />
                </div>
              )}
            </div>
          )}

          {/* Trial countdown / contract term — only from real dates */}
          {(() => {
            const trialEnds = license?.trialEndsAt ? new Date(String(license.trialEndsAt)) : null;
            const contractEnd = license?.contractEnd ? new Date(String(license.contractEnd)) : null;
            const isTrial = String(license?.status ?? "").toLowerCase() === "trial";
            if (isTrial && trialEnds && !Number.isNaN(trialEnds.getTime())) {
              const daysLeft = Math.ceil((trialEnds.getTime() - Date.now()) / 86_400_000);
              return (
                <p className={`mt-3 text-sm font-semibold ${daysLeft <= 0 ? "text-red-600" : daysLeft <= 7 ? "text-amber-600" : "text-slate-600"}`}>
                  {daysLeft <= 0
                    ? `Trial ended ${trialEnds.toLocaleDateString()} — contact support to continue`
                    : `Trial ends ${trialEnds.toLocaleDateString()} (${daysLeft} day${daysLeft === 1 ? "" : "s"} left)`}
                </p>
              );
            }
            if (contractEnd && !Number.isNaN(contractEnd.getTime()))
              return <p className="mt-3 text-sm text-slate-600">Contract runs through <span className="font-semibold">{contractEnd.toLocaleDateString()}</span></p>;
            return null;
          })()}
        </div>

        <div className="panel p-5">
          <div className="mb-3 flex items-center justify-between gap-3">
            <p className="text-[10px] font-bold uppercase tracking-widest text-slate-500">Service status</p>
            <button
              type="button"
              className="text-xs font-semibold text-teal-700 hover:text-teal-600"
              onClick={() => runtimeQuery.refetch()}
              disabled={runtimeQuery.isFetching}
            >
              {runtimeQuery.isFetching ? "Checking…" : "Recheck"}
            </button>
          </div>
          <div className="flex items-center gap-2.5">
            <span className={`inline-block h-2.5 w-2.5 rounded-full ${status.dot}`} />
            <p className={`text-base font-bold ${status.text}`}>{status.label}</p>
          </div>
          {runtimeQuery.data?.checkedAt && (
            <p className="mt-2 text-xs text-slate-500">
              Last checked {new Date(runtimeQuery.data.checkedAt).toLocaleTimeString()}
            </p>
          )}
        </div>
      </div>

      {/* ── Support ── */}
      <div className="panel p-5">
        <div className="mb-3 flex items-center gap-2">
          <LifeBuoy className="h-4 w-4 text-slate-500" />
          <p className="text-[10px] font-bold uppercase tracking-widest text-slate-500">Support</p>
        </div>
        <p className="mb-3 text-sm text-slate-600">
          Quote <span className="font-semibold text-slate-800">{supportReference}</span> when contacting support — it identifies your exact release.
        </p>
        <div className="flex flex-wrap gap-x-8 gap-y-2">
          <a href={`mailto:${String(support?.email ?? "info@kodekinetics.com")}`} className="flex items-center gap-2 text-sm font-medium text-teal-700 hover:text-teal-600">
            <Mail className="h-3.5 w-3.5" />
            {String(support?.email ?? "info@kodekinetics.com")}
          </a>
          <a href={`tel:${String(support?.phone ?? "+15714305333").replace(/[^+\d]/g, "")}`} className="flex items-center gap-2 text-sm font-medium text-slate-600 hover:text-slate-800">
            <Phone className="h-3.5 w-3.5" />
            {String(support?.phone ?? "+1 571 430 5333")}
          </a>
          <a href={`https://${String(support?.website ?? "www.kodekinetics.com").replace(/^https?:\/\//, "")}`} target="_blank" rel="noopener noreferrer" className="flex items-center gap-2 text-sm font-medium text-slate-600 hover:text-slate-800">
            <Globe className="h-3.5 w-3.5" />
            {String(support?.website ?? "www.kodekinetics.com")}
          </a>
        </div>
      </div>

      {/* ── Included modules ── */}
      <div className="panel p-5">
        <p className="mb-3 text-[10px] font-bold uppercase tracking-widest text-slate-500">Included modules</p>
        <div className="grid grid-cols-2 gap-x-6 gap-y-1.5 sm:grid-cols-3 lg:grid-cols-4">
          {modules.map((m) => (
            <p key={m} className="text-sm text-slate-700">{m}</p>
          ))}
        </div>
      </div>

      {/* ── Legal & compliance ── */}
      <div className="panel p-5">
        <div className="mb-2 flex items-center gap-2">
          <ShieldCheck className="h-4 w-4 text-slate-500" />
          <p className="text-[10px] font-bold uppercase tracking-widest text-slate-500">Legal &amp; Compliance</p>
        </div>
        <p className="text-sm leading-relaxed text-slate-600">
          {String(platform?.disclaimer ?? "OpsTrax provides operational, compliance management, and audit-readiness tools. Final regulatory compliance remains the responsibility of the carrier/operator. ELD certification and regulatory approval depend on the connected device/provider and applicable country requirements.")}
        </p>
      </div>

      {/* ── Developer footer — one muted line, not a brochure ── */}
      <p className="px-1 text-center text-xs text-slate-400">
        OpsTrax is developed by{" "}
        <a href="https://www.kodekinetics.com" target="_blank" rel="noopener noreferrer" className="font-medium text-slate-500 hover:text-teal-600">
          Kode Kinetics
        </a>
        . © {new Date().getFullYear()} All rights reserved.
      </p>
    </div>
  );
}
