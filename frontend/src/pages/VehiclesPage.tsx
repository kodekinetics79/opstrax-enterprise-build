import { deviceWorkspaceRoute, measuredNumber, observationAge, observedMotion, recentObservation } from "@/utils/vehicleWorkspace";
import { useDialogFocus } from "@/hooks/useDialogFocus";
import { telematicsService } from "@/services/telematicsService";
import { FormEvent, useEffect, useMemo, useRef, useState, useSyncExternalStore } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  Activity, ArchiveRestore, Boxes, Camera, ChevronRight, Cpu, Download, Gauge, Info,
  MapPin, Navigation, Plus, Save, Search, ShieldAlert, Sparkles, Trash2,
  UserCheck, Video, Wrench, X, Radio, RefreshCw,
} from "lucide-react";
import { useNavigate, useSearchParams } from "react-router";
import { vehiclesApi } from "@/services/vehiclesApi";
import { driversApi } from "@/services/driversApi";
import { downloadServerExport } from "@/services/fleetDomainApi";
import { PERMISSIONS, useHasPermission } from "@/hooks/usePermission";
import { useAuth } from "@/hooks/useAuth";
import { scopeRowsForSession } from "@/auth/accessScope";
import { apiErrorMessage } from "@/utils/apiErrorMessage";
import { resolveAuthorizedSummaryCount } from "@/utils/vehicleSummaryPresentation";
import { optionsWithPersistedValue, VEHICLE_TYPE_OPTIONS } from "@/utils/vehicleEditorOptions";
import { labelize, LoadingState, ErrorState, EmptyState, PageHeader, PageStack } from "@/components/ui";
import { ConfirmDialog } from "@/components/ConfirmDialog";
import type { AnyRecord, UserSession } from "@/types";
import { optionalTelemetryHeading, readSpeedMph, telemetrySpeedSummary } from "@/utils/telemetryMeasurements";

/* ------------------------------------------------------------------ helpers */

const g = (row: AnyRecord, ...keys: string[]) => {
  for (const k of keys) if (row?.[k] != null && row[k] !== "") return row[k];
  return undefined;
};
const num = (v: unknown) => (Number.isFinite(Number(v)) ? Number(v) : 0);

function riskTier(row: AnyRecord): "High" | "Medium" | "Low" {
  const heat = String(g(row, "riskHeatScore", "risk_heat_score") ?? "");
  if (/high|critical/i.test(heat)) return "High";
  if (/medium|warning/i.test(heat)) return "Medium";
  if (/low/i.test(heat)) return "Low";
  const n = num(g(row, "riskScore", "risk_score"));
  return n >= 70 ? "High" : n >= 40 ? "Medium" : "Low";
}

// Live movement is derived from real telemetry only. speedMph/lastSeenAt come from the
// location_events join; where they are absent we say so honestly (no fabricated motion).
function isMoving(row: AnyRecord): boolean {
  return observedMotion(readSpeedMph(row), g(row, "lastSeenAt", "last_seen_at")) === "Moving";
}

function vehicleDeviceStatus(row: AnyRecord): string {
  const deviceId = g(row, "currentDeviceId", "current_device_id", "installedDeviceId", "installed_device_id");
  if (deviceId == null) return "Unknown";
  const lastSeen = g(row, "deviceLastSeenAt", "device_last_seen_at", "currentDeviceLastSeenAt", "current_device_last_seen_at");
  if (lastSeen == null) return "Disconnected";
  const reported = String(g(row, "currentDeviceStatus", "current_device_status", "deviceConnectionStatus", "device_connection_status") ?? "");
  if (/revoked|suspend|offline|malfunction|fault/i.test(reported)) return reported || "Disconnected";
  return hasRecentHeartbeat(lastSeen) ? "Online" : "Disconnected";
}

function vehicleCameraStatus(row: AnyRecord): string {
  const cameraId = g(row, "currentCameraId", "current_camera_id", "installedCameraId", "installed_camera_id");
  if (cameraId == null) return "Unknown";
  const lastSeen = g(row, "cameraLastSeenAt", "camera_last_seen_at", "currentCameraLastSeenAt", "current_camera_last_seen_at");
  if (lastSeen == null) return "Disconnected";
  const reported = String(g(row, "currentCameraStatus", "current_camera_status") ?? "");
  if (/revoked|suspend|offline|fault|not recording/i.test(reported)) return reported || "Disconnected";
  return hasRecentHeartbeat(lastSeen) ? "Recording" : "Disconnected";
}

function hasRecentHeartbeat(iso: unknown): boolean {
  return recentObservation(iso, Date.now(), 15 * 60_000);
}

function hasReadinessEvidence(row: AnyRecord): boolean {
  return [vehicleDeviceStatus(row), vehicleCameraStatus(row)]
    .some((status) => status != null && !/^(unknown|unavailable|--)$/i.test(String(status).trim()));
}

function vehicleReadiness(row: AnyRecord): number | null {
  if (!hasReadinessEvidence(row)) return null;
  const value = Number(g(row, "fleetReadinessScore", "fleet_readiness_score", "readinessScore", "readiness_score"));
  return Number.isFinite(value) ? value : null;
}

function freshness(iso?: unknown): { label: string; live: boolean } | null {
  if (iso == null || iso === "") return null;
  const age = observationAge(iso);
  if (age == null) return null;
  const mins = Math.floor(age / 60_000);
  if (mins < 3) return { label: "live", live: true };
  if (mins < 60) return { label: `${mins}m ago`, live: false };
  const hours = Math.round(mins / 60);
  if (hours < 48) return { label: `${hours}h ago`, live: false };
  return { label: `${Math.round(hours / 24)}d ago`, live: false };
}

type VehicleField = {
  key: string;
  label: string;
  required?: boolean;
  type?: "text" | "number" | "select" | "email";
  options?: readonly string[];
};

const FIELDS: VehicleField[] = [
  { key: "vehicleCode", label: "Vehicle code", required: true },
  { key: "type", label: "Type", type: "select", options: VEHICLE_TYPE_OPTIONS, required: true },
  { key: "make", label: "Make" },
  { key: "model", label: "Model" },
  { key: "year", label: "Year", type: "number" },
  { key: "odometerMiles", label: "Odometer (mi)", type: "number" },
  { key: "vin", label: "VIN" },
  { key: "vinExceptionType", label: "Alternate identity kind", type: "select", options: ["manufacturer-serial-number", "government-registration-number", "legacy-fleet-identifier"] },
  { key: "alternateIdentifier", label: "Alternate identifier" },
  { key: "plateNumber", label: "Plate number" },
  { key: "plateJurisdiction", label: "Plate jurisdiction" },
  { key: "vehicleClass", label: "Vehicle class", type: "select", options: ["Class 1", "Class 2", "Class 3", "Class 4", "Class 5", "Class 6", "Class 7", "Class 8"] },
  { key: "status", label: "Status", type: "select", options: ["Available", "On Route", "At Stop", "Idle", "Delayed", "Maintenance"] },
] as const;

const FILTERS = ["All", "Moving", "Available", "On Route", "Maintenance", "At risk", "Archived"] as const;

// An uncertain write survives SPA navigation/remounts in this document. Only a
// full reload clears it; this is not cross-tab exclusion or server idempotency.
let vehicleLifecycleRefreshRequired = false;
let vehicleLifecyclePending = false;
const vehicleLifecycleListeners = new Set<() => void>();
const subscribeVehicleLifecycle = (listener: () => void) => {
  vehicleLifecycleListeners.add(listener);
  return () => { vehicleLifecycleListeners.delete(listener); };
};
// Primitive snapshot is stable between transitions; no identity or credentials.
const vehicleLifecycleSnapshot = () => (vehicleLifecyclePending ? 1 : 0) | (vehicleLifecycleRefreshRequired ? 2 : 0);
const notifyVehicleLifecycle = () => { vehicleLifecycleListeners.forEach((listener) => listener()); };

type VehicleArchiveTarget = Readonly<{
  id: string;
  code: string;
  companyId: string;
  session: UserSession;
  opener: HTMLElement | null;
}>;

/* ------------------------------------------------------------------ page */

