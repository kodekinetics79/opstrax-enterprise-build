import { useMemo } from "react";
import { useQuery } from "@tanstack/react-query";
import { useNavigate } from "react-router";
import { Activity, AlertTriangle, CheckCircle2, Gauge, MapPinned, RadioTower, ShieldCheck, Wrench } from "lucide-react";
import { EmptyState, ErrorState, KpiCard, LoadingState, PageHeader, StatusBadge } from "@/components/ui";
import { telematicsService, type DeviceCommandRecord, type TelematicsClusterRecord } from "@/services/telematicsService";

type TowerData = {
  devices: DeviceCommandRecord[];
  gps: TelematicsClusterRecord[];
  diagnostics: TelematicsClusterRecord[];
};

type ExceptionRow = {
  id: string;
  device: string;
  vehicle: string;
  provider: string;
  status: string;
  reason: string;
  evidence: string;
  action: string;
  severity: number;
};

function matchSignal(device: DeviceCommandRecord, rows: TelematicsClusterRecord[]) {
  return rows.find((row) => String(row.deviceId) === String(device.id) || row.deviceId === device.deviceId || (device.vehicleId && row.vehicleId === device.vehicleId));
}

function exceptionFor(device: DeviceCommandRecord, gps: TelematicsClusterRecord[], diagnostics: TelematicsClusterRecord[]): ExceptionRow | null {
  const gpsSignal = matchSignal(device, gps);
  const engineSignal = matchSignal(device, diagnostics);
  const neverConnected = !device.lastCheckIn || device.lastCheckIn === "—";
  const faults = engineSignal?.troubleCodes ?? [];

  if (neverConnected) return {
    id: String(device.id), device: device.deviceName, vehicle: device.assignedVehicleCode || "Unassigned", provider: device.provider,
    status: "Never connected", reason: "No device check-in has been observed", evidence: "last check-in: none",
    action: "Verify installation, power, SIM, and activation", severity: 4,
  };
  if (/offline/i.test(device.connectionStatus) || gpsSignal?.offlineWarning) return {
    id: String(device.id), device: device.deviceName, vehicle: device.assignedVehicleCode || "Unassigned", provider: device.provider,
    status: "Offline", reason: "The latest trusted signal is outside the freshness window",
    evidence: `check-in: ${device.lastCheckIn}${gpsSignal ? ` · GPS: ${gpsSignal.dataFreshnessStatus}` : ""}`,
    action: "Check vehicle power, cellular coverage, and device cable", severity: 3,
  };
  if (device.assignedVehicleCode && !gpsSignal?.positionAvailable) return {
    id: String(device.id), device: device.deviceName, vehicle: device.assignedVehicleCode, provider: device.provider,
    status: "No valid fix", reason: "The assigned vehicle has no valid latitude/longitude in the current position feed",
    evidence: `source: ${gpsSignal?.positionSource ?? "Unknown source"} · fix time: ${gpsSignal?.deviceFixAt ?? "none"}`,
    action: "Verify GNSS reception and ingest mapping before using this unit for location decisions", severity: 3,
  };
  if (faults.length > 0) return {
    id: String(device.id), device: device.deviceName, vehicle: device.assignedVehicleCode || "Unassigned", provider: device.provider,
    status: "Engine fault", reason: `${faults.length} active diagnostic code${faults.length === 1 ? "" : "s"}`,
    evidence: faults.slice(0, 4).join(", "), action: engineSignal?.recommendedAction || "Review diagnostic evidence and maintenance impact", severity: 3,
  };
  if (/attention|malfunction|quarantined|rotation/i.test(`${device.connectionStatus} ${device.lifecycleStatus}`) || device.openAlertCount > 0) return {
    id: String(device.id), device: device.deviceName, vehicle: device.assignedVehicleCode || "Unassigned", provider: device.provider,
    status: "Needs attention", reason: device.openAlertCount ? `${device.openAlertCount} open telemetry alert${device.openAlertCount === 1 ? "" : "s"}` : device.lifecycleStatus,
    evidence: `health: ${device.dataHealthAvailable ? `${device.dataHealthScore}/100` : "unknown"} · lifecycle: ${device.lifecycleStatus}`,
    action: "Open Device Health and resolve the evidence-backed issue", severity: 2,
  };
  return null;
}

