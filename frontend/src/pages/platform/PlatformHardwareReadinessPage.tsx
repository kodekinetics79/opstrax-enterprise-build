import { useMemo, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { Cable, CheckCircle2, CircleAlert, Plus } from "lucide-react";
import type { AnyRecord } from "@/types";
import { platformApi } from "@/services/platformApi";
import { usePlatformAuth } from "@/hooks/usePlatformAuth";
import { PBadge, PButton, PCard, PDrawer, PEmpty, PError, PField, PHeader, PInput, PKpi, PLoading } from "./ui";

type CandidateResponse = {
  runtime?: AnyRecord;
  supportedPaths?: AnyRecord[];
  candidates?: AnyRecord[];
};

const blankIntake = {
  manufacturer: "",
  deviceModel: "",
  hardwareRevision: "",
  firmwareVersion: "",
  externalHoldReason: "Physical bench, route, failure-recovery and soak evidence is pending.",
};

const blankDeclaration = {
  protocolNames: "GT06",
  supportedFields: "imei, latitude, longitude, speedKph, courseDeg, fixTimeUtc",
  supportedEvents: "login, heartbeat, location, alarm, status",
  supportedCommands: "",
  knownLimitations: "Exact supplier firmware and physical behavior remain unverified until the hardware evidence run.",
  declarationSourceReference: "",
};

function commaList(value: string): string[] {
  return value.split(",").map((item) => item.trim()).filter(Boolean);
}

function stringList(value: unknown): string[] {
  if (Array.isArray(value)) return value.map(String);
  return [];
}

function shortSha(value: unknown): string {
  const sha = String(value ?? "");
  return sha.length >= 12 ? `${sha.slice(0, 12)}…` : sha || "Unavailable";
}

export function PlatformHardwareReadinessPage() {
  const queryClient = useQueryClient();
  const { can } = usePlatformAuth();
  const canManage = can("platform:devices:manage");
  const query = useQuery({
    queryKey: ["platform", "hardware-readiness"],
    queryFn: platformApi.deviceCompatibilityCandidates,
  });
  const [intakeOpen, setIntakeOpen] = useState(false);
  const [declarationTarget, setDeclarationTarget] = useState<AnyRecord | null>(null);
  const [intake, setIntake] = useState(blankIntake);
  const [declaration, setDeclaration] = useState(blankDeclaration);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const payload = (query.data ?? {}) as CandidateResponse;
  const candidates = payload.candidates ?? [];
  const declared = useMemo(
    () => candidates.filter((row) => String(row.capabilityDeclarationStatus) === "EngineeringDeclaredUnverified").length,
    [candidates],
  );
  const runtimeExact = payload.runtime?.exactSha === true;

  const refresh = () => queryClient.invalidateQueries({ queryKey: ["platform", "hardware-readiness"] });

  const createCandidate = async () => {
    setBusy(true); setError(null); setNotice(null);
    try {
      await platformApi.createDeviceCompatibilityCandidate(intake);
      setIntake(blankIntake);
      setIntakeOpen(false);
      setNotice("Exact device tuple registered against the current release. It remains on External Hold.");
      await refresh();
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Candidate could not be registered.");
    } finally {
      setBusy(false);
    }
  };

  const declareCapabilities = async () => {
    if (!declarationTarget) return;
    setBusy(true); setError(null); setNotice(null);
    try {
      await platformApi.declareDeviceCompatibilityCandidate(Number(declarationTarget.id), {
        protocolNames: commaList(declaration.protocolNames),
        supportedFields: commaList(declaration.supportedFields),
        supportedEvents: commaList(declaration.supportedEvents),
        supportedCommands: commaList(declaration.supportedCommands),
        knownLimitations: declaration.knownLimitations,
        declarationSourceReference: declaration.declarationSourceReference,
      });
      setDeclarationTarget(null);
      setDeclaration(blankDeclaration);
      setNotice("Engineering capability declaration frozen. Physical certification evidence is still required.");
      await refresh();
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Capabilities could not be declared.");
    } finally {
      setBusy(false);
    }
  };

  if (query.isLoading) return <PLoading />;
  if (query.error) return <PError message={(query.error as Error).message} />;

  return (
    <div className="space-y-6">
      <PHeader
        eyebrow="DeviceOps"
        title="Hardware readiness"
        description="Register an exact supplier model, hardware revision and firmware against the running release before the unit arrives. This prepares configuration and evidence collection; it cannot certify hardware."
        actions={canManage ? (
          <PButton onClick={() => { setError(null); setIntakeOpen(true); }} disabled={!runtimeExact}>
            <Plus className="h-4 w-4" /> Register device candidate
          </PButton>
        ) : undefined}
      />

      {!runtimeExact && (
        <div className="rounded-xl border border-amber-300 bg-amber-50 px-4 py-3 text-sm text-amber-800">
          Candidate registration is disabled because this runtime does not expose an exact release SHA.
        </div>
      )}
      {notice && <div role="status" className="rounded-xl border border-emerald-200 bg-emerald-50 px-4 py-3 text-sm text-emerald-700">{notice}</div>}
      {error && <div role="alert" className="rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">{error}</div>}

      <div className="grid gap-3 md:grid-cols-3">
        <PKpi label="Exact candidates" value={candidates.length} sub={`Release ${shortSha(payload.runtime?.sha)}`} />
        <PKpi label="Capabilities declared" value={declared} tone={declared > 0 ? "good" : "warn"} sub="Engineering declaration only" />
        <PKpi label="Certification state" value="External Hold" tone="warn" sub="Physical evidence cannot be entered here" />
      </div>

      <div className="grid gap-3 lg:grid-cols-2">
        {(payload.supportedPaths ?? []).map((path) => {
          const ready = String(path.state) === "SoftwareReadyForPhysicalConfirmation";
          return (
            <PCard key={String(path.key)} className="p-4">
              <div className="flex items-start gap-3">
                <span className={`mt-0.5 rounded-xl p-2 ${ready ? "bg-emerald-50 text-emerald-600" : "bg-amber-50 text-amber-600"}`}>
                  {ready ? <CheckCircle2 className="h-5 w-5" /> : <CircleAlert className="h-5 w-5" />}
                </span>
                <div className="min-w-0">
                  <div className="flex flex-wrap items-center gap-2">
                    <h2 className="font-bold text-slate-900">{String(path.label)}</h2>
                    <PBadge value={ready ? "software ready" : "external dependency"} />
                  </div>
                  <p className="mt-1 text-sm leading-6 text-slate-500">{String(path.next)}</p>
                </div>
              </div>
            </PCard>
          );
        })}
      </div>

      <PCard className="overflow-hidden">
        <div className="flex items-center gap-2 border-b border-slate-200 px-5 py-4">
          <Cable className="h-4 w-4 text-teal-600" />
          <h2 className="font-bold text-slate-900">Exact hardware and firmware candidates</h2>
        </div>
        {candidates.length === 0 ? (
          <PEmpty title="No exact candidates registered" subtitle="Register the supplier's exact tuple after they confirm the model, hardware revision and firmware version." />
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[900px] text-left text-sm">
              <thead className="bg-slate-50 text-[10px] uppercase tracking-wider text-slate-500">
                <tr>
                  <th className="px-4 py-3">Device</th><th className="px-4 py-3">Exact tuple</th><th className="px-4 py-3">Protocol</th>
                  <th className="px-4 py-3">Release</th><th className="px-4 py-3">State</th><th className="px-4 py-3">Next action</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {candidates.map((row) => {
                  const isDeclared = String(row.capabilityDeclarationStatus) === "EngineeringDeclaredUnverified";
                  return (
                    <tr key={String(row.id)} className="align-top">
                      <td className="px-4 py-3"><p className="font-semibold text-slate-900">{String(row.manufacturer)}</p><p className="text-xs text-slate-500">{String(row.deviceModel)}</p></td>
                      <td className="px-4 py-3 text-xs text-slate-600"><p>HW {String(row.hardwareRevision)}</p><p>FW {String(row.firmwareVersion)}</p></td>
                      <td className="px-4 py-3 text-xs text-slate-600">{stringList(row.protocolNames).join(", ") || "Unconfirmed"}</td>
                      <td className="px-4 py-3 font-mono text-xs text-slate-600" title={String(row.softwareCandidateSha)}>{shortSha(row.softwareCandidateSha)}</td>
                      <td className="px-4 py-3"><PBadge value="external hold" /><p className="mt-1 max-w-xs text-xs leading-5 text-slate-500">{String(row.externalHoldReason)}</p></td>
                      <td className="px-4 py-3">
                        {!isDeclared && canManage ? (
                          <PButton variant="ghost" onClick={() => { setError(null); setDeclaration(blankDeclaration); setDeclarationTarget(row); }}>Declare supported path</PButton>
                        ) : (
                          <span className="text-xs text-slate-500">{isDeclared ? "Await physical evidence" : "Declaration pending"}</span>
                        )}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </PCard>

      <PDrawer open={intakeOpen} onClose={() => setIntakeOpen(false)} title="Register exact device candidate">
        <div className="space-y-4">
          <p className="rounded-xl border border-amber-200 bg-amber-50 px-3 py-2 text-xs leading-5 text-amber-800">
            Use the supplier's exact labels. This freezes the tuple to release {shortSha(payload.runtime?.sha)} and records External Hold; it makes no certification claim.
          </p>
          <div className="grid gap-3 sm:grid-cols-2">
            <PField label="Manufacturer"><PInput value={intake.manufacturer} onChange={(event) => setIntake({ ...intake, manufacturer: event.target.value })} /></PField>
            <PField label="Model"><PInput value={intake.deviceModel} onChange={(event) => setIntake({ ...intake, deviceModel: event.target.value })} /></PField>
            <PField label="Hardware revision"><PInput value={intake.hardwareRevision} onChange={(event) => setIntake({ ...intake, hardwareRevision: event.target.value })} /></PField>
            <PField label="Firmware version"><PInput value={intake.firmwareVersion} onChange={(event) => setIntake({ ...intake, firmwareVersion: event.target.value })} /></PField>
          </div>
          <PField label="Why external evidence is pending">
            <textarea className="min-h-24 w-full rounded-[14px] border border-slate-300 bg-white px-3 py-2.5 text-sm text-slate-900 outline-none focus:border-teal-400 focus:ring-2 focus:ring-teal-400/15" value={intake.externalHoldReason} onChange={(event) => setIntake({ ...intake, externalHoldReason: event.target.value })} />
          </PField>
          {error && <p role="alert" className="rounded-xl border border-red-200 bg-red-50 px-3 py-2 text-xs text-red-700">{error}</p>}
          <div className="flex gap-2"><PButton onClick={createCandidate} disabled={busy || Object.values(intake).some((value) => !value.trim())}>{busy ? "Registering…" : "Register candidate"}</PButton><PButton variant="ghost" onClick={() => setIntakeOpen(false)}>Cancel</PButton></div>
        </div>
      </PDrawer>

      <PDrawer open={Boolean(declarationTarget)} onClose={() => setDeclarationTarget(null)} title="Declare software-supported path">
        <div className="space-y-4">
          <p className="rounded-xl border border-sky-200 bg-sky-50 px-3 py-2 text-xs leading-5 text-sky-800">
            Only GT06 and J1939 are currently software-ready. A supplier cloud or proprietary parser stays unconfirmed until its adapter is installed and tested.
          </p>
          <PField label="Protocol names (comma separated)"><PInput value={declaration.protocolNames} onChange={(event) => setDeclaration({ ...declaration, protocolNames: event.target.value })} /></PField>
          <PField label="Supported fields (comma separated)"><PInput value={declaration.supportedFields} onChange={(event) => setDeclaration({ ...declaration, supportedFields: event.target.value })} /></PField>
          <PField label="Supported events (comma separated)"><PInput value={declaration.supportedEvents} onChange={(event) => setDeclaration({ ...declaration, supportedEvents: event.target.value })} /></PField>
          <PField label="Supported commands (optional)"><PInput value={declaration.supportedCommands} onChange={(event) => setDeclaration({ ...declaration, supportedCommands: event.target.value })} /></PField>
          <PField label="Supplier specification or protocol reference"><PInput value={declaration.declarationSourceReference} onChange={(event) => setDeclaration({ ...declaration, declarationSourceReference: event.target.value })} placeholder="supplier://document/version or controlled evidence reference" /></PField>
          <PField label="Known limitations">
            <textarea className="min-h-24 w-full rounded-[14px] border border-slate-300 bg-white px-3 py-2.5 text-sm text-slate-900 outline-none focus:border-teal-400 focus:ring-2 focus:ring-teal-400/15" value={declaration.knownLimitations} onChange={(event) => setDeclaration({ ...declaration, knownLimitations: event.target.value })} />
          </PField>
          {error && <p role="alert" className="rounded-xl border border-red-200 bg-red-50 px-3 py-2 text-xs text-red-700">{error}</p>}
          <div className="flex gap-2"><PButton onClick={declareCapabilities} disabled={busy || !declaration.protocolNames.trim() || !declaration.supportedFields.trim() || !declaration.supportedEvents.trim() || !declaration.declarationSourceReference.trim() || !declaration.knownLimitations.trim()}>{busy ? "Freezing…" : "Freeze declaration"}</PButton><PButton variant="ghost" onClick={() => setDeclarationTarget(null)}>Cancel</PButton></div>
        </div>
      </PDrawer>
    </div>
  );
}
