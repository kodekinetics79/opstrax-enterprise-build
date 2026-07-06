import { useEffect, useMemo, useState } from 'react';
import {
  AlertTriangle, ArrowRight, CheckCircle2, ChevronRight, ClipboardList, Fuel,
  Gauge, MapPinned, Package, RefreshCw, Truck, Wrench,
} from 'lucide-react';
import { useNavigate } from 'react-router-dom';
import { notifyApiError } from '@/services/fleetTmsApi';
import {
  fleetApi,
  type FleetFuelEvent,
  type FleetMaintenanceTicket,
  type FleetOverview,
  type FleetShipment,
  type FleetTrackingPoint,
  type FleetVehicle,
} from '@/services/fleetTmsApi';
import { carriersApi } from '@/services/carriersApi';
import { useAuth } from '@/hooks/useAuth';
import { ShipmentLifecycleDrawer } from '../components/fleet/ShipmentLifecycleDrawer';
import type { AnyRecord } from '@/types';

type FleetMode = 'command' | 'shipments' | 'vehicles' | 'tracking' | 'maintenance' | 'fuel' | 'carriers';

// Operational copy only — every line describes what the surface shows, never the product.
const MODULES: Record<FleetMode, { label: string; short: string; description: string }> = {
  command:     { label: 'Command',     short: 'Command',     description: 'Live posture across movement, assets, service and spend.' },
  shipments:   { label: 'Shipments',   short: 'Shipments',   description: 'Booked loads, movement state and proof progression.' },
  vehicles:    { label: 'Vehicles',    short: 'Vehicles',    description: 'Assignment, readiness, fuel and service timing per unit.' },
  tracking:    { label: 'Tracking',    short: 'Tracking',    description: 'Movement events, geofences and telemetry exceptions.' },
  maintenance: { label: 'Maintenance', short: 'Maint.',      description: 'Work orders, downtime and vendor activity.' },
  fuel:        { label: 'Fuel',        short: 'Fuel',        description: 'Fueling events, spend and anomaly review.' },
  carriers:    { label: 'Carriers',    short: 'Carriers',    description: 'External capacity — compliance, on-time and cost posture.' },
};

const MODE_ORDER: FleetMode[] = ['command', 'shipments', 'vehicles', 'tracking', 'maintenance', 'fuel', 'carriers'];

// Real carrier row from /api/carriers (tenant-scoped, DB-backed).
interface CarrierRow {
  id: string;
  name: string;
  number: string;
  region: string;
  status: string;
  compliance: string;
  onTime: number;
  performance: number;
  action: string;
}

const num = (v: unknown) => (Number.isFinite(Number(v)) ? Number(v) : 0);

function toCarrierRow(raw: AnyRecord): CarrierRow {
  return {
    id: String(raw.id),
    name: String(raw.name ?? 'Carrier'),
    number: String(raw.carrierNumber ?? raw.mcNumber ?? ''),
    region: String(raw.region ?? ''),
    status: String(raw.status ?? '—'),
    compliance: String(raw.complianceStatus ?? '—'),
    onTime: num(raw.onTimePercent),
    performance: num(raw.performanceScore),
    action: String(raw.recommendedAction ?? ''),
  };
}

function fmtTime(iso?: string): string | null {
  if (!iso) return null;
  const t = Date.parse(iso);
  if (Number.isNaN(t)) return null;
  return new Date(t).toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' });
}