export function TelematicsControlTowerPage() {
  const navigate = useNavigate();
  const query = useQuery<TowerData>({
    queryKey: ["telematics-control-tower"],
    queryFn: async () => {
      const [devices, gps, diagnostics] = await Promise.all([
        telematicsService.getDevices(), telematicsService.getGpsTrackingRecords(), telematicsService.getDiagnosticsRecords(),
      ]);
      return { devices, gps, diagnostics };
    },
    refetchInterval: 30_000,
  });

  const exceptions = useMemo(() => (query.data?.devices ?? [])
    .map((device) => exceptionFor(device, query.data?.gps ?? [], query.data?.diagnostics ?? []))
    .filter((row): row is ExceptionRow => Boolean(row))
    .sort((a, b) => b.severity - a.severity || a.device.localeCompare(b.device)), [query.data]);

  if (query.isLoading) return <LoadingState />;
  if (query.isError) return <ErrorState message={query.error instanceof Error ? query.error.message : "Unable to load device signals."} onRetry={() => { void query.refetch(); }} />;

  const devices = query.data?.devices ?? [];
  const online = devices.filter((device) => /online/i.test(device.connectionStatus)).length;
  const neverConnected = devices.filter((device) => !device.lastCheckIn || device.lastCheckIn === "—").length;
  const faulted = query.data?.diagnostics.filter((row) => row.troubleCodes.length > 0).length ?? 0;
  const gps = query.data?.gps ?? [];
  const denominator = devices.length;
  const scorecard = [
    { label: "Check-in evidence", count: devices.filter((device) => Boolean(device.lastCheckIn && device.lastCheckIn !== "—")).length, definition: "Device has reported at least one check-in." },
    { label: "Current position", count: gps.filter((row) => row.positionAvailable && row.dataFreshnessStatus === "Fresh").length, definition: "Valid coordinates and server freshness is Fresh." },
    { label: "Known provenance", count: gps.filter((row) => row.positionAvailable && row.positionSource !== "Unknown source").length, definition: "Current/last position identifies its source." },
    { label: "Health evidence", count: devices.filter((device) => device.dataHealthAvailable).length, definition: "Health score has check-in, alert, fault, or lifecycle evidence." },
  ];

  return (
    <div className="space-y-5">
      <PageHeader title="Telematics Control Tower" description="Exception-first DeviceOps: connectivity, GPS freshness, diagnostic evidence, and the next operator action from real signals." eyebrow="Telematics & IoT" />

      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-5">
        <KpiCard label="Managed devices" value={devices.length} icon={<RadioTower className="h-4 w-4" />} />
        <KpiCard label="Online" value={online} icon={<CheckCircle2 className="h-4 w-4" />} />
        <KpiCard label="Needs action" value={exceptions.length} icon={<AlertTriangle className="h-4 w-4" />} />
        <KpiCard label="Never connected" value={neverConnected} icon={<Activity className="h-4 w-4" />} />
        <KpiCard label="Faulted assets" value={faulted} icon={<Gauge className="h-4 w-4" />} />
      </div>

      <section className="rounded-xl border border-slate-200 bg-white p-4 shadow-sm">
        <div>
          <h2 className="font-semibold text-slate-900">Pilot evidence scorecard</h2>
          <p className="mt-1 text-sm text-slate-500">Coverage counts from current API fields. “Unknown” is shown when there are no managed devices; these are evidence ratios, not a synthetic readiness score.</p>
        </div>
        <div className="mt-4 grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
          {scorecard.map((item) => (
            <div key={item.label} className="rounded-xl border border-slate-200 bg-slate-50 p-4">
              <p className="text-xs font-semibold uppercase tracking-wide text-slate-500">{item.label}</p>
              <p className="mt-2 text-2xl font-bold text-slate-900">{denominator ? `${item.count}/${denominator}` : "Unknown"}</p>
              <p className="mt-2 text-xs text-slate-500">{item.definition}</p>
            </div>
          ))}
        </div>
      </section>

      <section className="rounded-xl border border-slate-200 bg-white p-4 shadow-sm">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <h2 className="font-semibold text-slate-900">Priority action queue</h2>
            <p className="mt-1 text-sm text-slate-500">Ranked by observed risk. Unknown signals stay unknown; the page does not manufacture modem, power, GPS, or engine readings.</p>
          </div>
          <div className="flex flex-wrap gap-2">
            <button className="btn-ghost" onClick={() => navigate("/gps-tracking")}><MapPinned className="h-4 w-4" /> GPS</button>
            <button className="btn-ghost" onClick={() => navigate("/obd-j1939")}><Gauge className="h-4 w-4" /> Diagnostics</button>
            <button className="btn-primary" onClick={() => navigate("/iot-devices")}><Wrench className="h-4 w-4" /> Device Health</button>
          </div>
        </div>

        {exceptions.length === 0 ? (
          <div className="mt-4"><EmptyState title="No device exceptions" subtitle="All currently observed device, GPS, and diagnostic signals are within their available health rules." /></div>
        ) : (
          <div className="mt-4 overflow-x-auto">
            <table className="min-w-full text-left text-sm">
              <thead className="border-b border-slate-200 text-xs uppercase tracking-wide text-slate-500"><tr>
                {['Device','Vehicle','Provider','State','Why it is here','Evidence','Next action'].map((header) => <th key={header} className="px-3 py-2 font-semibold">{header}</th>)}
              </tr></thead>
              <tbody className="divide-y divide-slate-100">{exceptions.map((row) => <tr key={row.id} className="align-top hover:bg-slate-50">
                <td className="px-3 py-3 font-semibold text-slate-900">{row.device}</td><td className="px-3 py-3">{row.vehicle}</td><td className="px-3 py-3">{row.provider}</td>
                <td className="px-3 py-3"><StatusBadge status={row.status} /></td><td className="max-w-xs px-3 py-3">{row.reason}</td>
                <td className="max-w-xs px-3 py-3 text-slate-600">{row.evidence}</td><td className="max-w-xs px-3 py-3 font-medium text-slate-800">{row.action}</td>
              </tr>)}</tbody>
            </table>
          </div>
        )}
      </section>

      <section className="grid gap-3 md:grid-cols-3">
        <button className="rounded-xl border border-slate-200 bg-white p-4 text-left hover:border-teal-400" onClick={() => navigate('/iot-devices')}><ShieldCheck className="h-5 w-5 text-teal-700" /><h3 className="mt-3 font-semibold">Trust & lifecycle</h3><p className="mt-1 text-sm text-slate-500">Provision, assign, rotate credentials, and inspect evidence-backed health.</p></button>
        <button className="rounded-xl border border-slate-200 bg-white p-4 text-left hover:border-teal-400" onClick={() => navigate('/gps-tracking')}><MapPinned className="h-5 w-5 text-teal-700" /><h3 className="mt-3 font-semibold">Location truth</h3><p className="mt-1 text-sm text-slate-500">Separate device fix time, ingress time, freshness, and operational position.</p></button>
        <button className="rounded-xl border border-slate-200 bg-white p-4 text-left hover:border-teal-400" onClick={() => navigate('/obd-j1939')}><Gauge className="h-5 w-5 text-teal-700" /><h3 className="mt-3 font-semibold">Vehicle intelligence</h3><p className="mt-1 text-sm text-slate-500">Review immutable OBD/J1939 evidence before maintenance or safety action.</p></button>
      </section>
    </div>
  );
}
