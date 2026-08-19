import { useEffect, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { ShieldCheck, Clock, KeyRound } from "lucide-react";
import type { AnyRecord } from "@/types";
import { platformApi } from "@/services/platformApi";
import { usePlatformAuth } from "@/hooks/usePlatformAuth";
import { PHeader, PCard, PKpi, PBadge, PButton, PField, PInput, PSelect, PLoading, PError, PEmpty } from "./ui";

// Support Access — the break-glass surface.
//
// The whole point of this screen is that vendor access to a customer's data is
// never ambient: it is granted for a stated reason, to one named user, for a
// bounded number of minutes, read-only within a published scope, revocable on
// sight, and written to BOTH the platform audit log and the tenant's own audit
// log so the customer can see it without asking.

function countdown(seconds: number): string {
  if (seconds <= 0) return "expired";
  const m = Math.floor(seconds / 60);
  const s = seconds % 60;
  return `${m}m ${String(s).padStart(2, "0")}s`;
}

export function PlatformSupportAccessPage() {
  const qc = useQueryClient();
  const { can } = usePlatformAuth();
  const canGrant = can("platform:impersonation:start");

  const { data, isLoading, error } = useQuery({
    queryKey: ["platform", "support-access"],
    queryFn: platformApi.supportAccess,
    // Grants expire on a clock, so the ledger has to move on its own.
    refetchInterval: 15_000,
  });
  const { data: tenants } = useQuery({ queryKey: ["platform", "tenants"], queryFn: platformApi.tenants });

  const [startOpen, setStartOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const [err, setErr] = useState<string | null>(null);
  const [tick, setTick] = useState(0);

  // Local ticking so the countdown reads live between refetches.
  useEffect(() => {
    const t = setInterval(() => setTick((n) => n + 1), 1000);
    return () => clearInterval(t);
  }, []);

  const ledger = data as AnyRecord | undefined;
  const grants = (ledger?.grants ?? []) as AnyRecord[];
  const scope = (ledger?.readOnlyScope ?? []) as string[];
  const enabled = Boolean(ledger?.enabled);
  const active = grants.filter((g) => g.isActive);

  const refresh = () => qc.invalidateQueries({ queryKey: ["platform", "support-access"] });

  const endGrant = async (id: number) => {
    setBusy(true); setErr(null);
    try {
      await platformApi.endSupportAccess(id);
      setNotice("Support access ended and its session revoked.");
      refresh();
    } catch (e) { setErr(e instanceof Error ? e.message : "Could not end the grant"); }
    finally { setBusy(false); }
  };

  if (isLoading) return <PLoading />;
  if (error) return <PError message={(error as Error)?.message} />;

  return (
    <div className="space-y-7">
      <PHeader
        eyebrow="Support Access"
        title="Break-glass access"
        description="Time-boxed, reason-captured, read-only access to a tenant account. Every grant is written to the customer's own audit log as well as ours, and can be revoked on sight."
        actions={canGrant && enabled ? (
          <PButton onClick={() => { setStartOpen((v) => !v); setErr(null); }}>
            <KeyRound className="h-4 w-4" /> {startOpen ? "Cancel" : "Request access"}
          </PButton>
        ) : undefined}
      />

      {!enabled && (
        <div className="rounded-xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-800">
          <strong>Support access is disabled by deployment policy.</strong> The ledger below is still readable, but no new
          grant can be issued until <code className="font-mono text-xs">PlatformImpersonation:Enabled</code> is turned on
          for this environment. It is off by default so that a deployment must opt in to vendor access.
        </div>
      )}

      {notice && <div className="rounded-xl border border-teal-500/30 bg-teal-500/5 px-4 py-2.5 text-sm text-teal-700">{notice}</div>}
      {err && <div className="rounded-xl border border-red-200 bg-red-50 px-4 py-2.5 text-sm text-red-700">{err}</div>}

      <div className="grid gap-4 sm:grid-cols-3">
        <PKpi label="Active grants" value={active.length} tone={active.length > 0 ? "warn" : "good"}
              sub={active.length === 0 ? "no one holds tenant access" : "live vendor access right now"} />
        <PKpi label="Grants on record" value={grants.length} sub="most recent 100" />
        <PKpi label="Access mode" value="Read-only" sub={`${scope.length} permitted API areas`} />
      </div>

      {startOpen && canGrant && enabled && (
        <StartGrantCard
          tenants={(tenants ?? []) as AnyRecord[]}
          onDone={(msg) => { setStartOpen(false); setNotice(msg); refresh(); }}
          onError={setErr}
        />
      )}

      {/* The scope is published rather than described. "Read-only" is a claim; this is the evidence. */}
      <PCard className="p-5">
        <div className="flex items-center gap-2">
          <ShieldCheck className="h-4 w-4 text-teal-600" />
          <h3 className="text-sm font-semibold text-slate-900">What a support session can reach</h3>
        </div>
        <p className="mt-1 text-xs leading-5 text-slate-500">
          An impersonated session is refused at the authentication edge for anything outside this list, and for every
          write regardless of path. Sign-out is the only mutation it is permitted.
        </p>
        <div className="mt-3 flex flex-wrap gap-2">
          {scope.map((s) => (
            <span key={s} className="rounded-lg border border-slate-200 bg-slate-50 px-2.5 py-1 font-mono text-[11px] text-slate-600">
              GET {s}
            </span>
          ))}
        </div>
      </PCard>

      {grants.length === 0 ? (
        <PEmpty title="No support access has ever been granted" subtitle="Grants appear here the moment one is issued." />
      ) : (
        <PCard className="overflow-hidden">
          <div className="overflow-x-auto">
            <table className="w-full min-w-[980px] text-left text-sm">
              <thead className="border-b border-slate-200 bg-slate-50">
                <tr className="text-xs uppercase tracking-wider text-slate-500">
                  {["Status", "Tenant", "Operator", "Acting as", "Reason", "Granted", "Remaining", ""].map((h) => (
                    <th key={h} className="px-5 py-3 font-semibold">{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-200">
                {grants.map((g) => {
                  const isActive = Boolean(g.isActive);
                  // The server's remaining-seconds, decremented locally between refetches.
                  const remaining = Math.max(0, Number(g.secondsRemaining ?? 0) - (tick % 15));
                  return (
                    <tr key={String(g.id)} className={isActive ? "bg-amber-50/50" : ""}>
                      <td className="px-5 py-3">
                        <PBadge value={isActive ? "active" : g.endedAt ? "ended" : "expired"} />
                      </td>
                      <td className="px-5 py-3 font-medium text-slate-800">{String(g.tenant)}</td>
                      <td className="px-5 py-3 text-slate-600">{String(g.operatorEmail ?? "—")}</td>
                      <td className="px-5 py-3 text-slate-600">
                        {String(g.targetName ?? "—")}
                        <span className="ml-1.5 text-[11px] text-slate-400">{String(g.targetEmail ?? "")}</span>
                      </td>
                      <td className="px-5 py-3 max-w-xs truncate text-slate-500" title={String(g.reason ?? "")}>
                        {String(g.reason ?? "—")}
                      </td>
                      <td className="px-5 py-3 font-mono text-xs text-slate-500">
                        {String(g.createdAt ?? "").slice(0, 16).replace("T", " ")}
                      </td>
                      <td className="px-5 py-3">
                        {isActive ? (
                          <span className="inline-flex items-center gap-1 font-mono text-xs font-semibold text-amber-700">
                            <Clock className="h-3 w-3" /> {countdown(remaining)}
                          </span>
                        ) : (
                          <span className="font-mono text-xs text-slate-400">
                            {g.endedAt ? `ended ${String(g.endedAt).slice(11, 16)}` : "expired"}
                          </span>
                        )}
                      </td>
                      <td className="px-5 py-3 text-right">
                        {isActive && canGrant && (
                          <PButton variant="danger" disabled={busy} onClick={() => endGrant(Number(g.id))}>
                            End now
                          </PButton>
                        )}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        </PCard>
      )}
    </div>
  );
}

function StartGrantCard({ tenants, onDone, onError }: {
  tenants: AnyRecord[];
  onDone: (msg: string) => void;
  onError: (msg: string) => void;
}) {
  const [companyId, setCompanyId] = useState("");
  const [targetUserId, setTargetUserId] = useState("");
  const [reason, setReason] = useState("");
  const [minutes, setMinutes] = useState("30");
  const [busy, setBusy] = useState(false);
  const [issued, setIssued] = useState<AnyRecord | null>(null);

  const { data: users } = useQuery({
    queryKey: ["platform", "tenant", companyId, "users"],
    queryFn: () => platformApi.tenantUsers(Number(companyId)),
    enabled: companyId !== "",
  });
  const tenantUsers = ((users as AnyRecord)?.users ?? []) as AnyRecord[];

  const submit = async () => {
    setBusy(true);
    try {
      const res = await platformApi.startSupportAccess(Number(companyId), {
        targetUserId: Number(targetUserId),
        reason: reason.trim(),
        minutes: Number(minutes),
      });
      setIssued(res);
      onDone(`Support access granted for ${minutes} minutes — grant ${String(res.grantRef).slice(0, 8)}`);
    } catch (e) { onError(e instanceof Error ? e.message : "Could not start support access"); }
    finally { setBusy(false); }
  };

  const valid = companyId !== "" && targetUserId !== "" && reason.trim().length >= 10;

  if (issued) {
    return (
      <PCard className="p-5">
        <h3 className="text-sm font-semibold text-slate-900">Support session token</h3>
        <p className="mt-1 text-xs text-slate-500">
          Shown once. Use it as the tenant-app bearer to view the account as this user. The grant is already visible in
          the customer's own audit log, and expires on its own.
        </p>
        <code className="mt-3 block overflow-x-auto rounded-lg border border-slate-200 bg-slate-50 px-3 py-2 font-mono text-xs text-slate-800">
          {String(issued.token)}
        </code>
        <p className="mt-2 text-[11px] text-slate-400">Grant reference {String(issued.grantRef)}</p>
      </PCard>
    );
  }

  return (
    <PCard className="p-5">
      <h3 className="text-sm font-semibold text-slate-900">Request support access</h3>
      <div className="mt-3 grid gap-3 sm:grid-cols-2">
        <PField label="Tenant">
          <PSelect value={companyId} onChange={(e) => { setCompanyId(e.target.value); setTargetUserId(""); }}>
            <option value="">— Select tenant —</option>
            {tenants.map((t) => <option key={String(t.id)} value={String(t.id)}>{String(t.name)}</option>)}
          </PSelect>
        </PField>
        <PField label="Act as (an active user of that tenant)">
          <PSelect value={targetUserId} disabled={companyId === ""} onChange={(e) => setTargetUserId(e.target.value)}>
            <option value="">— Select user —</option>
            {tenantUsers
              .filter((u) => String(u.status) === "Active")
              .map((u) => (
                <option key={String(u.id)} value={String(u.id)}>
                  {String(u.fullName)} · {String(u.roleName)}
                </option>
              ))}
          </PSelect>
        </PField>
      </div>
      <div className="mt-3 grid gap-3 sm:grid-cols-[1fr_160px]">
        <PField label="Reason (recorded in the customer's audit log — minimum 10 characters)">
          <PInput
            value={reason}
            maxLength={400}
            placeholder="e.g. Ticket 4821 — driver reports DVIR defect not clearing"
            onChange={(e) => setReason(e.target.value)}
          />
        </PField>
        <PField label="Duration">
          <PSelect value={minutes} onChange={(e) => setMinutes(e.target.value)}>
            {[5, 15, 30, 45, 60].map((m) => <option key={m} value={String(m)}>{m} minutes</option>)}
          </PSelect>
        </PField>
      </div>
      <div className="mt-4 flex items-center gap-2">
        <PButton disabled={busy || !valid} onClick={submit}>
          {busy ? "Granting…" : "Grant read-only access"}
        </PButton>
        <span className="text-[11px] text-slate-400">
          Maximum 60 minutes. The grant expires by itself; you do not have to remember to close it.
        </span>
      </div>
    </PCard>
  );
}