export function FleetWorkspacePage({ mode: initialMode = 'command' }: { mode?: FleetMode }) {
  const navigate = useNavigate();
  const { session } = useAuth();
  const [mode, setMode] = useState<FleetMode>(initialMode);
  const config = MODULES[mode];
  const [overview, setOverview] = useState<FleetOverview | null>(null);
  const [shipments, setShipments] = useState<FleetShipment[]>([]);
  const [vehicles, setVehicles] = useState<FleetVehicle[]>([]);
  const [tracking, setTracking] = useState<FleetTrackingPoint[]>([]);
  const [maintenance, setMaintenance] = useState<FleetMaintenanceTicket[]>([]);
  const [fuel, setFuel] = useState<FleetFuelEvent[]>([]);
  const [carriers, setCarriers] = useState<CarrierRow[]>([]);
  const [selectedShipment, setSelectedShipment] = useState<FleetShipment | null>(null);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [loadWarnings, setLoadWarnings] = useState<string[]>([]);
  const [authIssue, setAuthIssue] = useState<string | null>(null);
  const [savingId, setSavingId] = useState<string | null>(null);

  const loadWorkspaceData = async () => {
    const warnings: string[] = [];
    const isUnauthorized = (reason: unknown) => {
      const status = typeof reason === 'object' && reason && 'response' in reason
        ? Number((reason as { response?: { status?: number } }).response?.status ?? 0)
        : 0;
      const message = String((reason as { message?: string } | null | undefined)?.message ?? reason ?? '');
      return status === 401 || /unauthorized|missing bearer token/i.test(message);
    };
    const [ov, shipmentRes, vehicleRes, trackingRes, maintenanceRes, fuelRes, carrierRes] = await Promise.allSettled([
      fleetApi.overview(),
      fleetApi.shipments({ pageSize: 12 }),
      fleetApi.vehicles(),
      fleetApi.tracking({ pageSize: 12 }),
      fleetApi.maintenance({ pageSize: 12 }),
      fleetApi.fuel({ pageSize: 12 }),
      carriersApi.list(),
    ]);

    const apply = <T,>(result: PromiseSettledResult<T>, label: string, setter: (value: T) => void) => {
      if (result.status === 'fulfilled') {
        setter(result.value);
        return;
      }
      if (isUnauthorized(result.reason)) {
        setAuthIssue('Your session is missing or expired. Sign in again to load Fleet TMS Workspace.');
        return;
      }
      warnings.push(`${label} could not load (${result.reason instanceof Error ? result.reason.message : 'request failed'}).`);
    };

    apply(ov, 'Fleet overview', setOverview);
    apply(shipmentRes, 'Shipments', (value) => setShipments(value.items));
    apply(vehicleRes, 'Vehicles', (value) => setVehicles(value.items));
    apply(trackingRes, 'Tracking', (value) => setTracking(value.items));
    apply(maintenanceRes, 'Maintenance', (value) => setMaintenance(value.items));
    apply(fuelRes, 'Fuel', (value) => setFuel(value.items));
    apply(carrierRes, 'Carriers', (value) => setCarriers(((value as AnyRecord[]) || []).map(toCarrierRow)));

    setLoadWarnings(warnings);
  };

  useEffect(() => {
    if (!session?.token) {
      setAuthIssue('Sign in is required to load Fleet TMS Workspace.');
      setLoading(false);
      return;
    }
    let cancelled = false;
    setLoading(true);
    setAuthIssue(null);
    (async () => {
      try {
        await loadWorkspaceData();
        if (cancelled) return;
      } catch (err) {
        const status = typeof err === 'object' && err && 'response' in err
          ? Number((err as { response?: { status?: number } }).response?.status ?? 0)
          : 0;
        const message = String((err as { message?: string } | null | undefined)?.message ?? err ?? '');
        if (!cancelled && (status === 401 || /unauthorized|missing bearer token/i.test(message))) {
          setAuthIssue('Your session has expired or is not attached. Sign in again to load Fleet TMS Workspace.');
          return;
        }
        if (!cancelled) notifyApiError(err, 'Unable to load fleet workspace.');
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [session?.token]);

  const refreshAll = async () => {
    setRefreshing(true);
    try {
      await loadWorkspaceData();
    } finally {
      setRefreshing(false);
    }
  };

  const summary = overview?.summary;

  // Per-mode instrument readouts — every value is a live count from the fetched rows.
  const signals = useMemo(() => {
    switch (mode) {
      case 'shipments':
        return [
          { label: 'In transit', value: String(shipments.filter((s) => s.status === 'In Transit').length) },
          { label: 'Awaiting proof', value: String(shipments.filter((s) => s.status !== 'Delivered').length) },
          { label: 'Lead load', value: shipments[0]?.shipmentNumber ?? '—' },
        ];
      case 'vehicles':
        return [
          { label: 'Available', value: String(vehicles.filter((v) => v.status === 'Available').length) },
          { label: 'Service exposure', value: String(vehicles.filter((v) => v.healthStatus !== 'Healthy').length) },
          { label: 'Units listed', value: String(vehicles.length) },
        ];
      case 'tracking':
        return [
          { label: 'Moving', value: String(tracking.filter((p) => p.status === 'In Transit').length) },
          { label: 'With alerts', value: String(tracking.filter((p) => p.alertType).length) },
          { label: 'Top speed', value: tracking.length ? `${Math.max(0, ...tracking.map((p) => p.speedKph)).toFixed(0)} km/h` : '—' },
        ];
      case 'maintenance':
        return [
          { label: 'Open orders', value: String(maintenance.filter((t) => t.status !== 'Closed').length) },
          { label: 'High priority', value: String(maintenance.filter((t) => t.priority === 'High' || t.priority === 'Critical').length) },
          { label: 'Downtime hrs', value: maintenance.reduce((s, t) => s + t.downtimeHours, 0).toFixed(0) },
        ];
      case 'fuel':
        return [
          { label: 'Events', value: String(fuel.length) },
          { label: 'Flagged', value: String(fuel.filter((e) => e.anomalyFlag).length) },
          { label: 'Spend listed', value: fuel.reduce((s, e) => s + e.cost, 0).toFixed(0) },
        ];
      case 'carriers':
        return [
          { label: 'On record', value: String(carriers.length) },
          { label: 'Compliance risk', value: String(carriers.filter((c) => !/^compliant$/i.test(c.compliance)).length) },
          { label: 'Avg on-time', value: carriers.length ? `${(carriers.reduce((s, c) => s + c.onTime, 0) / carriers.length).toFixed(0)}%` : '—' },
        ];
      default:
        return [
          { label: 'On trip', value: String(summary?.onTripVehicles ?? 0) },
          { label: 'Delivered today', value: String(summary?.deliveredToday ?? 0) },
          { label: 'Delivery rate', value: summary ? `${summary.deliveredRate.toFixed(1)}%` : '—' },
        ];
    }
  }, [carriers, fuel, maintenance, mode, shipments, summary, tracking, vehicles]);

  const exceptions = useMemo(() => {
    const rows: Array<{ id: string; label: string; detail: string; led: string }> = [];
    if (summary?.fuelAlerts) rows.push({ id: 'fuel-alerts', label: 'Fuel alerts', detail: `${summary.fuelAlerts} fueling events need review.`, led: 'deck-led-amber' });
    if (summary?.openMaintenance) rows.push({ id: 'maintenance', label: 'Maintenance queue', detail: `${summary.openMaintenance} work orders open or in progress.`, led: 'deck-led-amber' });
    if (summary?.enRoute) rows.push({ id: 'movement', label: 'Freight in motion', detail: `${summary.enRoute} shipments on the road now.`, led: 'deck-led-sky' });
    const carriersAtRisk = carriers.filter((c) => !/^compliant$/i.test(c.compliance)).length;
    if (carriersAtRisk) rows.push({ id: 'carrier-compliance', label: 'Carrier compliance', detail: `${carriersAtRisk} carriers not fully compliant.`, led: 'deck-led-red' });
    return rows.slice(0, 4);
  }, [carriers, summary]);

  const handleDispatch = async (shipment: FleetShipment) => {
    setSavingId(shipment.id);
    try {
      await fleetApi.dispatchShipment(shipment.id, {
        vehicleNumber: shipment.vehicleNumber,
        driverName: shipment.driverName,
        routeCode: shipment.routeCode,
        notes: shipment.notes,
      });
      await refreshAll();
    } catch (err) {
      notifyApiError(err, 'Unable to dispatch shipment.');
    } finally {
      setSavingId(null);
    }
  };

  const handleService = async (vehicle: FleetVehicle) => {
    setSavingId(vehicle.id);
    try {
      await fleetApi.serviceVehicle(vehicle.id, {
        status: 'Maintenance',
        healthStatus: 'Reviewing',
        nextServiceAtUtc: vehicle.nextServiceAtUtc,
        notes: 'Sent for planned service from fleet control.',
      });
      await refreshAll();
    } catch (err) {
      notifyApiError(err, 'Unable to update vehicle service state.');
    } finally {
      setSavingId(null);
    }
  };

  const handleCloseMaintenance = async (ticket: FleetMaintenanceTicket) => {
    setSavingId(ticket.id);
    try {
      await fleetApi.closeMaintenance(ticket.id, {
        status: 'Closed',
        actualCost: ticket.estimatedCost,
        notes: 'Closed after review from fleet operations.',
      });
      await refreshAll();
    } catch (err) {
      notifyApiError(err, 'Unable to close maintenance ticket.');
    } finally {
      setSavingId(null);
    }
  };

  const handleFlagFuel = async (eventRow: FleetFuelEvent) => {
    setSavingId(eventRow.id);
    try {
      await fleetApi.flagFuelEvent(eventRow.id, {
        anomalyFlag: true,
        notes: eventRow.notes || 'Flagged for manual review by fleet operations.',
      });
      await refreshAll();
    } catch (err) {
      notifyApiError(err, 'Unable to flag fuel event.');
    } finally {
      setSavingId(null);
    }
  };

  if (authIssue) {
    return (
      <div className="fleet-console grid min-h-[70vh] place-items-center">
        <section className="fc-neumo w-full max-w-xl p-8">
          <div className="flex items-center gap-3">
            <span className="fc-blob h-12 w-12 text-amber-600"><AlertTriangle className="h-5 w-5" /></span>
            <div>
              <p className="section-title text-amber-700">Authentication required</p>
              <h1 className="mt-1 text-xl font-black text-slate-950">Sign in to open the Fleet TMS Workspace</h1>
            </div>
          </div>
          <p className="mt-4 text-sm leading-6 text-slate-600">{authIssue}</p>
          <div className="mt-6 flex flex-wrap gap-3">
            <button type="button" className="btn-primary" onClick={() => navigate('/login')}>
              Go to login
              <ArrowRight className="h-4 w-4" />
            </button>
            <button type="button" className="btn-ghost" onClick={() => window.location.reload()}>
              Retry
            </button>
          </div>
        </section>
      </div>
    );
  }

  const generatedAt = fmtTime(overview?.generatedAtUtc);
  const boardCount =
    mode === 'vehicles' ? vehicles.length :
    mode === 'tracking' ? tracking.length :
    mode === 'maintenance' ? maintenance.length :
    mode === 'fuel' ? fuel.length :
    mode === 'carriers' ? carriers.length : shipments.length;

  return (
    <div className="fleet-console flex h-full min-h-0 flex-col gap-3">

      {/* ── Console rail ─────────────────────────────────────────────────── */}
      <header className="fc-rail relative shrink-0 px-6 py-3.5">
        <div className="flex flex-wrap items-center gap-x-6 gap-y-3">
          <div className="min-w-0">
            <span className="section-title inline-flex items-center gap-2">
              <Truck className="h-3.5 w-3.5 text-teal-700" /> Fleet &amp; TMS
            </span>
            <h1 className="mt-1 text-[26px] font-black leading-none tracking-tight text-slate-950">{config.label}</h1>
            <p className="mt-1.5 text-[12.5px] font-medium text-slate-500">{config.description}</p>
          </div>
          <div className="ml-auto flex flex-wrap items-center gap-2.5">
            {generatedAt && (
              <span className="inline-flex items-center gap-1.5 text-[11px] font-semibold text-slate-500">
                <span className="live-dot h-1.5 w-1.5" /> Updated {generatedAt}
              </span>
            )}
            <button type="button" className="btn-ghost h-10" disabled={refreshing} onClick={() => void refreshAll()}>
              <RefreshCw className={`h-4 w-4 ${refreshing ? 'animate-spin' : ''}`} /> Refresh
            </button>
            <button type="button" className="btn-primary h-10" onClick={() => navigate('/map-view')}>
              Live map <MapPinned className="h-4 w-4" />
            </button>
          </div>
        </div>
        <div className="fc-seg mt-3 flex flex-wrap items-center gap-1 p-1">
          {MODE_ORDER.map((item) => (
            <button
              key={item}
              type="button"
              onClick={() => setMode(item)}
              className={`fc-seg-btn ${item === mode ? 'fc-seg-btn-active' : ''}`}
            >
              {MODULES[item].short}
            </button>
          ))}
        </div>
      </header>

      {loadWarnings.length > 0 && (
        <div className="shrink-0 rounded-2xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-900">
          <p className="font-semibold">Some workspace data could not be loaded.</p>
          <ul className="mt-1 space-y-0.5 text-xs text-amber-800">
            {loadWarnings.slice(0, 3).map((warning) => <li key={warning}>• {warning}</li>)}
          </ul>
        </div>
      )}

      {/* ── Clay overview tiles — pressable mode shortcuts ────────────────── */}
      <div className="grid shrink-0 grid-cols-2 gap-3 xl:grid-cols-4">
        <ClayStat Icon={Package} tone="fc-clay-teal"    iconCls="text-teal-700"    label="Active shipments" value={summary?.activeShipments ?? (loading ? '…' : 0)} caption="Booked + moving"      active={mode === 'shipments'}   onClick={() => setMode(mode === 'shipments' ? 'command' : 'shipments')} />
        <ClayStat Icon={Truck}   tone="fc-clay-emerald" iconCls="text-emerald-700" label="Fleet available"  value={summary?.activeVehicles ?? (loading ? '…' : 0)}  caption="Ready for assignment" active={mode === 'vehicles'}    onClick={() => setMode(mode === 'vehicles' ? 'command' : 'vehicles')} />
        <ClayStat Icon={Wrench}  tone="fc-clay-amber"   iconCls="text-amber-700"   label="Open maintenance" value={summary?.openMaintenance ?? (loading ? '…' : 0)} caption="Work orders open"     active={mode === 'maintenance'} onClick={() => setMode(mode === 'maintenance' ? 'command' : 'maintenance')} alert={Boolean(summary?.openMaintenance)} />
        <ClayStat Icon={Fuel}    tone="fc-clay-red"     iconCls="text-rose-700"    label="Fuel alerts"      value={summary?.fuelAlerts ?? (loading ? '…' : 0)}      caption="Anomalies to review"  active={mode === 'fuel'}        onClick={() => setMode(mode === 'fuel' ? 'command' : 'fuel')} alert={Boolean(summary?.fuelAlerts)} />
      </div>

      {/* ── Main deck ─────────────────────────────────────────────────────── */}
      <div className="grid min-h-0 flex-1 grid-cols-1 gap-3 xl:grid-cols-[minmax(0,1fr)_312px]">

        {/* Operational board */}
        <section className="fc-neumo flex min-h-[460px] flex-col overflow-hidden xl:min-h-0">
          <div className="flex shrink-0 flex-wrap items-center gap-x-3 gap-y-2 px-4 pb-2.5 pt-3.5">
            <div>
              <p className="section-title">Operational board</p>
              <p className="mt-0.5 text-[15px] font-black tracking-tight text-slate-950">
                {mode === 'vehicles' ? 'Fleet readiness' :
                 mode === 'tracking' ? 'Movement & live location' :
                 mode === 'maintenance' ? 'Maintenance queue' :
                 mode === 'fuel' ? 'Fuel events & anomalies' :
                 mode === 'carriers' ? 'Carrier panel' : 'Shipments & loads'}
              </p>
            </div>
            <span className="ml-auto text-[11px] font-semibold text-slate-400 tabular-nums">{boardCount} records</span>
          </div>

          <div className="fc-bezel mx-3 flex min-h-0 flex-1 flex-col">
            <div className="fc-screen min-h-0 flex-1 overflow-y-auto p-3">
              {loading ? (
                <div className="grid gap-2.5 md:grid-cols-2">
                  {Array.from({ length: 6 }).map((_, i) => <div key={i} className="skeleton h-28 rounded-2xl" />)}
                </div>
              ) : (
                <div className="grid gap-2.5 md:grid-cols-2">
                  {mode === 'vehicles' && vehicles.map((vehicle) => (
                    <VehicleCard key={vehicle.id} vehicle={vehicle} saving={savingId === vehicle.id} onService={() => handleService(vehicle)} />
                  ))}
                  {mode === 'tracking' && tracking.map((point) => (
                    <TrackingCard key={point.id} point={point} onOpen={() => {
                      const match = shipments.find((s) => s.shipmentNumber === point.shipmentNumber);
                      if (match) setSelectedShipment(match);
                    }} canOpen={shipments.some((s) => s.shipmentNumber === point.shipmentNumber)} />
                  ))}
                  {mode === 'maintenance' && maintenance.map((ticket) => (
                    <MaintenanceCard key={ticket.id} ticket={ticket} saving={savingId === ticket.id} onClose={() => handleCloseMaintenance(ticket)} />
                  ))}
                  {mode === 'fuel' && fuel.map((eventRow) => (
                    <FuelCard key={eventRow.id} eventRow={eventRow} saving={savingId === eventRow.id} onFlag={() => handleFlagFuel(eventRow)} />
                  ))}
                  {mode === 'carriers' && carriers.map((carrier) => (
                    <CarrierCard key={carrier.id} carrier={carrier} onManage={() => navigate('/carrier-management')} />
                  ))}
                  {(mode === 'command' || mode === 'shipments') && shipments.map((shipment) => (
                    <ShipmentCard key={shipment.id} shipment={shipment} saving={savingId === shipment.id}
                      onDispatch={() => handleDispatch(shipment)} onOpenLifecycle={() => setSelectedShipment(shipment)} />
                  ))}
                </div>
              )}

              {!loading && boardCount === 0 && (
                <div className="grid h-full min-h-[200px] place-items-center text-center">
                  <div>
                    <ClipboardList className="mx-auto h-8 w-8 text-slate-300" />
                    <p className="mt-2 text-sm font-semibold text-slate-500">
                      {mode === 'carriers' ? 'No carriers on record for this tenant yet.' : 'No records in this slice yet.'}
                    </p>
                    <p className="mt-1 text-xs text-slate-400">
                      {mode === 'carriers' ? 'Add carriers from Carrier Management.' : 'Rows appear here as soon as live data lands for this tenant.'}
                    </p>
                  </div>
                </div>
              )}
            </div>
          </div>

          <div className="flex shrink-0 flex-wrap items-center gap-x-5 gap-y-2 px-5 py-3 text-[11.5px] font-semibold text-slate-500">
            <span className="inline-flex items-center gap-2">
              <span className="deck-led deck-led-emerald" />
              <span className="tabular-nums">{summary?.enRoute ?? 0} en route</span>
            </span>
            <span className="inline-flex items-center gap-2">
              <span className={`deck-led ${(summary?.fuelAlerts ?? 0) > 0 ? 'deck-led-amber' : 'deck-led-slate'}`} />
              <span className="tabular-nums">{summary?.fuelAlerts ?? 0} fuel alerts</span>
            </span>
            <span className="inline-flex items-center gap-2">
              <span className={`deck-led ${(summary?.openMaintenance ?? 0) > 0 ? 'deck-led-amber' : 'deck-led-slate'}`} />
              <span className="tabular-nums">{summary?.openMaintenance ?? 0} work orders</span>
            </span>
            <button type="button" onClick={() => navigate('/jobs')} className="ml-auto inline-flex items-center gap-1 font-bold text-teal-700 hover:underline">
              Jobs board <ChevronRight className="h-3 w-3" />
            </button>
          </div>
        </section>

        {/* Instrument rail */}
        <aside className="flex min-h-0 flex-col gap-3 xl:overflow-y-auto">
          {/* Mode instruments */}
          <div className="fc-neumo shrink-0 p-4">
            <span className="section-title inline-flex items-center gap-2">
              <Gauge className="h-3.5 w-3.5 text-teal-700" /> {config.label} signals
            </span>
            <div className="mt-3 grid grid-cols-3 gap-2">
              {signals.map((signal) => (
                <div key={signal.label} className="deck-inset rounded-xl px-2 py-2.5 text-center">
                  <div className="truncate text-[15px] font-black leading-none tabular-nums text-slate-900">{signal.value}</div>
                  <div className="mt-1 text-[9px] font-bold uppercase tracking-wider text-slate-400">{signal.label}</div>
                </div>
              ))}
            </div>
          </div>

          {/* Load plan — real route aggregation from the overview */}
          <div className="fc-neumo shrink-0 p-4">
            <div className="flex items-center justify-between">
              <span className="section-title inline-flex items-center gap-2">
                <MapPinned className="h-3.5 w-3.5 text-teal-700" /> Load plan
              </span>
              <span className="text-[10px] font-bold uppercase tracking-wider text-slate-400 tabular-nums">
                {(overview?.loadPlanCards?.length ?? 0)} routes
              </span>
            </div>
            <div className="mt-3 space-y-2.5">
              {(overview?.loadPlanCards ?? []).slice(0, 4).map((plan) => (
                <div key={plan.routeCode || 'unassigned'} className="flex items-center gap-2.5">
                  <span className="w-20 shrink-0 truncate text-[11.5px] font-bold text-slate-700">{plan.routeCode || 'Unassigned'}</span>
                  <div className="deck-track flex-1">
                    <div className="deck-fill deck-fill-teal" style={{ width: `${Math.min(100, plan.shipmentCount * 22)}%` }} />
                  </div>
                  <span className="w-20 shrink-0 text-right text-[10.5px] font-semibold tabular-nums text-slate-500">
                    {plan.shipmentCount} · {plan.totalWeightKg.toFixed(0)} kg
                  </span>
                </div>
              ))}
              {!loading && (overview?.loadPlanCards?.length ?? 0) === 0 && (
                <p className="deck-inset rounded-xl px-3 py-2.5 text-[12px] font-medium text-slate-400">No routed shipments yet.</p>
              )}
            </div>
          </div>

          {/* Exceptions */}
          <div className="fc-neumo shrink-0 p-4">
            <span className="section-title inline-flex items-center gap-2">
              <AlertTriangle className="h-3.5 w-3.5 text-teal-700" /> Exceptions
            </span>
            <div className="mt-3 space-y-2">
              {exceptions.length ? exceptions.map((item) => (
                <div key={item.id} className="deck-alert flex items-start gap-2.5 px-3 py-2.5">
                  <span className={`deck-led mt-1 shrink-0 ${item.led}`} />
                  <span className="min-w-0">
                    <span className="block text-[12.5px] font-bold text-slate-800">{item.label}</span>
                    <span className="block text-[10.5px] font-medium text-slate-400">{item.detail}</span>
                  </span>
                </div>
              )) : (
                <div className="flex items-center gap-2.5 px-1 py-2">
                  <span className="deck-led deck-led-emerald" />
                  <p className="text-[12px] font-semibold text-slate-500">No open exceptions.</p>
                </div>
              )}
            </div>
          </div>

          {/* Signal summary */}
          <div className="fc-neumo flex-1 p-4">
            <span className="section-title inline-flex items-center gap-2">
              <CheckCircle2 className="h-3.5 w-3.5 text-teal-700" /> Today
            </span>
            <div className="mt-3 space-y-3">
              <RailMeter label="Delivery rate" value={summary ? `${summary.deliveredRate.toFixed(1)}%` : '—'} pct={summary?.deliveredRate ?? 0} fill="deck-fill-emerald" />
              <RailMeter label="Avg fuel level" value={summary ? `${summary.avgFuelLevel.toFixed(1)}%` : '—'} pct={summary?.avgFuelLevel ?? 0} fill="deck-fill-teal" />
              <div className="grid grid-cols-2 gap-2">
                <div className="deck-inset rounded-xl px-3 py-2.5 text-center">
                  <div className="text-[18px] font-black leading-none tabular-nums text-slate-900">{summary?.onTripVehicles ?? 0}</div>
                  <div className="mt-1 text-[9px] font-bold uppercase tracking-wider text-slate-400">On trip</div>
                </div>
                <div className="deck-inset rounded-xl px-3 py-2.5 text-center">
                  <div className="text-[18px] font-black leading-none tabular-nums text-slate-900">{summary?.deliveredToday ?? 0}</div>
                  <div className="mt-1 text-[9px] font-bold uppercase tracking-wider text-slate-400">Delivered today</div>
                </div>
              </div>
            </div>
          </div>
        </aside>
      </div>

      {selectedShipment && (
        <ShipmentLifecycleDrawer shipment={selectedShipment} onClose={() => setSelectedShipment(null)} />
      )}
    </div>
  );
}

/* ── primitives ─────────────────────────────────────────────────────────── */

function ClayStat({ Icon, tone, iconCls, label, value, caption, active, onClick, alert }:
  { Icon: React.ElementType; tone: string; iconCls: string; label: string; value: React.ReactNode; caption: string; active: boolean; onClick: () => void; alert?: boolean }) {
  const n = Number(value);
  const valueColor = alert && Number.isFinite(n) && n > 0 ? (tone.includes('red') ? 'text-rose-600' : 'text-amber-600') : 'text-slate-900';
  return (
    <button type="button" onClick={onClick} aria-pressed={active}
      className={`fc-clay ${tone} ${active ? 'deck-clay-pressed' : ''} p-4 text-left`}>
      <div className="flex items-center justify-between">
        <span className="text-[12px] font-bold text-slate-600">{label}</span>
        <span className="fc-blob"><Icon className={`h-4 w-4 ${iconCls}`} /></span>
      </div>
      <div className={`mt-2 text-[30px] font-black leading-none tracking-tight tabular-nums ${valueColor}`}>{value}</div>
      <p className="mt-2 text-[11px] font-medium text-slate-500">{caption}</p>
    </button>
  );
}

function RailMeter({ label, value, pct, fill }: { label: string; value: string; pct: number; fill: string }) {
  return (
    <div>
      <div className="flex items-center justify-between text-[11px] font-bold">
        <span className="text-slate-600">{label}</span>
        <span className="tabular-nums text-slate-800">{value}</span>
      </div>
      <div className="deck-track mt-1.5">
        <div className={`deck-fill ${fill}`} style={{ width: `${Math.min(100, Math.max(0, pct))}%` }} />
      </div>
    </div>
  );
}

function BoardCard({ children }: { children: React.ReactNode }) {
  return <div className="fc-card flex flex-col gap-2.5 p-3.5">{children}</div>;
}

function CardHead({ title, subtitle, chip }: { title: string; subtitle: string; chip: React.ReactNode }) {
  return (
    <div className="flex items-start justify-between gap-3">
      <div className="min-w-0">
        <p className="truncate text-[13px] font-black tracking-tight text-slate-950">{title}</p>
        <p className="truncate text-[11px] font-medium text-slate-500">{subtitle}</p>
      </div>
      {chip}
    </div>
  );
}

function CardChip({ text, tone = 'slate' }: { text: string; tone?: 'slate' | 'emerald' | 'amber' | 'rose' }) {
  const cls =
    tone === 'emerald' ? 'bg-emerald-50 text-emerald-700 ring-emerald-600/15' :
    tone === 'amber' ? 'bg-amber-50 text-amber-700 ring-amber-600/15' :
    tone === 'rose' ? 'bg-rose-50 text-rose-700 ring-rose-600/15' :
    'bg-slate-100 text-slate-600 ring-slate-500/15';
  return <span className={`shrink-0 rounded-full px-2.5 py-1 text-[9.5px] font-bold uppercase tracking-wide ring-1 ring-inset ${cls}`}>{text}</span>;
}

function CardMeta({ items }: { items: string[] }) {
  return (
    <div className="flex flex-wrap items-center gap-x-2 gap-y-1 text-[10px] font-semibold uppercase tracking-wide text-slate-400">
      {items.map((item, i) => (
        <span key={i} className="inline-flex items-center gap-2">
          {i > 0 && <span className="text-slate-300">•</span>}
          {item}
        </span>
      ))}
    </div>
  );
}

function statusTone(status: string): 'slate' | 'emerald' | 'amber' | 'rose' {
  if (/delivered|available|healthy|active|compliant|closed/i.test(status)) return 'emerald';
  if (/transit|route|moving|progress|open/i.test(status)) return 'amber';
  if (/risk|non-compliant|critical|overdue|review/i.test(status)) return 'rose';
  return 'slate';
}

/* ── mode cards ─────────────────────────────────────────────────────────── */

function ShipmentCard({ shipment, onDispatch, onOpenLifecycle, saving }: { shipment: FleetShipment; onDispatch: () => void; onOpenLifecycle: () => void; saving: boolean }) {
  return (
    <BoardCard>
      <CardHead title={shipment.shipmentNumber} subtitle={`${shipment.customerName} · ${shipment.destination}`}
        chip={<CardChip text={shipment.status} tone={statusTone(shipment.status)} />} />
      <CardMeta items={[shipment.vehicleNumber || 'Unassigned', shipment.priority, shipment.mode]} />
      <div className="mt-auto grid grid-cols-2 gap-2">
        <button type="button" onClick={onDispatch} disabled={saving || shipment.status === 'Delivered'} className="btn-primary h-9 justify-center px-3 text-xs">
          {saving ? 'Dispatching…' : 'Dispatch'}
        </button>
        <button type="button" onClick={onOpenLifecycle} className="btn-ghost h-9 justify-center px-3 text-xs">
          Lifecycle <ClipboardList className="h-3.5 w-3.5" />
        </button>
      </div>
    </BoardCard>
  );
}

function VehicleCard({ vehicle, onService, saving }: { vehicle: FleetVehicle; onService: () => void; saving: boolean }) {
  return (
    <BoardCard>
      <CardHead title={vehicle.vehicleNumber} subtitle={`${vehicle.type} · ${vehicle.driverName || 'Unassigned'}`}
        chip={<CardChip text={vehicle.status} tone={statusTone(vehicle.status)} />} />
      <CardMeta items={[`${vehicle.currentLoadKg.toFixed(0)} kg`, `${vehicle.fuelLevelPercent.toFixed(0)}% fuel`, vehicle.healthStatus]} />
      <div className="deck-track">
        <div className={`deck-fill ${vehicle.fuelLevelPercent >= 50 ? 'deck-fill-emerald' : vehicle.fuelLevelPercent >= 25 ? 'deck-fill-amber' : 'deck-fill-red'}`} style={{ width: `${Math.min(100, vehicle.fuelLevelPercent)}%` }} />
      </div>
      <button type="button" onClick={onService} disabled={saving} className="btn-ghost mt-auto h-9 justify-center px-3 text-xs">
        {saving ? 'Updating…' : 'Send to service'} <Wrench className="h-3.5 w-3.5" />
      </button>
    </BoardCard>
  );
}

function TrackingCard({ point, onOpen, canOpen }: { point: FleetTrackingPoint; onOpen: () => void; canOpen: boolean }) {
  return (
    <BoardCard>
      <CardHead title={point.shipmentNumber} subtitle={`${point.locationLabel} · ${point.geofenceName}`}
        chip={<CardChip text={point.status} tone={statusTone(point.status)} />} />
      <CardMeta items={[point.vehicleNumber, `${point.speedKph.toFixed(0)} km/h`, point.alertType || 'No alert']} />
      {point.alertType && (
        <p className="flex items-center gap-1.5 text-[11px] font-semibold text-amber-700">
          <AlertTriangle className="h-3 w-3 shrink-0" /> {point.alertType}
        </p>
      )}
      {canOpen && (
        <button type="button" onClick={onOpen} className="btn-ghost mt-auto h-9 justify-center px-3 text-xs">
          Open shipment <ChevronRight className="h-3.5 w-3.5" />
        </button>
      )}
    </BoardCard>
  );
}

function MaintenanceCard({ ticket, onClose, saving }: { ticket: FleetMaintenanceTicket; onClose: () => void; saving: boolean }) {
  return (
    <BoardCard>
      <CardHead title={ticket.workOrderNumber} subtitle={`${ticket.vehicleNumber} · ${ticket.vendorName}`}
        chip={<CardChip text={ticket.status} tone={statusTone(ticket.status)} />} />
      <CardMeta items={[ticket.priority, `${ticket.downtimeHours.toFixed(1)} hrs down`, `est ${ticket.estimatedCost.toFixed(0)}`]} />
      <button type="button" onClick={onClose} disabled={saving || ticket.status === 'Closed'} className="btn-ghost mt-auto h-9 justify-center px-3 text-xs">
        {saving ? 'Closing…' : ticket.status === 'Closed' ? 'Closed' : 'Close work order'} <CheckCircle2 className="h-3.5 w-3.5" />
      </button>
    </BoardCard>
  );
}

function FuelCard({ eventRow, onFlag, saving }: { eventRow: FleetFuelEvent; onFlag: () => void; saving: boolean }) {
  return (
    <BoardCard>
      <CardHead title={eventRow.vehicleNumber} subtitle={`${eventRow.stationName} · ${eventRow.city}`}
        chip={<CardChip text={eventRow.anomalyFlag ? 'Review' : eventRow.eventType} tone={eventRow.anomalyFlag ? 'amber' : 'slate'} />} />
      <CardMeta items={[`${eventRow.liters.toFixed(0)} L`, `cost ${eventRow.cost.toFixed(0)}`, eventRow.fuelCardNumber]} />
      <button type="button" onClick={onFlag} disabled={saving || eventRow.anomalyFlag} className="btn-ghost mt-auto h-9 justify-center px-3 text-xs">
        {saving ? 'Flagging…' : eventRow.anomalyFlag ? 'Flagged for review' : 'Flag anomaly'} <Fuel className="h-3.5 w-3.5" />
      </button>
    </BoardCard>
  );
}

function CarrierCard({ carrier, onManage }: { carrier: CarrierRow; onManage: () => void }) {
  return (
    <BoardCard>
      <CardHead title={carrier.name} subtitle={[carrier.number, carrier.region].filter(Boolean).join(' · ') || '—'}
        chip={<CardChip text={carrier.compliance} tone={statusTone(carrier.compliance)} />} />
      <div className="space-y-1.5">
        <RailMeter label="On-time" value={`${carrier.onTime.toFixed(0)}%`} pct={carrier.onTime} fill="deck-fill-emerald" />
        <RailMeter label="Performance" value={carrier.performance.toFixed(0)} pct={carrier.performance} fill="deck-fill-sky" />
      </div>
      {carrier.action && <p className="text-[11px] font-semibold text-slate-500">{carrier.action}</p>}
      <button type="button" onClick={onManage} className="btn-ghost mt-auto h-9 justify-center px-3 text-xs">
        Manage carrier <ChevronRight className="h-3.5 w-3.5" />
      </button>
    </BoardCard>
  );
}
