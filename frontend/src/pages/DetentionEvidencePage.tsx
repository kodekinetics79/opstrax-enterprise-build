import { useEffect, useState } from "react";
import { useParams } from "react-router-dom";
import { detentionApi } from "@/services/detentionApi";
import type { AnyRecord } from "@/types";

// PUBLIC no-login detention evidence page — the artifact an AP clerk verifies in under 2 minutes.
// Plain language, the shipper's own references first, the math shown, the notice log visible.
// Print stylesheet = the one-page PDF. No cryptography language ('Evidence ref' only).
export function DetentionEvidencePage() {
  const { token = "" } = useParams<{ token: string }>();
  const [data, setData] = useState<AnyRecord | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const res = await detentionApi.publicEvidence(token);
        if (!cancelled) setData(res as AnyRecord);
      } catch {
        if (!cancelled) setError("This evidence link is unavailable, expired, or revoked.");
      }
    })();
    return () => { cancelled = true; };
  }, [token]);

  if (error) return <div className="mx-auto max-w-2xl p-10 text-center text-slate-600">{error}</div>;
  if (!data) return <div className="mx-auto max-w-2xl p-10 text-center text-slate-400">Loading evidence…</div>;

  const ev = (data.evidence ?? {}) as AnyRecord;
  const job = (ev.job ?? {}) as AnyRecord;
  const refs = (job.references ?? {}) as AnyRecord;
  const appt = (ev.appointment ?? {}) as AnyRecord;
  const clock = (ev.clock ?? {}) as AnyRecord;
  const intervals = (ev.intervals ?? {}) as AnyRecord;
  const comp = (ev.computation ?? {}) as AnyRecord;
  const rule = (ev.ruleCardSnapshot ?? {}) as AnyRecord;
  const notices = (ev.noticeLog ?? []) as AnyRecord[];

  const row = (label: string, value: unknown) =>
    value != null && String(value) !== "" ? (
      <div className="flex justify-between gap-6 border-b border-slate-100 py-1.5 text-sm">
        <span className="text-slate-500">{label}</span>
        <span className="text-right font-medium text-slate-900">{String(value)}</span>
      </div>
    ) : null;

  return (
    <div className="mx-auto max-w-2xl px-6 py-8 print:py-2">
      <div className="mb-6 border-b border-slate-200 pb-4">
        <h1 className="text-xl font-bold text-slate-900">Detention charge — audit-ready evidence</h1>
        <p className="text-sm text-slate-500">
          {String((ev.customer as AnyRecord)?.name ?? "")} · {String((ev.geofence as AnyRecord)?.name ?? "")}
          {" · "}Evidence ref: <span className="font-mono">{String(data.evidenceRef ?? "")}</span>
        </p>
      </div>

      <section className="mb-5">
        <h2 className="mb-1 text-sm font-bold uppercase tracking-wide text-slate-600">Your references</h2>
        {row("PO number", refs.poNumber)}
        {row("BOL number", refs.bolNumber)}
        {row("Rate confirmation", refs.rateConNumber)}
        {row("Appointment ref", refs.appointmentRef)}
        {row("Load", job.jobCode)}
      </section>

      <section className="mb-5">
        <h2 className="mb-1 text-sm font-bold uppercase tracking-wide text-slate-600">Appointment vs actual</h2>
        {row("Appointment", appt.plannedAt)}
        {row("Arrived", appt.arrivedAt)}
        {row("On time", appt.onTime === true ? "Yes" : appt.onTime === false ? "No" : undefined)}
        {row("Billable clock started", clock.clockStartAt)}
        {Number(clock.earlyArrivalExcludedMinutes) > 0 &&
          row("Early-arrival time excluded", `${String(clock.earlyArrivalExcludedMinutes)} minutes (not billed)`)}
      </section>

      <section className="mb-5">
        <h2 className="mb-1 text-sm font-bold uppercase tracking-wide text-slate-600">Time on site</h2>
        {row("Billed from", intervals.billedFrom)}
        {row("Billed to", intervals.billedTo)}
        <p className="pt-1 text-xs text-slate-400">{String(intervals.note ?? "")} — every timing ambiguity is resolved in your favor.</p>
      </section>

      <section className="mb-5">
        <h2 className="mb-1 text-sm font-bold uppercase tracking-wide text-slate-600">The charge, computed by your terms</h2>
        {row("Total dwell", comp.dwellMinutes != null ? `${String(comp.dwellMinutes)} minutes` : undefined)}
        {row("Free time applied", comp.freeMinutesApplied != null ? `${String(comp.freeMinutesApplied)} minutes` : undefined)}
        {row("Billable (rounded DOWN)", comp.billableMinutes != null ? `${String(comp.billableMinutes)} minutes in ${String(rule.incrementMinutes)}-min increments` : undefined)}
        {row("Rate", rule.ratePerHour != null ? `${String(rule.ratePerHour)}/hour` : undefined)}
        {row("Amount", comp.amount != null ? `${String(comp.amount)} ${String(comp.currency ?? "")}` : undefined)}
      </section>

      {notices.length > 0 && (
        <section className="mb-5">
          <h2 className="mb-1 text-sm font-bold uppercase tracking-wide text-slate-600">Notice log</h2>
          {notices.map((n, i) => (
            <p key={i} className="py-1 text-sm text-slate-700">
              {String(n.loggedAt ?? "")} — your designated contact{n.recipient ? ` (${String(n.recipient)})` : ""} was
              notified that the meter was running, before charges began.
            </p>
          ))}
        </section>
      )}

      <div className="mt-6 flex items-center justify-between border-t border-slate-200 pt-4 print:hidden">
        <p className="text-xs text-slate-400">Record generated {String(data.generatedAt ?? "")} — unaltered since generation.</p>
        <button type="button" className="rounded-lg border border-slate-300 px-3 py-1.5 text-sm text-slate-700 hover:bg-slate-50" onClick={() => window.print()}>
          Print / save PDF
        </button>
      </div>
    </div>
  );
}