export function VehiclesPage({ embedded = false }: { embedded?: boolean }) {
  const navigate = useNavigate();
  const { session } = useAuth();
  const hasPermission = useHasPermission();
  const queryClient = useQueryClient();

  const canManageFleet = hasPermission(PERMISSIONS.FLEET_MANAGE);
  const canCreate = canManageFleet;
  const canUpdate = canManageFleet;
  const canDelete = canManageFleet;
  const canAssign = canManageFleet;
  const canExport = hasPermission("vehicles:export");
  const canViewCameraEvidence = hasPermission(PERMISSIONS.SAFETY_EVIDENCE_VIEW);

  const [search, setSearch] = useState("");
  const [filter, setFilter] = useState<(typeof FILTERS)[number]>("All");
  const [selectedId, setSelectedId] = useState<string | number | null>(null);
  const [archiveTarget, setArchiveTarget] = useState<VehicleArchiveTarget | null>(null);
  const [lifecycleGuardError, setLifecycleGuardError] = useState<string | null>(() => vehicleLifecycleRefreshRequired
    ? "A previous vehicle lifecycle outcome could not be confirmed. Reload the vehicle status before another lifecycle action."
    : null);
  const [localLifecycleNeedsRefresh, setLifecycleNeedsRefresh] = useState(vehicleLifecycleRefreshRequired);
  const lifecycleDocumentState = useSyncExternalStore(subscribeVehicleLifecycle, vehicleLifecycleSnapshot, vehicleLifecycleSnapshot);
  const lifecycleNeedsRefresh = localLifecycleNeedsRefresh || (lifecycleDocumentState & 2) !== 0;
  const lifecycleRecoveryError = lifecycleGuardError ?? (lifecycleNeedsRefresh
    ? "A previous vehicle lifecycle outcome could not be confirmed. Reload the vehicle status before another lifecycle action."
    : null);
  // React state alone cannot stop two activations before the next render.
  const lifecycleInFlight = useRef(false);
  const lifecycleRefreshRequired = useRef(vehicleLifecycleRefreshRequired);
  const [assignmentVehicle, setAssignmentVehicle] = useState<AnyRecord | null>(null);
  const [editing, setEditing] = useState<AnyRecord | null>(null);
  const [isCreating, setIsCreating] = useState(false);
  // Single-add lives here on the roster, but users look for it on the module
  // Overview, which only showed the bulk Template/Import/Export actions. The
  // Overview button deep-links to ?new=1 so "add one vehicle" is reachable from
  // where people actually look, without duplicating the form.
  const [searchParams, setSearchParams] = useSearchParams();
  const [exportError, setExportError] = useState<string | null>(null);
  const [offset, setOffset] = useState(0);
  const PAGE_SIZE = 50;
  const archivedView = filter === "Archived";

  // Server-side paginated + searched — never fetches the full fleet. The search term
  // is applied server-side (so a 1000-vehicle fleet is searchable); the status filter
  // below operates on the returned page. Live telemetry columns refresh on an interval.
  const list = useQuery({
    queryKey: ["vehicles", "paged", archivedView ? "archived" : "active", search.trim(), offset],
    queryFn: () => vehiclesApi.listPaged({ limit: PAGE_SIZE, offset, search, lifecycle: archivedView ? "archived" : "active" }),
    refetchInterval: 30_000,
  });
  const pagedRows = (list.data?.rows ?? []) as AnyRecord[];
  const totalRows = list.data?.total ?? pagedRows.length;
  const summary = useQuery({ queryKey: ["vehicles", "summary"], queryFn: vehiclesApi.summary, refetchInterval: 30_000 });
  const detail = useQuery({
    queryKey: ["vehicles", "detail", selectedId, archivedView ? "archived" : "active"],
    queryFn: () => vehiclesApi.detail(String(selectedId), archivedView ? "archived" : "active"),
    enabled: selectedId != null,
    refetchInterval: selectedId != null ? 20_000 : false,
  });
  const drivers = useQuery({ queryKey: ["drivers", "assign-pool", 2000], queryFn: () => driversApi.listPaged({ limit: 2000 }).then((result) => result.rows), enabled: canAssign });
  const rows = useMemo(() => {
    return scopeRowsForSession("vehicles", pagedRows, session);
  }, [pagedRows, session]);

  // Status filter operates on the current server page (search is already applied
  // server-side across the whole fleet).
  const filtered = useMemo(() => {
    return rows.filter((row) => {
      const status = String(g(row, "status") ?? "");
      return filter === "Archived" ? true : filter === "All" ? true :
        filter === "Moving" ? isMoving(row) :
        filter === "At risk" ? (riskTier(row) === "High" || /maintenance|delayed/i.test(status)) :
        status.toLowerCase().includes(filter.toLowerCase());
    });
  }, [rows, filter]);

  const sum = (summary.data as AnyRecord) || {};
  const readinessRows = rows.filter(hasReadinessEvidence);
  const evidencedScores = readinessRows.map(vehicleReadiness).filter((score): score is number => score != null);
  const readiness = evidencedScores.length === 0
    ? null
    : Math.round(evidencedScores.reduce((total, score) => total + score, 0) / Math.max(evidencedScores.length, 1));
  const available = rows.filter((r) => /available/i.test(String(g(r, "status")))).length;
  const moving = rows.filter(isMoving).length;
  const atRisk = resolveAuthorizedSummaryCount(summary.isSuccess, g(sum, "atRisk", "at_risk"));
  const deviceEx = resolveAuthorizedSummaryCount(summary.isSuccess, g(sum, "deviceExceptions", "device_exceptions"));
  const pageScopeSummary = `${rows.length} on this page · ${moving} moving on page · ${available} available on page · ${atRisk == null ? "attention unavailable in authorized scope" : `${atRisk} need attention in authorized scope`}`;

  useEffect(() => {
    if (searchParams.get("new") !== "1") return;
    const next = new URLSearchParams(searchParams);
    next.delete("new");
    setSearchParams(next, { replace: true });
    if (!canCreate) return;
    setIsCreating(true);
    setEditing({ type: "Truck", status: "Available" });
  }, [searchParams, setSearchParams, canCreate]);

  const save = useMutation({
    mutationFn: (p: AnyRecord) => (p.id && !isCreating ? vehiclesApi.update(String(p.id), p) : vehiclesApi.create(p)),
    onSuccess: async () => { setEditing(null); setIsCreating(false); await queryClient.invalidateQueries({ queryKey: ["vehicles"] }); },
  });
  const remove = useMutation({
    mutationFn: (id: string | number) => vehiclesApi.archive(id),
    retry: false,
    onSuccess: async () => {
      setArchiveTarget(null); setLifecycleGuardError(null); setSelectedId(null);
      await queryClient.invalidateQueries({ queryKey: ["vehicles"] });
    },
    onError: (error) => {
      vehicleLifecycleRefreshRequired = true;
      notifyVehicleLifecycle();
      lifecycleRefreshRequired.current = true;
      setLifecycleNeedsRefresh(true);
      setLifecycleGuardError(`${apiErrorMessage(error, "The archive outcome could not be confirmed.")} Cancel this confirmation, then reload the vehicle status before another lifecycle action.`);
    },
    onSettled: () => {
      lifecycleInFlight.current = false; vehicleLifecyclePending = false; notifyVehicleLifecycle();
    },
  });
  const reactivate = useMutation({
    mutationFn: (id: string | number) => vehiclesApi.reactivate(id),
    retry: false,
    onSuccess: async () => {
      setLifecycleGuardError(null); setSelectedId(null);
      await queryClient.invalidateQueries({ queryKey: ["vehicles"] });
    },
    onError: (error) => {
      vehicleLifecycleRefreshRequired = true;
      notifyVehicleLifecycle();
      lifecycleRefreshRequired.current = true;
      setLifecycleNeedsRefresh(true);
      setLifecycleGuardError(`${apiErrorMessage(error, "The reactivation outcome could not be confirmed.")} Reload the vehicle status before another lifecycle action.`);
    },
    onSettled: () => {
      lifecycleInFlight.current = false; vehicleLifecyclePending = false; notifyVehicleLifecycle();
    },
  });
  const assign = useMutation({
    mutationFn: ({ vehicleId, driverId }: { vehicleId: string | number; driverId: string | number }) =>
      vehiclesApi.assignDriver(String(vehicleId), String(driverId)),
    onSuccess: async () => {
      setAssignmentVehicle(null);
      await queryClient.invalidateQueries({ queryKey: ["vehicles"] });
      await queryClient.invalidateQueries({ queryKey: ["drivers"] });
      if (selectedId != null) await queryClient.invalidateQueries({ queryKey: ["vehicles", "detail", selectedId] });
    },
  });
  const actionError = save.error ?? remove.error ?? reactivate.error ?? assign.error;
  const lifecycleBusy = (lifecycleDocumentState & 1) !== 0 || remove.isPending || reactivate.isPending;

  const detailEnvelope = detail.data as AnyRecord | undefined;
  const authoritativeRecord = detailEnvelope?.record as AnyRecord | undefined;
  const observation = detailEnvelope?.latestObservation as AnyRecord | undefined;
  const selectedDetailRecord = useMemo(() => authoritativeRecord && observation ? { ...authoritativeRecord,
    lat: observation.lat, lng: observation.lng, speedMph: observation.speedMph, heading: observation.heading,
    lastSeenAt: observation.eventTime, observationSource: observation.source,
  } : authoritativeRecord, [authoritativeRecord, observation]);

  // A background list error must not detach an open confirmation or pending outcome.
  if (list.isLoading && !archiveTarget && !lifecycleBusy && !lifecycleNeedsRefresh) return <LoadingState />;
  if (list.isError && !archiveTarget && !lifecycleBusy && !lifecycleNeedsRefresh) return <ErrorState message={list.error instanceof Error ? list.error.message : "Unable to load vehicles."} />;

  const selectedRecord = selectedDetailRecord
    ? selectedDetailRecord
    : rows.find((r) => String(r.id) === String(selectedId)) || null;
  const lifecycleSelectionPreparing = selectedId != null && (detail.isLoading || detail.isFetching);
  const lifecycleSelectionUnavailable = selectedId != null && !lifecycleSelectionPreparing
    && (detail.isError || !selectedDetailRecord);
  const lifecycleStatusError = lifecycleSelectionUnavailable
    ? apiErrorMessage(detail.error, "Authoritative vehicle status is unavailable. Retry before a lifecycle action.")
    : null;

  const lifecycleSelectionValid = (archived: boolean) => {
    if (!session || !selectedRecord || !selectedDetailRecord || selectedRecord !== selectedDetailRecord
      || String(selectedDetailRecord.id) !== String(selectedId) || detail.isLoading || detail.isFetching) return false;
    const companyId = String(session?.company?.id ?? "");
    const id = String(selectedRecord?.id ?? "");
    const code = g(selectedRecord, "vehicleCode", "vehicle_code");
    const deletedAt = selectedRecord && ("deletedAt" in selectedRecord ? selectedRecord.deletedAt : selectedRecord.deleted_at);
    return /^[1-9][0-9]*$/.test(companyId) && /^[1-9][0-9]*$/.test(id)
      && selectedId != null && id === String(selectedId)
      && String(g(selectedRecord, "companyId", "company_id") ?? "") === companyId
      && typeof code === "string" && code.trim().length > 0
      && archivedView === archived && !detail.isError
      && (archived ? typeof deletedAt === "string" && Number.isFinite(Date.parse(deletedAt)) : deletedAt === null);
  };
  const openArchive = () => {
    if (vehicleLifecycleRefreshRequired || vehicleLifecyclePending || lifecycleRefreshRequired.current || lifecycleInFlight.current || lifecycleBusy || archiveTarget || save.isPending || assign.isPending) return;
    if (!canDelete || !lifecycleSelectionValid(false) || !session || !selectedRecord) {
      setLifecycleGuardError("The vehicle or your access has changed. Close this view and reopen the vehicle before archiving.");
      return;
    }
    remove.reset(); reactivate.reset(); setLifecycleGuardError(null);
    setArchiveTarget(Object.freeze({
      id: String(selectedRecord.id), code: String(g(selectedRecord, "vehicleCode", "vehicle_code")),
      companyId: String(session.company.id), session,
      opener: document.activeElement instanceof HTMLElement ? document.activeElement : null,
    }));
  };
  const cancelArchive = () => {
    if (vehicleLifecyclePending || lifecycleInFlight.current || lifecycleBusy) return;
    remove.reset();
    if (!lifecycleNeedsRefresh) setLifecycleGuardError(null);
    setArchiveTarget(null);
  };
  const confirmArchive = () => {
    if (vehicleLifecycleRefreshRequired || vehicleLifecyclePending || lifecycleRefreshRequired.current || lifecycleInFlight.current || lifecycleBusy || !archiveTarget || save.isPending || assign.isPending) return;
    if (!selectedRecord || !canDelete || !lifecycleSelectionValid(false) || session !== archiveTarget.session
      || String(session?.company?.id ?? "") !== archiveTarget.companyId
      || String(selectedId) !== archiveTarget.id
      || String(g(selectedRecord, "vehicleCode", "vehicle_code")) !== archiveTarget.code) {
      setLifecycleGuardError("The vehicle or your access has changed. Cancel and reopen the vehicle before archiving.");
      return;
    }
    setLifecycleGuardError(null);
    lifecycleInFlight.current = true;
    vehicleLifecyclePending = true; notifyVehicleLifecycle();
    remove.mutate(archiveTarget.id);
  };
  const reactivateSelected = () => {
    if (vehicleLifecycleRefreshRequired || vehicleLifecyclePending || lifecycleRefreshRequired.current || lifecycleInFlight.current || lifecycleBusy || archiveTarget || save.isPending || assign.isPending) return;
    if (!canUpdate || !lifecycleSelectionValid(true) || !selectedRecord) {
      setLifecycleGuardError("The vehicle or your access has changed. Close this view and reopen the archived vehicle before reactivating.");
      return;
    }
    remove.reset(); reactivate.reset(); setLifecycleGuardError(null);
    lifecycleInFlight.current = true;
    vehicleLifecyclePending = true; notifyVehicleLifecycle();
    reactivate.mutate(String(selectedRecord.id));
  };
  const closeVehicle = () => {
    if (archiveTarget || vehicleLifecyclePending || lifecycleInFlight.current || lifecycleBusy) return;
    if (!lifecycleNeedsRefresh) setLifecycleGuardError(null);
    remove.reset(); reactivate.reset(); setSelectedId(null);
  };
  const reloadLifecycleStatus = () => {
    if ((vehicleLifecycleRefreshRequired || lifecycleRefreshRequired.current) && !vehicleLifecyclePending && !lifecycleInFlight.current && !lifecycleBusy) window.location.reload();
  };

  return (
    <PageStack className={`fleet-console min-h-0 ${embedded ? "flex-1" : "h-full"}`}>

      {!embedded && <PageHeader
        eyebrow="Fleet · Master Data"
        title="Vehicles"
        description={pageScopeSummary}
        actions={
          <>
            <span className="inline-flex items-center gap-1.5 text-[11px] font-semibold text-slate-500">
              <Activity className="h-3 w-3 text-teal-600" /> Registry · 30 s
            </span>
            <button type="button" disabled={!canExport}
              onClick={() => { if (!canExport) return; setExportError(null); void downloadServerExport("/api/vehicles/export", `vehicles_${new Date().toISOString().slice(0, 10)}.csv`).catch((error: unknown) => setExportError(error instanceof Error ? error.message : "Full fleet export failed.")); }}
              title="Export the full fleet (all pages)" className="btn-ghost min-h-11 sm:min-h-8">
              <Download className="h-4 w-4" /> Export
            </button>
            {canCreate ? (
              <button type="button" onClick={() => { setIsCreating(true); setEditing({ type: "Truck", status: "Available" }); }} className="btn-primary min-h-11 sm:min-h-8">
                <Plus className="h-4 w-4" /> New vehicle
              </button>
            ) : null}
          </>
        }
      />}

      {actionError instanceof Error ? (
        <div role="alert" className="shrink-0 rounded-xl border border-rose-200 bg-rose-50 px-4 py-3 text-sm font-semibold text-rose-700">
          {apiErrorMessage(actionError, "The vehicle action could not be completed.")}
        </div>
      ) : null}
      {lifecycleNeedsRefresh && !selectedRecord && !archiveTarget ? (
        <div role="alert" className="shrink-0 rounded-xl border border-rose-200 bg-rose-50 p-3 text-sm text-rose-700">
          <p>{lifecycleRecoveryError}</p>
          <button type="button" disabled={lifecycleBusy} className="btn-ghost mt-2" onClick={reloadLifecycleStatus}>Reload vehicle status</button>
        </div>
      ) : null}
      {exportError ? <div role="alert" className="shrink-0 rounded-xl border border-rose-200 bg-rose-50 px-4 py-3 text-sm font-semibold text-rose-700">{exportError} No partial-page fallback was downloaded.</div> : null}

      {/* Compact operating summary keeps the roster in the first viewport. */}
      <dl className="vehicle-summary-strip panel grid shrink-0 grid-cols-2 gap-1 p-1 xl:grid-cols-4" aria-label="Vehicle operating summary">
        <ClayStat Icon={Gauge}      tone="fc-clay-teal"    iconCls="text-teal-700"    label="Page readiness"      value={readiness == null ? "Unknown" : `${readiness}%`} meter={readiness ?? undefined} caption={`${readinessRows.length} assessed on this page · ${rows.length - readinessRows.length} unknown`} />
        <ClayStat Icon={Navigation} tone="fc-clay-emerald" iconCls="text-emerald-700" label="Page moving"      value={moving}          meter={rows.length ? (moving / rows.length) * 100 : 0} caption={`${available} available on this page`} />
        <ClayStat Icon={ShieldAlert} tone="fc-clay-red"    iconCls="text-rose-700"    label="Scope at risk" value={atRisk == null ? "Unknown" : atRisk} alert={atRisk != null && atRisk > 0} caption="Tenant or permitted branch summary" />
        <ClayStat Icon={Cpu}        tone="fc-clay-amber"   iconCls="text-amber-700"   label="Scope device / camera gaps" value={deviceEx == null ? "Unknown" : deviceEx} alert={deviceEx != null && deviceEx > 0} caption="Tenant or permitted branch summary" />
      </dl>

      {/* The roster is the primary task surface. */}
      <section className="vehicle-roster-list panel flex min-h-[280px] flex-1 flex-col overflow-hidden sm:min-h-0">
        <div className="flex shrink-0 flex-wrap items-center gap-2 border-b border-slate-200 px-3 py-2">
          <div className="relative w-full sm:max-w-xs">
            <Search className="pointer-events-none absolute left-3.5 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
            <input value={search} onChange={(e) => { setSearch(e.target.value); setOffset(0); }}
              aria-label="Search vehicles"
              placeholder="Search code, make, plate, driver…"
              className="fc-search min-h-11 w-full py-2 pl-10 pr-3 text-sm text-slate-800 outline-none placeholder:text-slate-400 sm:min-h-8 sm:py-1.5" />
          </div>
          <div className="flex min-w-0 max-w-full items-center gap-1 overflow-x-auto" aria-label="Vehicle roster filters">
            {FILTERS.map((f) => (
              <button key={f} type="button" onClick={() => { if (archiveTarget || lifecycleInFlight.current) return; setFilter(f); setOffset(0); setSelectedId(null); }}
                aria-pressed={filter === f}
                className={`min-h-11 shrink-0 rounded-lg border px-2.5 text-xs font-semibold transition sm:min-h-8 ${filter === f ? "border-teal-300 bg-teal-50 text-teal-800" : "border-slate-200 bg-white text-slate-600 hover:border-slate-300 hover:bg-slate-50"}`}>
                {f}
              </button>
            ))}
          </div>
          <span className="ml-auto text-[11px] font-semibold text-slate-400 tabular-nums">
            {filtered.length} shown
          </span>
        </div>

        <div className="mx-2 mb-1 flex min-h-0 flex-1 flex-col overflow-hidden rounded-lg border border-slate-200 bg-white">
          <div className="min-h-0 flex-1 overflow-auto">
            {filtered.length ? (
              <table className="w-full min-w-[720px] text-left text-sm">
                <thead className="sticky top-0 z-10 bg-[#fcfdff]">
                  <tr className="border-b border-slate-200/80 text-[10px] uppercase tracking-[0.12em] text-slate-400">
                    <th className="px-3 py-2.5 font-bold">Vehicle</th>
                    <th className="px-3 py-2.5 font-bold">Status</th>
                    <th className="px-3 py-2.5 font-bold">Reported speed</th>
                    <th className="hidden px-3 py-2.5 font-bold lg:table-cell">Last seen</th>
                    <th className="px-3 py-2.5 font-bold">Readiness</th>
                    <th className="px-3 py-2.5 font-bold">Risk</th>
                    <th className="hidden px-3 py-2.5 font-bold md:table-cell">Driver</th>
                    <th className="hidden px-3 py-2.5 font-bold xl:table-cell">Health</th>
                    <th className="px-3 py-2.5"><span className="sr-only">Open</span></th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100/90">
                  {filtered.map((row) => {
                    const ready = vehicleReadiness(row);
                    const speed = readSpeedMph(row);
                    const fresh = freshness(g(row, "lastSeenAt", "last_seen_at"));
                    const moving = isMoving(row);
                    return (
                      <tr key={String(row.id)} onClick={() => { if (!archiveTarget && !lifecycleInFlight.current) setSelectedId(row.id as string); }}
                        className="group cursor-pointer transition hover:bg-sky-50/50">
                        <td className="px-3 py-2.5">
                          <div className="flex items-center gap-2.5">
                            <span className={`h-2 w-2 shrink-0 rounded-full ${moving ? "bg-emerald-500 animate-pulse" : fresh?.live ? "bg-sky-400" : "bg-slate-300"}`} />
                            <div className="min-w-0">
                              <div className="font-semibold text-slate-900">{String(g(row, "vehicleCode", "vehicle_code") ?? `Vehicle ${row.id}`)}</div>
                              <div className="truncate text-xs text-slate-500">{[g(row, "make"), g(row, "model")].filter(Boolean).join(" ") || String(g(row, "type") ?? "—")}</div>
                            </div>
                          </div>
                        </td>
                        <td className="px-3 py-2.5"><StatusPill status={g(row, "status")} /></td>
                        <td className="px-3 py-2.5">
                          {fresh && speed != null ? (
                            <span className="inline-flex items-baseline gap-1">
                              <span className={`text-[15px] font-bold tabular-nums ${moving ? "text-emerald-600" : "text-slate-400"}`}>{Math.round(speed)}</span>
                              <span className="text-[10px] font-semibold text-slate-400">mph</span>
                            </span>
                          ) : fresh ? (
                            <span className="text-xs italic text-slate-400">Speed unavailable</span>
                          ) : (
                            <span className="text-xs italic text-slate-400">No GPS</span>
                          )}
                        </td>
                        <td className="hidden px-3 py-2.5 lg:table-cell">
                          {fresh ? (
                            <span className={`inline-flex items-center gap-1.5 text-xs font-medium ${fresh.live ? "text-emerald-600" : "text-slate-500"}`}>
                              {fresh.live && <span className="live-dot h-1.5 w-1.5" />}
                              {fresh.label}
                            </span>
                          ) : <span className="text-xs italic text-slate-400">—</span>}
                        </td>
                        <td className="px-3 py-2.5"><Meter value={ready} /></td>
                        <td className="px-3 py-2.5"><RiskChip tier={riskTier(row)} /></td>
                        <td className="hidden px-3 py-2.5 text-slate-600 md:table-cell">{String(g(row, "assignedDriver", "assigned_driver", "driverName") ?? "—")}</td>
                        <td className="hidden px-3 py-2.5 xl:table-cell">
                          <div className="flex items-center gap-3">
                          <HealthDot status={vehicleDeviceStatus(row)} icon={<Cpu className="h-3.5 w-3.5" />} />
                          <HealthDot status={vehicleCameraStatus(row)} icon={<Camera className="h-3.5 w-3.5" />} />
                          </div>
                        </td>
                        <td className="px-3 py-2.5 text-right">
                          <button type="button" className="ml-auto grid min-h-11 min-w-11 place-items-center rounded-lg text-slate-400 transition hover:bg-slate-100 hover:text-slate-700 sm:min-h-8 sm:min-w-8" aria-label={`Open ${String(g(row, "vehicleCode", "vehicle_code") ?? `vehicle ${row.id}`)}`} onClick={(event) => { event.stopPropagation(); setSelectedId(row.id as string); }}>
                            <ChevronRight className="h-4 w-4 transition group-hover:translate-x-0.5" />
                          </button>
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            ) : (
              <div className="grid h-full place-items-center py-12">
                <EmptyState title="No vehicles match" subtitle="Adjust your search or filter, or add a new vehicle." />
              </div>
            )}
          </div>
        </div>

        {/* Pager / instrument footer */}
        {totalRows > PAGE_SIZE ? (
          <div className="flex shrink-0 flex-wrap items-center justify-between gap-2 px-3 py-2 text-[12px] text-slate-600">
            <span className="tabular-nums">
              Showing <strong>{totalRows === 0 ? 0 : offset + 1}–{Math.min(offset + PAGE_SIZE, totalRows)}</strong> of <strong>{totalRows.toLocaleString()}</strong>
            </span>
            <div className="flex items-center gap-2">
              <button type="button" disabled={offset === 0 || list.isFetching}
                onClick={() => setOffset(Math.max(0, offset - PAGE_SIZE))}
                className="fc-pager-btn min-h-11 sm:min-h-8">← Prev</button>
              <span className="text-xs text-slate-400 tabular-nums">Page {Math.floor(offset / PAGE_SIZE) + 1} of {Math.max(1, Math.ceil(totalRows / PAGE_SIZE))}</span>
              <button type="button" disabled={offset + PAGE_SIZE >= totalRows || list.isFetching}
                onClick={() => setOffset(offset + PAGE_SIZE)}
                className="fc-pager-btn min-h-11 sm:min-h-8">Next →</button>
            </div>
          </div>
        ) : (
          <div className="flex shrink-0 flex-wrap items-center gap-x-4 gap-y-2 px-3 py-2 text-[11.5px] font-semibold text-slate-500">
            <span className="inline-flex items-center gap-2"><span className="deck-led deck-led-emerald" /> {moving} moving on page</span>
            <span className="inline-flex items-center gap-2"><span className={`deck-led ${deviceEx != null && deviceEx > 0 ? "deck-led-amber" : "deck-led-slate"}`} /> {deviceEx == null ? "Device gaps unavailable" : `${deviceEx} device gaps in authorized scope`}</span>
            <button type="button" onClick={() => navigate("/iot-devices")} className="ml-auto inline-flex items-center gap-1 font-bold text-teal-700 hover:underline">
              Device health <ChevronRight className="h-3 w-3" />
            </button>
          </div>
        )}
      </section>

      {selectedRecord && (
        <VehicleDrawer
          key={String(selectedRecord.id)} record={selectedRecord} detail={detail.data} loading={detail.isLoading}
          refreshing={detail.isFetching} detailError={detail.isError ? apiErrorMessage(detail.error, "Vehicle data could not be refreshed.") : null} onRefresh={() => { void detail.refetch(); }}
          canUpdate={canUpdate && !archivedView} canDelete={canDelete && !archivedView} canAssign={canAssign && !archivedView} canReactivate={canUpdate && archivedView} assigning={assign.isPending}
          canViewCameraEvidence={canViewCameraEvidence}
          lifecycleBusy={lifecycleBusy}
          lifecycleSelectionPreparing={lifecycleSelectionPreparing}
          lifecycleSelectionUnavailable={lifecycleSelectionUnavailable}
          lifecycleStatusError={lifecycleStatusError}
          lifecycleNeedsRefresh={lifecycleNeedsRefresh}
          lifecycleError={archiveTarget ? null : lifecycleRecoveryError ?? (reactivate.error ? apiErrorMessage(reactivate.error, "The vehicle could not be reactivated. Refresh its status before trying again.") : null)}
          onClose={closeVehicle}
          onEdit={() => { if (canUpdate && !archiveTarget && !lifecycleInFlight.current) { setIsCreating(false); setEditing(selectedRecord); } }}
          onDelete={openArchive}
          onReactivate={reactivateSelected}
          onRetryLifecycleStatus={() => { void detail.refetch(); }}
          onAssign={() => { if (canAssign && !archiveTarget && !lifecycleInFlight.current) setAssignmentVehicle(selectedRecord); }}
          onNavigate={(route) => { if (!archiveTarget && !lifecycleInFlight.current) navigate(route); }}
        />
      )}

      {archiveTarget ? (
        <ConfirmDialog
          title="Archive vehicle"
          message={`Archive ${archiveTarget.code}? It will leave the active fleet registry; the server may block archival while operational records still depend on it.`}
          confirmLabel={lifecycleNeedsRefresh ? "Reload vehicle status" : "Archive vehicle"}
          variant={lifecycleNeedsRefresh ? "default" : "danger"}
          busy={lifecycleBusy}
          error={lifecycleRecoveryError ?? (remove.error ? apiErrorMessage(remove.error, "The archive outcome could not be confirmed. Cancel and refresh the vehicle status before trying again.") : null)}
          returnFocusTo={lifecycleNeedsRefresh ? null : archiveTarget.opener}
          onConfirm={lifecycleNeedsRefresh ? reloadLifecycleStatus : confirmArchive}
          onCancel={cancelArchive}
        />
      ) : null}

      {editing && canManageFleet && (
        <VehicleFormModal title={isCreating ? "New vehicle" : "Edit vehicle"} initial={editing} saving={save.isPending} serverError={save.error ? apiErrorMessage(save.error, "The vehicle could not be saved. Please try again.") : undefined}
          onClose={() => { if (save.isPending) return; save.reset(); setEditing(null); setIsCreating(false); }} onSave={(p) => save.mutate(p)} />
      )}
      {assignmentVehicle && canAssign ? (
        <DriverAssignmentModal
          vehicle={assignmentVehicle}
          drivers={(drivers.data || []) as AnyRecord[]}
          saving={assign.isPending}
          serverError={assign.error ? apiErrorMessage(assign.error, "The assignment could not be saved. Please try again.") : undefined}
          onClose={() => setAssignmentVehicle(null)}
          onSave={(driverId) => assign.mutate({ vehicleId: String(assignmentVehicle.id), driverId })}
        />
      ) : null}
    </PageStack>
  );
}

/* ------------------------------------------------------------------ primitives */

function ClayStat({ Icon, tone, iconCls, label, value, meter, caption, alert }:
  { Icon: React.ElementType; tone: string; iconCls: string; label: string; value: React.ReactNode; meter?: number; caption?: string; alert?: boolean }) {
  const valueColor = alert && num(value) > 0 ? (tone.includes("red") ? "text-rose-600" : "text-amber-600") : "text-slate-900";
  const surfaceTone = tone.includes("red")
    ? "border-rose-100 bg-rose-50/45"
    : tone.includes("amber")
      ? "border-amber-100 bg-amber-50/45"
      : tone.includes("emerald")
        ? "border-emerald-100 bg-emerald-50/45"
        : "border-teal-100 bg-teal-50/45";
  return (
    <div className={`min-w-0 rounded-lg border px-3 py-2 ${surfaceTone}`} title={caption ? `${label}: ${value}. ${caption}` : label}>
      <dt className="flex min-w-0 items-center gap-2 text-[11px] font-bold leading-tight text-slate-600">
        <Icon className={`h-3.5 w-3.5 shrink-0 ${iconCls}`} />
        <span className="min-w-0 flex-1">{label}</span>
      </dt>
      <dd className="mt-1 flex min-w-0 items-baseline gap-2">
        <strong className={`shrink-0 text-base font-black leading-none tracking-tight tabular-nums ${valueColor}`}>{value}</strong>
        {caption ? <span className="min-w-0 truncate text-[10px] font-medium text-slate-500" title={caption}>{caption}</span> : null}
      </dd>
      {meter != null ? (
        <div className="deck-track mt-1.5" aria-hidden="true">
          <div className="deck-fill deck-fill-teal" style={{ width: `${Math.min(100, meter)}%` }} />
        </div>
      ) : null}
    </div>
  );
}

function StatusPill({ status }: { status?: unknown }) {
  const text = String(status ?? "—");
  const tone =
    /available|active|on route|en route|driving/i.test(text) ? "bg-emerald-50 text-emerald-700 ring-emerald-600/15" :
    /maintenance|delayed|out/i.test(text) ? "bg-rose-50 text-rose-700 ring-rose-600/15" :
    /idle|at stop|stop/i.test(text) ? "bg-amber-50 text-amber-700 ring-amber-600/15" :
    "bg-slate-100 text-slate-600 ring-slate-500/15";
  return <span className={`inline-flex items-center gap-1.5 rounded-full px-2.5 py-1 text-xs font-medium ring-1 ring-inset ${tone}`}>
    <span className="h-1.5 w-1.5 rounded-full bg-current opacity-70" />{text}
  </span>;
}

function RiskChip({ tier }: { tier: "High" | "Medium" | "Low" }) {
  const tone = tier === "High" ? "bg-rose-50 text-rose-700 ring-rose-600/15" : tier === "Medium" ? "bg-amber-50 text-amber-700 ring-amber-600/15" : "bg-slate-50 text-slate-500 ring-slate-500/15";
  return <span className={`inline-flex rounded-md px-2 py-0.5 text-xs font-medium ring-1 ring-inset ${tone}`}>{tier}</span>;
}

function Meter({ value }: { value: number | null }) {
  if (value == null) return <span className="text-xs font-semibold text-slate-400">Unknown</span>;
  const v = Math.round(value);
  const color = v >= 85 ? "deck-fill-emerald" : v >= 70 ? "deck-fill-amber" : "deck-fill-red";
  return (
    <div className="flex items-center gap-2.5">
      <div className="deck-track w-20"><div className={`deck-fill ${color}`} style={{ width: `${Math.min(100, v)}%` }} /></div>
      <span className="w-9 text-xs font-semibold tabular-nums text-slate-600">{v}%</span>
    </div>
  );
}

function HealthDot({ status, icon }: { status: string; icon: React.ReactNode }) {
  const healthy = /online|recording/i.test(status);
  const unknown = /unknown/i.test(status);
  return <span className={`inline-flex items-center ${healthy ? "text-emerald-500" : unknown ? "text-slate-400" : "text-amber-500"}`} title={status}>{icon}</span>;
}

/* ------------------------------------------------------------------ lifecycle band */

function DriverAssignmentModal({ vehicle, drivers, saving, serverError, onClose, onSave }: {
  vehicle: AnyRecord;
  drivers: AnyRecord[];
  saving: boolean;
  serverError?: string;
  onClose: () => void;
  onSave: (driverId: string) => void;
}) {
  const dialogRef = useDialogFocus<HTMLFormElement>(true, () => { if (!saving) onClose(); });
  const currentDriverId = String(g(vehicle, "assignedDriverId", "assigned_driver_id") ?? "");
  const [driverId, setDriverId] = useState(currentDriverId);
  const availableDrivers = useMemo(() => drivers
    .filter((driver) => {
      const assignedVehicleId = g(driver, "assignedVehicleId", "assigned_vehicle_id");
      return assignedVehicleId == null || String(driver.id) === currentDriverId;
    })
    .sort((a, b) => String(g(a, "driverCode", "driver_code") ?? "").localeCompare(String(g(b, "driverCode", "driver_code") ?? ""))), [drivers, currentDriverId]);
  const selected = drivers.find((driver) => String(driver.id) === driverId);
  const vehicleCode = String(g(vehicle, "vehicleCode", "vehicle_code") ?? `Vehicle ${vehicle.id}`);

  return (
    <div className="fixed inset-0 z-[80] grid place-items-center bg-slate-900/50 p-4 backdrop-blur-sm anim-fade-in">
      <form ref={dialogRef} role="dialog" aria-modal="true" aria-labelledby="driver-assignment-title" className="panel w-full max-w-xl p-6 shadow-2xl" onSubmit={(event) => { event.preventDefault(); if (driverId) onSave(driverId); }}>
        <div className="flex items-start justify-between border-b border-slate-200 pb-4">
          <div>
            <p className="section-title text-teal-700">Fleet master assignment</p>
            <h2 id="driver-assignment-title" className="mt-1 text-xl font-bold text-slate-900">{currentDriverId ? "Reassign" : "Assign"} {vehicleCode}</h2>
            <p className="mt-1 text-sm text-slate-500">Choose the intended driver, review the change, then confirm. The previous pairing will be recorded in assignment history. Dispatch readiness is checked separately before a job is assigned.</p>
          </div>
          <button type="button" className="icon-btn" onClick={onClose} disabled={saving} aria-label="Close"><X className="h-5 w-5" /></button>
        </div>
        <label className="mt-5 block">
          <span className="mb-1.5 block text-xs font-bold uppercase tracking-[0.14em] text-slate-500">Driver</span>
          <select className="field w-full" required value={driverId} onChange={(event) => setDriverId(event.target.value)}>
            <option value="">Select an available driver</option>
            {availableDrivers.map((driver) => (
              <option key={String(driver.id)} value={String(driver.id)}>
                {String(g(driver, "driverCode", "driver_code") ?? `Driver ${driver.id}`)} — {String(g(driver, "fullName", "full_name") ?? "Unnamed driver")}
              </option>
            ))}
          </select>
        </label>
        {selected ? (
          <div className="mt-4 rounded-xl border border-teal-200 bg-teal-50 p-4 text-sm text-slate-700">
            <strong>{vehicleCode}</strong> will be assigned to <strong>{String(g(selected, "driverCode", "driver_code") ?? selected.id)} — {String(g(selected, "fullName", "full_name") ?? "Unnamed driver")}</strong>.
          </div>
        ) : null}
        {serverError ? <p role="alert" className="mt-4 rounded-xl border border-rose-200 bg-rose-50 p-3 text-sm text-rose-700">{serverError}</p> : null}
        <div className="mt-6 flex justify-end gap-3 border-t border-slate-200 pt-4">
          <button type="button" className="btn-ghost" onClick={onClose} disabled={saving}>Cancel</button>
          <button type="submit" className="btn-primary" disabled={saving || !driverId || driverId === currentDriverId}>{saving ? "Saving assignment…" : "Confirm assignment"}</button>
        </div>
      </form>
    </div>
  );
}

/* ------------------------------------------------------------------ drawer */

function VehicleDrawer({ record, detail, loading, refreshing, detailError, onRefresh, canUpdate, canDelete, canAssign, canReactivate, canViewCameraEvidence, assigning, lifecycleBusy, lifecycleSelectionPreparing, lifecycleSelectionUnavailable, lifecycleStatusError, lifecycleNeedsRefresh, lifecycleError, onClose, onEdit, onDelete, onReactivate, onRetryLifecycleStatus, onAssign, onNavigate }: {
  record: AnyRecord; detail?: AnyRecord; loading: boolean; refreshing: boolean; detailError: string | null; onRefresh: () => void;
  canUpdate: boolean; canDelete: boolean; canAssign: boolean; canReactivate: boolean; canViewCameraEvidence: boolean; assigning: boolean;
  lifecycleBusy: boolean; lifecycleSelectionPreparing: boolean; lifecycleSelectionUnavailable: boolean;
  lifecycleStatusError: string | null; lifecycleNeedsRefresh: boolean; lifecycleError: string | null;
  onClose: () => void; onEdit: () => void; onDelete: () => void; onReactivate: () => void; onRetryLifecycleStatus: () => void;
  onAssign: () => void; onNavigate: (r: string) => void;
}) {
  const code = String(g(record, "vehicleCode", "vehicle_code") ?? `Vehicle ${record.id}`);
  const dialogRef = useDialogFocus<HTMLElement>(true, onClose);
  const [tab, setTab] = useState<"behaviour" | "devices" | "records">("behaviour");
  const rows = (key: string) => Array.isArray(detail?.[key]) ? detail[key] as AnyRecord[] : [];
  const currentDevices = rows("currentDevices");
  const retainedInstallations = rows("installationHistory").filter((installation) => g(installation, "effectiveTo", "effective_to") == null && /^(Installed|Verified)$/i.test(String(installation.status)) && !currentDevices.some((current) => String(g(current, "installationId", "installation_id")) === String(g(installation, "installationId", "installation_id"))));
  const activeJobs = rows("activeJobs");
  const replayTrail = rows("replayTrail");
  const access = detail?.activityAccess as AnyRecord | undefined;
  const speed = readSpeedMph(record);
  const seenAt = g(record, "lastSeenAt", "last_seen_at");
  const fresh = freshness(seenAt);
  const motion = observedMotion(speed, seenAt);
  const lat = g(record, "lat"), lng = g(record, "lng");
  const hasGps = lat != null && lng != null && Number.isFinite(Number(lat)) && Number.isFinite(Number(lng)) && Math.abs(Number(lat)) <= 90 && Math.abs(Number(lng)) <= 180 && !(Number(lat) === 0 && Number(lng) === 0);
  const track = () => onNavigate(`/map-view?vehicleId=${encodeURIComponent(String(record.id))}`);
  const canReadDevices = useHasPermission()(PERMISSIONS.TELEMETRY_DEVICES_READ);
  const profile = [
    ["Recorded status", String(g(record, "status") ?? "Unknown")],
    ["Assigned driver", String(g(record, "assignedDriver", "assigned_driver", "driverName") ?? "Unassigned")],
    ["Odometer (recorded)", measuredNumber(g(record, "odometerMiles", "odometer_miles"), "mi")],
    ["Year", String(g(record, "year") ?? "Unknown")],
    ["VIN", String(g(record, "vin") ?? "Unknown")],
    ["Plate", [g(record, "plateNumber", "plate_number"), g(record, "plateJurisdiction", "plate_jurisdiction")].filter(Boolean).join(" · ") || "Unknown"],
    ["Class", String(g(record, "vehicleClass", "vehicle_class") ?? "Unknown")],
    ["Alternate identity", g(record, "alternateIdentifier", "alternate_identifier") ? `${String(g(record, "alternateIdentifier", "alternate_identifier"))} (${String(g(record, "vinExceptionType", "vin_exception_type") ?? "governed exception")})` : "Not recorded"],
  ];

  return (
    <div className="fixed inset-0 z-50 flex justify-end bg-slate-900/30 backdrop-blur-[2px]" onClick={onClose}>
      <aside ref={dialogRef} role="dialog" aria-modal="true" aria-label={`${code} vehicle workspace`} className="vehicle-workspace fleet-console flex h-full w-full max-w-[680px] flex-col border-l border-slate-200 shadow-2xl anim-slide-left" onClick={(e) => e.stopPropagation()}>
        <div className="fc-drawer-head shrink-0 px-4 py-3">
          <div className="flex items-center justify-between gap-2">
            <div className="min-w-0"><p className="text-[10px] font-semibold text-slate-500">Vehicle workspace</p><h2 className="truncate text-lg font-bold text-slate-900">{code}</h2></div>
            <button type="button" aria-label="Close vehicle workspace" disabled={lifecycleBusy} onClick={onClose} className="icon-btn"><X className="h-4 w-4" /></button>
          </div>
          <div className="mt-2 flex flex-wrap items-center gap-2">
            <button type="button" disabled={refreshing || lifecycleBusy} onClick={onRefresh} className="btn-ghost h-8 px-2.5 text-xs"><RefreshCw className={`h-3.5 w-3.5 ${refreshing ? "animate-spin" : ""}`} /> {refreshing ? "Refreshing…" : "Refresh vehicle"}</button>
            {canUpdate ? <button type="button" disabled={lifecycleBusy} onClick={onEdit} className="btn-primary h-8 px-2.5 text-xs">Edit vehicle</button> : null}
            <details className="relative ml-auto">
              <summary className="btn-ghost h-8 cursor-pointer list-none px-2.5 text-xs [&::-webkit-details-marker]:hidden">More</summary>
              <div className="absolute right-0 z-20 mt-1 flex w-48 flex-col gap-1 rounded-xl border border-slate-200 bg-white p-2 shadow-lg">
                {canAssign ? <button type="button" disabled={assigning || lifecycleBusy} onClick={onAssign} className="btn-ghost text-xs"><UserCheck className="h-3.5 w-3.5" /> Change driver</button> : null}
                {hasGps ? <button type="button" disabled={lifecycleBusy} onClick={track} className="btn-ghost text-xs"><MapPin className="h-3.5 w-3.5" /> Track vehicle</button> : null}
                {canDelete ? <button type="button" disabled={lifecycleBusy || lifecycleSelectionPreparing || lifecycleSelectionUnavailable || lifecycleNeedsRefresh} aria-busy={lifecycleSelectionPreparing ? true : undefined} onClick={onDelete} className="btn-ghost text-xs text-rose-700"><Trash2 className="h-3.5 w-3.5" /> {lifecycleSelectionPreparing ? "Checking status…" : lifecycleSelectionUnavailable ? "Status unavailable" : "Archive vehicle"}</button> : null}
                {canReactivate ? <button type="button" disabled={lifecycleBusy || lifecycleSelectionPreparing || lifecycleSelectionUnavailable || lifecycleNeedsRefresh} aria-busy={lifecycleSelectionPreparing ? true : undefined} onClick={onReactivate} className="btn-ghost text-xs"><ArchiveRestore className="h-3.5 w-3.5" /> {lifecycleSelectionPreparing ? "Checking status…" : lifecycleSelectionUnavailable ? "Status unavailable" : "Reactivate vehicle"}</button> : null}
              </div>
            </details>
          </div>
          {(lifecycleStatusError || lifecycleError) ? <div role="alert" className="mt-2 rounded-lg border border-amber-200 bg-amber-50 p-2 text-xs text-amber-800">
            {lifecycleStatusError || lifecycleError}
            {lifecycleNeedsRefresh ? <button type="button" disabled={lifecycleBusy} className="ml-2 underline" onClick={() => window.location.reload()}>Reload vehicle status</button> : <button type="button" disabled={lifecycleBusy || lifecycleSelectionPreparing} className="ml-2 underline" onClick={onRetryLifecycleStatus}>Retry status check</button>}
          </div> : null}
          <nav aria-label="Selected vehicle sections" className="mt-2 flex gap-1 border-t border-slate-200 pt-2">
            {([['behaviour', 'Status & behaviour'], ['devices', `Devices (${currentDevices.length + retainedInstallations.length})`], ['records', 'Maintenance & history']] as const).map(([key, label]) => <button type="button" key={key} aria-pressed={tab === key} onClick={() => setTab(key)} className={`min-h-9 rounded-lg px-2.5 text-xs font-semibold ${tab === key ? "bg-teal-50 text-teal-800 ring-1 ring-teal-200" : "text-slate-600 hover:bg-slate-100"}`}>{label}</button>)}
          </nav>
        </div>
        <div className="min-h-0 flex-1 space-y-3 overflow-y-auto overscroll-contain p-4">
          {detailError ? <div role="alert" className="rounded-lg border border-rose-200 bg-rose-50 p-3 text-sm text-rose-800">{detailError} {detail ? "Previously loaded records may be out of date." : "Devices and activity have not been loaded."}<button type="button" disabled={refreshing} onClick={onRefresh} className="ml-2 underline">Retry vehicle data</button></div> : null}
          {loading ? <p role="status" className="p-3 text-sm text-slate-500">Loading vehicle, installations and activity…</p> : null}
          {detail && !loading ? <>
            {tab === "behaviour" ? <>
              <section className="rounded-xl border border-slate-200 bg-white p-3 shadow-sm">
                <div className="flex flex-wrap items-center justify-between gap-2"><h3 className="text-sm font-bold text-slate-900">{motion}</h3><span className="text-xs text-slate-500">{fresh ? `GPS ${fresh.label}` : "No verified GPS timestamp"}</span></div>
                <dl className="vehicle-property-list mt-2">
                  <dt>Last reported speed</dt><dd>{measuredNumber(speed, "mph")}{speed != null && !fresh?.live ? " · historical sample" : ""}</dd>
                  <dt>GPS time</dt><dd>{timestamp(seenAt)}</dd>
                  <dt>Observation source</dt><dd>{String(g(record, "observationSource") ?? "Not reported")}</dd>
                  <dt>Heading</dt><dd>{headingLabel(g(record, "heading"))}</dd>
                  <dt>Last known position</dt><dd>{hasGps ? `${Number(lat).toFixed(4)}, ${Number(lng).toFixed(4)}` : "Not reported"}{hasGps ? <button type="button" onClick={track} className="ml-2 font-semibold text-teal-700 underline">Track vehicle</button> : null}</dd>
                  <dt>Recorded status</dt><dd>{String(g(record, "status") ?? "Unknown")}</dd>
                  <dt>Assigned driver</dt><dd>{String(g(record, "assignedDriver", "assigned_driver", "driverName") ?? "Unassigned")}</dd>
                  <dt>Devices</dt><dd><button type="button" onClick={() => setTab("devices")} className="font-semibold text-teal-700 underline">Inspect {currentDevices.length} active / {retainedInstallations.length} retained installations</button></dd>
                </dl>
                {!fresh?.live ? <p className="mt-2 text-xs text-amber-700">Recorded status and old measurements do not establish current movement. Inspect device communication before dispatch.</p> : null}
              </section>
              <DrawerSection title="Active work" icon={<Boxes className="h-4 w-4" />} count={activeJobs.length}>
                {access?.activeJobs === false ? <EmptyLine text="Job activity is not available for this role." /> : activeJobs.length ? <div className="space-y-1">{activeJobs.map((job, i) => <button type="button" key={String(job.id ?? i)} onClick={() => onNavigate(`/jobs?jobId=${encodeURIComponent(String(job.id))}`)} className="flex w-full items-center justify-between rounded-lg border border-slate-200 bg-white px-3 py-2 text-left text-xs"><span><strong>{String(g(job, "jobNumber", "job_number") ?? `Job ${job.id}`)}</strong> · {String(job.status ?? "Unknown")}{g(job, "eta") ? ` · ETA ${timestamp(job.eta)}` : ""}</span><ChevronRight className="h-4 w-4" /></button>)}</div> : <EmptyLine text="No active work returned for this vehicle." />}
              </DrawerSection>
              <DrawerTable title="Driving & device events" icon={<ShieldAlert className="h-4 w-4" />} rows={rows("safetyEvents")} cols={["eventType", "severity", "reviewStatus", "eventTime"]} onEmpty="No recorded driving or device events returned." />
              <p className="text-[11px] text-slate-500">Active work returns up to 12 records. GPS history covers the latest 100 observations within 24 hours.</p>
              <DrawerSection title="Recent GPS observations" icon={<Navigation className="h-4 w-4" />} count={replayTrail.length}>
                {access?.replayTrail === false ? <EmptyLine text="GPS history is not available for this role." /> : replayTrail.length ? <><ReplayTrail points={replayTrail} /><DrawerTable title="GPS samples · latest 24 hours" icon={<Radio className="h-4 w-4" />} rows={replayTrail} cols={["eventTime", "speedMph", "lat", "lng"]} onEmpty="No samples." collapsed /></> : <EmptyLine text="No GPS observations returned for the last 24 hours." />}
              </DrawerSection>
            </> : null}
            {tab === "devices" ? <>
              <p className="text-xs text-slate-600">Every installation below belongs to this vehicle. Communication, received evidence and remote capability are separate checks.</p>
              {currentDevices.length ? currentDevices.map((device, i) => <InstalledDeviceWorkspace key={String(g(device, "installationId", "installation_id") ?? i)} installation={device} vehicleId={String(record.id)} onNavigate={onNavigate} />) : <EmptyLine text="No active authoritative device installation exists for this vehicle. Inspect installation history for previous devices." />}
              {retainedInstallations.length ? <section><h3 className="mb-2 text-xs font-semibold text-amber-800">Retained installation records · excluded from active devices</h3><p className="mb-2 text-xs text-slate-600">These links may reference archived devices or an installation that changed. Their current state is checked below.</p><div className="space-y-2">{retainedInstallations.map((installation, index) => <InstalledDeviceWorkspace key={String(g(installation, "installationId", "installation_id") ?? index)} installation={installation} vehicleId={String(record.id)} onNavigate={onNavigate} />)}</div></section> : null}
              <DrawerTable title="Device installation history" icon={<Radio className="h-4 w-4" />} rows={rows("installationHistory")} cols={["deviceSerial", "deviceRole", "status", "effectiveFrom", "effectiveTo"]} onEmpty="No installation history recorded." collapsed onOpen={canReadDevices ? (row) => { const id = g(row, "deviceId", "device_id"); if (id != null) onNavigate(deviceWorkspaceRoute(String(id))); } : undefined} />
            </> : null}
            {tab === "records" ? <>
              <details className="record-detail-section" open><summary>Vehicle profile</summary><dl className="vehicle-property-list mt-2">{profile.map(([label, value]) => <div key={label} className="contents"><dt>{label}</dt><dd>{value}</dd></div>)}</dl></details>
              <DrawerTable title="Maintenance records" icon={<Wrench className="h-4 w-4" />} rows={rows("maintenance")} cols={["serviceType", "status", "priority", "dueDate"]} onEmpty="No maintenance records returned." collapsed />
              <DrawerTable title="Driver assignment history" icon={<UserCheck className="h-4 w-4" />} rows={rows("assignmentHistory")} cols={["driverCode", "driverName", "status", "effectiveFrom", "effectiveTo"]} onEmpty="No assignment history recorded." collapsed />
              <DrawerTable title="Trips" icon={<Navigation className="h-4 w-4" />} rows={rows("trips")} cols={["tripNumber", "status", "startedAt", "completedAt"]} onEmpty="No trips returned." collapsed />
              <DrawerTable title="Documents" icon={<Boxes className="h-4 w-4" />} rows={rows("documents")} cols={["documentName", "documentType", "status", "expiryDate"]} onEmpty="No vehicle documents returned." collapsed />
              <DrawerTable title="Compliance records" icon={<ShieldAlert className="h-4 w-4" />} rows={rows("compliance")} cols={["documentType", "status", "expiryDate"]} onEmpty="No compliance records returned." collapsed />
              <DrawerTable title="Audit history" icon={<Activity className="h-4 w-4" />} rows={rows("auditTrail")} cols={["actionName", "createdAt", "actorUserId"]} onEmpty="No linked audit records returned." collapsed />
              {canViewCameraEvidence ? <DrawerSection title="Verified camera evidence" icon={<Video className="h-4 w-4" />} count={rows("videoEvents").length} collapsed>
                {rows("videoEvents").length ? <div className="max-h-64 space-y-2 overflow-auto">{rows("videoEvents").map((event, index) => <div key={String(event.id ?? index)} className="flex items-center gap-3 rounded-lg border border-slate-200 bg-white p-2">
                  {g(event, "thumbnailUrl", "thumbnail_url") ? <img src={String(g(event, "thumbnailUrl", "thumbnail_url"))} alt="Verified camera event thumbnail" loading="lazy" className="h-16 w-24 rounded object-cover" /> : <Video className="h-5 w-5 text-slate-400" />}
                  <span className="text-xs"><strong>{String(g(event, "eventType", "event_type") ?? "Camera event")}</strong><br />{String(event.severity ?? "Unknown")} · {String(g(event, "reviewStatus", "review_status") ?? "Unknown")}</span>
                </div>)}</div> : <EmptyLine text="No provider-verified, media-ready camera evidence is available for this vehicle." />}
              </DrawerSection> : null}
            </> : null}
          </> : null}
        </div>
      </aside>
    </div>
  );
}

function InstalledDeviceWorkspace({ installation, vehicleId, onNavigate }: { installation: AnyRecord; vehicleId: string; onNavigate: (route: string) => void }) {
  const hasPermission = useHasPermission();
  const id = g(installation, "deviceId", "device_id");
  const canRead = hasPermission(PERMISSIONS.TELEMETRY_DEVICES_READ);
  const { session } = useAuth();
  const device = useQuery({ queryKey: ["vehicle-device-workspace", session?.company?.id, session?.user?.id, vehicleId, id], queryFn: () => telematicsService.getDeviceById(String(id)), enabled: canRead && id != null, refetchInterval: 30_000, retry: false });
  const detail = canRead && !device.isError ? device.data : undefined;
  const record = detail?.device;
  // Installation can change between the vehicle read and the device read. Do not expose
  // another vehicle's mutable settings as actions belonging to this selected vehicle.
  const stillInstalled = detail != null && String(detail.currentInstallation?.vehicleId) === vehicleId && record?.archivedAt == null && !/archived|retired/i.test(record?.lifecycleStatus ?? "");
  const availableCommands = detail?.remoteCommandCapabilities.filter((capability) => capability.requestAdmissionAvailable && !capability.externalHold) ?? [];
  const communication = record?.lastCheckIn;
  return <section className="rounded-xl border border-slate-200 bg-white p-3 shadow-sm">
    <div className="flex flex-wrap items-center justify-between gap-2"><h3 className="break-all text-sm font-bold text-slate-900">{String(g(installation, "deviceSerial", "device_serial") ?? "Registered device")}</h3><span className="text-xs text-slate-500">{String(g(installation, "deviceRole", "device_role") ?? "Device")} · {String(g(installation, "status") ?? "Unknown")}</span></div>
    {device.isLoading ? <p role="status" className="mt-2 text-xs text-slate-500">Reading device health and supported operations…</p> : null}
    {!canRead ? <p className="mt-2 text-xs text-amber-700">Device settings and diagnostics require device-read access.</p> : null}
    {device.isError ? <p role="alert" className="mt-2 text-xs text-rose-700">Device health could not be loaded. <button type="button" onClick={() => void device.refetch()} className="underline">Retry device</button></p> : null}
    {detail ? <>
      <dl className="vehicle-property-list mt-2">
        <dt>Lifecycle / communication</dt><dd>{record?.lifecycleStatus} · {observationAge(communication) == null ? "Communication unverified" : record?.connectionStatus}</dd>
        <dt>Last communication</dt><dd>{observationAge(communication) == null ? "Never reported or unverified timestamp" : timestamp(communication)}</dd>
        <dt>Setup / software checks</dt><dd>{record?.deviceOpsAssessmentAvailable ? record.deviceOpsGaps.join(" · ") || "No issues in the recorded assessment" : "No device assessment available"}</dd>
        <dt>Received faults</dt><dd>{detail.diagnostics.length ? detail.diagnostics.map((fault) => fault.faultCode).join(" · ") : "No received fault evidence returned"}</dd>
        <dt>Remote operations</dt><dd>{availableCommands.length ? `${availableCommands.map((capability) => capability.displayName).join(", ")} · request admission only` : "No verified remote operation available"}</dd>
        <dt>SIM / APN profile</dt><dd>{detail.currentConnectivityProfile ? `${detail.currentConnectivityProfile.carrierName} · inventory profile recorded` : "No inventory profile recorded"}</dd>
      </dl>
      {!stillInstalled ? <p role="status" className="mt-2 text-xs text-amber-700">This device is archived, removed or no longer active on this vehicle. Its historical device record remains available; refresh the vehicle after changing installation records.</p> : null}
      <div className="mt-2 flex flex-wrap gap-2">
        <button type="button" onClick={() => onNavigate(deviceWorkspaceRoute(String(id)))} className="btn-ghost h-8 px-2.5 text-xs">Device details</button>
        {stillInstalled ? <><button type="button" onClick={() => onNavigate(deviceWorkspaceRoute(String(id), "configuration"))} className="btn-ghost h-8 px-2.5 text-xs">SIM / APN inventory</button><button type="button" onClick={() => onNavigate(deviceWorkspaceRoute(String(id), "diagnostics"))} className="btn-ghost h-8 px-2.5 text-xs">Diagnostics evidence</button><button type="button" onClick={() => onNavigate(deviceWorkspaceRoute(String(id), "commands"))} className="btn-ghost h-8 px-2.5 text-xs">Test / command options</button></> : null}
      </div>
      <p className="mt-2 text-[11px] text-slate-500">Settings record inventory metadata. Hardware testing and restart require verified provider support; a request is not proof of execution.</p>
    </> : null}
  </section>;
}

function timestamp(value: unknown) {
  if (value == null || value === "") return "Not reported";
  const date = new Date(String(value));
  return Number.isNaN(date.getTime()) ? "Unverified timestamp" : date.toLocaleString();
}

function headingLabel(heading: unknown): string {
  // Number(null) === 0 (a valid finite value), which would fabricate a "N" heading
  // for units that report no bearing — reject null/empty explicitly first.
  const h = optionalTelemetryHeading(heading);
  if (h == null) return "—";
  const dirs = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"];
  return dirs[Math.round(((h % 360) / 45)) % 8];
}

function ReplayTrail({ points }: { points: AnyRecord[] }) {
  const trail = points.slice(-12);
  const speeds = trail.map(readSpeedMph);
  const { peak, knownCount, missingCount } = telemetrySpeedSummary(speeds);
  const scale = Math.max(1, peak ?? 0); // chart scale is not a measured peak
  return (
    <div className="deck-inset rounded-xl p-3">
      <div className="flex items-end gap-1" style={{ height: 44 }}>
        {trail.map((p, i) => {
          const sp = readSpeedMph(p);
          if (sp == null) return <div key={i} className="flex-1 self-stretch border-b border-dashed border-slate-300" title="Speed unavailable" aria-label="Speed unavailable" />;
          const h = Math.max(4, Math.round((sp / scale) * 40));
          const color = sp > 1 ? "bg-emerald-400" : "bg-slate-300";
          return <div key={i} className={`flex-1 rounded-t ${color}`} style={{ height: h }} title={`${Math.round(sp)} mph`} />;
        })}
      </div>
      <div className="mt-2 flex items-center justify-between text-[10px] font-semibold text-slate-400">
        <span>{knownCount}/{trail.length} measured speeds{missingCount > 0 ? ` · ${missingCount} unavailable` : ""}</span>
        <span className="tabular-nums">{peak == null ? "peak unavailable" : `peak ${Math.round(peak)} mph`}</span>
      </div>
    </div>
  );
}

function DrawerSection({ title, icon, count, loading, children, collapsed = false }: { title: string; icon: React.ReactNode; count: number; loading?: boolean; children: React.ReactNode; collapsed?: boolean }) {
  const heading = <span className="flex items-center gap-2 text-xs font-bold text-slate-700"><span className="text-teal-700">{icon}</span>{title}<span className="ml-auto text-slate-500">{count}</span></span>;
  const body = loading ? <p role="status" className="text-xs text-slate-500">Loading…</p> : children;
  return collapsed ? <details className="record-detail-section"><summary>{heading}</summary><div className="mt-2">{body}</div></details> : <section><h3 className="mb-2">{heading}</h3>{body}</section>;
}

function DrawerTable({ title, icon, rows, cols, loading, onEmpty, collapsed = false, onOpen }: { title: string; icon: React.ReactNode; rows?: AnyRecord[]; cols: string[]; loading?: boolean; onEmpty: string; collapsed?: boolean; onOpen?: (row: AnyRecord) => void }) {
  const data = rows || [];
  return <DrawerSection title={title} icon={icon} count={data.length} loading={loading} collapsed={collapsed}>
    {data.length === 0 ? <EmptyLine text={onEmpty} /> : <div className="max-h-64 overflow-auto rounded-lg border border-slate-200 bg-white"><table className="w-full text-left text-xs">
      <thead className="sticky top-0 bg-slate-50"><tr>{cols.map((key) => <th key={key} className="px-2 py-2 font-semibold text-slate-500">{labelize(key)}</th>)}{onOpen ? <th className="px-2 py-2">Details</th> : null}</tr></thead>
      <tbody className="divide-y divide-slate-100">{data.map((row, index) => <tr key={String(row.id ?? index)}>{cols.map((key) => <td key={key} className="px-2 py-2 text-slate-700">{fmt(g(row, key, key.replace(/[A-Z]/g, (char) => `_${char.toLowerCase()}`)))}</td>)}{onOpen ? <td className="px-2 py-2"><button type="button" className="text-teal-700 underline" onClick={() => onOpen(row)}>Device details</button></td> : null}</tr>)}</tbody>
    </table></div>}
  </DrawerSection>;
}

function EmptyLine({ text }: { text: string }) {
  return (
    <p className="deck-inset flex items-center gap-2 rounded-xl px-3 py-2.5 text-[12px] font-medium text-slate-400">
      <Info className="h-3.5 w-3.5 shrink-0" />{text}
    </p>
  );
}

function fmt(v: unknown) {
  if (v == null || v === "") return "—";
  const s = String(v);
  if (/^\d{4}-\d{2}-\d{2}T/.test(s)) return timestamp(s);
  return s;
}

/* ------------------------------------------------------------------ form modal */

function VehicleFormModal({ title, initial, saving, serverError, onClose, onSave }: { title: string; initial: AnyRecord; saving: boolean; serverError?: string; onClose: () => void; onSave: (p: AnyRecord) => void }) {
  const dialogRef = useDialogFocus<HTMLFormElement>(true, () => { if (!saving) onClose(); });
  const [form, setForm] = useState<AnyRecord>(initial);
  const [errors, setErrors] = useState<string[]>([]);

  useEffect(() => { setForm(initial); }, [initial]);

  const submit = (e: FormEvent) => {
    e.preventDefault();
    const nextErrors: string[] = [];
    const payload: AnyRecord = { ...form };

    for (const field of FIELDS) {
      const raw = payload[field.key];
      const value = String(raw ?? "").trim();
      if (field.required && value === "") {
        nextErrors.push(`${field.label} is required.`);
        continue;
      }
      if (field.type === "number") {
        if (value === "") {
          payload[field.key] = undefined;
          continue;
        }
        const parsed = Number(value);
        if (Number.isNaN(parsed)) {
          nextErrors.push(`${field.label} must be a valid number.`);
          continue;
        }
        payload[field.key] = parsed;
      } else {
        payload[field.key] = raw;
      }
    }

    const vin = String(payload.vin ?? "").trim();
    const alternateKind = String(payload.vinExceptionType ?? "").trim();
    const alternateIdentifier = String(payload.alternateIdentifier ?? "").trim();
    if (!vin && !alternateKind) nextErrors.push("Choose an approved alternate identity kind when the vehicle has no VIN.");
    if (!vin && !alternateIdentifier) nextErrors.push("Alternate identifier is required when the vehicle has no VIN.");
    if (!vin && alternateIdentifier && (alternateIdentifier.length < 4 || alternateIdentifier.length > 64)) {
      nextErrors.push("Alternate identifier must contain 4 to 64 characters.");
    }
    if (vin) {
      payload.vin = vin.toUpperCase();
      payload.vinExceptionType = undefined;
      payload.alternateIdentifier = undefined;
    }

    if (nextErrors.length > 0) {
      setErrors(nextErrors);
      return;
    }

    setErrors([]);
    onSave(payload);
  };
  return (
    <div className="fixed inset-0 z-[60] grid place-items-center bg-slate-900/40 p-4 backdrop-blur-sm" onClick={onClose}>
      <form ref={dialogRef} role="dialog" aria-modal="true" aria-label={title} onClick={(e) => e.stopPropagation()} onSubmit={submit} className="fc-neumo max-h-[calc(100dvh-2rem)] w-full max-w-xl overflow-y-auto overscroll-contain p-6 anim-fade-up">
        <div className="flex items-start justify-between">
          <div>
            <div className="text-[11px] font-bold uppercase tracking-[0.16em] text-slate-400">Fleet</div>
            <h2 className="mt-1 text-xl font-black tracking-tight text-slate-900">{title}</h2>
          </div>
          <button type="button" aria-label="Close" disabled={saving} onClick={onClose} className="rounded-lg p-2 text-slate-400 transition hover:bg-slate-100 hover:text-slate-700 disabled:cursor-wait disabled:opacity-50"><X className="h-5 w-5" /></button>
        </div>
        <div className="mt-6 grid gap-4 sm:grid-cols-2">
          {FIELDS.map((f) => (
            <label key={f.key} className="block">
              <span className="mb-1.5 block text-xs font-bold text-slate-600">{f.label}{f.required ? <span className="text-rose-500"> *</span> : null}</span>
              {f.type === "select" && f.options ? (
                <select className="fc-search w-full px-3 py-2.5 text-sm text-slate-800 outline-none"
                  required={Boolean(f.required)} value={String(form[f.key] ?? "")} onChange={(e) => setForm((c) => ({ ...c, [f.key]: e.target.value }))}>
                  <option value="">Select</option>
                  {optionsWithPersistedValue(f.options, form[f.key]).map((o) => <option key={o} value={o}>{o}</option>)}
                </select>
              ) : (
                <input className="fc-search w-full px-3 py-2.5 text-sm text-slate-800 outline-none placeholder:text-slate-400"
                  type={f.type === "number" ? "number" : "text"} required={Boolean(f.required)}
                  value={String(form[f.key] ?? "")} onChange={(e) => setForm((c) => ({ ...c, [f.key]: e.target.value }))} />
              )}
            </label>
          ))}
        </div>
        {errors.length > 0 && (
          <div className="mt-4 rounded-xl border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700">
            <ul className="space-y-1">
              {errors.map((error) => <li key={error}>{error}</li>)}
            </ul>
          </div>
        )}
        {serverError ? (
          <div role="alert" aria-live="assertive" className="mt-4 rounded-xl border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700">
            {serverError}
          </div>
        ) : null}
        <div className="mt-6 flex justify-end gap-3">
          <button type="button" disabled={saving} onClick={onClose} className="btn-ghost h-11 disabled:cursor-wait disabled:opacity-50">Cancel</button>
          <button type="submit" disabled={saving} className="btn-primary h-11"><Save className="h-4 w-4" /> {saving ? "Saving…" : "Save vehicle"}</button>
        </div>
      </form>
    </div>
  );
}
