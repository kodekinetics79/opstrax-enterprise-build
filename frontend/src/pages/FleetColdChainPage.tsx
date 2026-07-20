import { useEffect, useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { BellRing, BatteryCharging, FlaskConical, Gauge, Layers3, Thermometer, Truck } from 'lucide-react';
import { ClayStat, ConsoleRail } from '@/components/console';
import { notifyApiError } from '@/services/fleetTmsApi';
import { fleetApi, fleetColdChainApi, type ColdChainReport, type TemperatureAlert, type TemperatureDevice, type TemperatureZone } from '@/services/fleetTmsApi';
import { Select } from '@/components/ui';

type SummaryState = {
  generatedAtUtc: string;
  summary: {
    activeDevices: number;
    readingsToday: number;
    openAlerts: number;
    policyCount?: number;
    eventLogCount?: number;
    totalReadings: number;
    breachReadings: number;
    avgTemperatureCelsius: number;
    compliancePercent: number;
  };
  zones: TemperatureZone[];
  devices: Array<Pick<TemperatureDevice, 'id' | 'deviceCode' | 'name' | 'vehicleNumber' | 'status' | 'lastReportedTemperatureCelsius' | 'batteryPercent' | 'lastPingAtUtc' | 'notes'> & {
    zoneCode?: string | null;
    zoneName?: string | null;
  }>;
  alerts: Array<Pick<TemperatureAlert, 'id' | 'alertType' | 'severity' | 'status' | 'measuredTemperature' | 'thresholdMin' | 'thresholdMax' | 'triggeredAtUtc' | 'resolutionNotes'>>;
  reports: ColdChainReport[];
  policies: Array<{
    id: string;
    policyCode: string;
    scopeType: string;
    scopeKey: string;
    minCelsius?: number | null;
    maxCelsius?: number | null;
    humidityMinPercent?: number | null;
    humidityMaxPercent?: number | null;
    requiresAcknowledgement: boolean;
    severity: string;
    status: string;
    notes?: string | null;
  }>;
};

type EventLogState = Array<{
  id: string;
  eventType: string;
  aggregateType: string;
  aggregateId: string;
  status: string;
  errorMessage?: string | null;
  occurredAtUtc: string;
  processedAtUtc?: string | null;
  correlationId?: string | null;
  causationId?: string | null;
}>;

export function FleetColdChainPage() {
  const [summary, setSummary] = useState<SummaryState | null>(null);
  const [devices, setDevices] = useState<TemperatureDevice[]>([]);
  const [alerts, setAlerts] = useState<TemperatureAlert[]>([]);
  const [shipments, setShipments] = useState<Array<{ id: string; shipmentNumber: string; status: string; customerName: string; mode: string }>>([]);
  const [events, setEvents] = useState<EventLogState>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [selectedShipmentId, setSelectedShipmentId] = useState('');
  const [selectedZoneId, setSelectedZoneId] = useState('');
  const [form, setForm] = useState({
    deviceCode: '',
    name: '',
    vehicleNumber: '',
    temperature: '4.0',
    batteryPercent: '92',
    notes: '',
    readingNotes: '',
    alertNotes: '',
  });

  const refresh = async () => {
    const [summaryRes, devicesRes, alertsRes, shipmentsRes] = await Promise.all([
      fleetColdChainApi.summary(),
      fleetColdChainApi.devices(),
      fleetColdChainApi.alerts(),
      fleetApi.shipments({ pageSize: 8 }),
    ]);
    setSummary(summaryRes);
    setDevices(devicesRes.items);
    setAlerts(alertsRes.items);
    setShipments(shipmentsRes.items as Array<{ id: string; shipmentNumber: string; status: string; customerName: string; mode: string }>);
    const eventsRes = await fleetColdChainApi.events();
    setEvents(eventsRes.items.slice(0, 6));
    setSelectedZoneId(summaryRes.zones[0]?.id ?? '');
    if (!selectedShipmentId && shipmentsRes.items[0]) {
      setSelectedShipmentId(shipmentsRes.items[0].id);
    }
  };

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError(null);
    (async () => {
      try {
        const [summaryRes, devicesRes, alertsRes, shipmentsRes] = await Promise.all([
          fleetColdChainApi.summary(),
          fleetColdChainApi.devices(),
          fleetColdChainApi.alerts(),
          fleetApi.shipments({ pageSize: 8 }),
        ]);
        if (cancelled) return;
        setSummary(summaryRes);
        setDevices(devicesRes.items);
        setAlerts(alertsRes.items);
        setShipments(shipmentsRes.items as Array<{ id: string; shipmentNumber: string; status: string; customerName: string; mode: string }>);
        const eventsRes = await fleetColdChainApi.events();
        if (cancelled) return;
        setEvents(eventsRes.items.slice(0, 6));
        setSelectedZoneId(summaryRes.zones[0]?.id ?? '');
        setSelectedShipmentId(shipmentsRes.items[0]?.id ?? '');
      } catch (err) {
        if (!cancelled) {
          setError('Unable to load the cold-chain workspace. Make sure the local API is running and the tenant database is available.');
          notifyApiError(err, 'Unable to load cold-chain workspace.');
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  const metrics = useMemo(() => {
    if (!summary) return [];
    return [
      { label: 'Active devices', value: summary.summary.activeDevices, icon: Thermometer },
      { label: 'Readings today', value: summary.summary.readingsToday, icon: Gauge },
      { label: 'Open alerts', value: summary.summary.openAlerts, icon: BellRing },
      { label: 'Compliance', value: `${summary.summary.compliancePercent}%`, icon: Layers3 },
    ];
  }, [summary]);

  const createDevice = async () => {
    if (!selectedZoneId) return;
    setSaving(true);
    try {
      await fleetColdChainApi.createDevice({
        deviceCode: form.deviceCode,
        name: form.name,
        zoneId: selectedZoneId ? Number(selectedZoneId) : undefined,
        shipmentId: selectedShipmentId ? Number(selectedShipmentId) : undefined,
        vehicleNumber: form.vehicleNumber,
        status: 'Active',
        lastReportedTemperatureCelsius: Number(form.temperature),
        batteryPercent: Number(form.batteryPercent),
        notes: form.notes,
      });
      setForm((current) => ({ ...current, deviceCode: '', name: '', vehicleNumber: '', notes: '' }));
      await refresh();
    } catch (err) {
      notifyApiError(err, 'Unable to create device.');
    } finally {
      setSaving(false);
    }
  };

  const logReading = async (deviceId: string) => {
    const device = devices.find((item) => item.id === deviceId);
    if (!device) return;
    try {
      await fleetColdChainApi.createReading({
        deviceId: Number(deviceId),
        shipmentId: selectedShipmentId ? Number(selectedShipmentId) : device.shipmentId ? Number(device.shipmentId) : undefined,
        zoneId: selectedZoneId ? Number(selectedZoneId) : device.zoneId ? Number(device.zoneId) : undefined,
        temperatureCelsius: Number(form.temperature),
        humidityPercent: 51,
        source: 'Sensor',
        status: 'Normal',
        notes: form.readingNotes || 'Manual telemetry sample created from the cold-chain console.',
      });
      await refresh();
    } catch (err) {
      notifyApiError(err, 'Unable to log temperature reading.');
    }
  };

  const resolveAlert = async (alertId: string) => {
    try {
      await fleetColdChainApi.resolveAlert(alertId, { resolutionNotes: form.alertNotes || 'Reviewed by operations.' });
      await refresh();
    } catch (err) {
      notifyApiError(err, 'Unable to resolve alert.');
    }
  };

  const generateReport = async (shipmentId: string) => {
    try {
      await fleetColdChainApi.report(shipmentId);
      await refresh();
    } catch (err) {
      notifyApiError(err, 'Unable to generate report.');
    }
  };

  if (loading || !summary) {
    if (error) {
      return (
        <main className="min-h-screen bg-[linear-gradient(135deg,_#f8fbff_0%,_#e8f2ff_50%,_#eff6ff_100%)] px-6 py-8 text-slate-900">
          <section className="mx-auto flex w-full max-w-4xl flex-col gap-4 rounded-[30px] border border-rose-200 bg-white/85 p-8 shadow-xl backdrop-blur">
            <p className="text-xs font-bold uppercase tracking-[0.24em] text-rose-500">Cold chain workspace</p>
            <h1 className="text-3xl font-black tracking-tight text-slate-950">The local API is not reachable yet.</h1>
            <p className="max-w-2xl text-slate-600">{error}</p>
            <div className="flex flex-wrap gap-3">
              <button type="button" onClick={() => window.location.reload()} className="rounded-full bg-slate-950 px-4 py-2.5 text-sm font-bold text-white">
                Retry
              </button>
              <Link to="/fleet-workspace" className="rounded-full border border-slate-200 bg-white px-4 py-2.5 text-sm font-semibold text-slate-700">
                Open Fleet Workspace
              </Link>
            </div>
          </section>
        </main>
      );
    }
    return (
      <main className="min-h-screen bg-[linear-gradient(135deg,_#f8fbff_0%,_#e8f2ff_50%,_#eff6ff_100%)] px-6 py-8 text-slate-900">
        <div className="mx-auto grid w-full max-w-7xl gap-6 lg:grid-cols-[1.15fr_0.85fr]">
          <section className="space-y-4 rounded-[30px] border border-white/80 bg-white/70 p-6 shadow-xl backdrop-blur">
            <div className="h-3 w-40 animate-pulse rounded-full bg-slate-200" />
            <div className="h-14 w-3/4 animate-pulse rounded-3xl bg-slate-200/80" />
            <div className="h-6 w-full animate-pulse rounded-full bg-slate-200/70" />
            <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
              {Array.from({ length: 4 }).map((_, index) => (
                <div key={index} className="h-28 animate-pulse rounded-3xl bg-slate-200/70" />
              ))}
            </div>
          </section>
          <aside className="space-y-4">
            <div className="h-72 animate-pulse rounded-[28px] bg-slate-200/70" />
            <div className="h-72 animate-pulse rounded-[28px] bg-slate-200/70" />
          </aside>
        </div>
      </main>
    );
  }

  return (
    <main className="fleet-console text-slate-900">
      <section className="relative mx-auto flex w-full max-w-7xl flex-col gap-3">
        <ConsoleRail
          eyebrow="Fleet · Cold Chain"
          icon={<FlaskConical className="h-3.5 w-3.5 text-teal-700" />}
          title="Cold Chain Monitor"
          meta={<>
            <span className="font-bold text-slate-700 tabular-nums">{devices.length}</span> temperature devices ·{" "}
            <span className="font-bold text-rose-600 tabular-nums">{alerts.length}</span> open alerts ·{" "}
            <span className="font-bold text-emerald-600 tabular-nums">{summary ? `${summary.summary.compliancePercent}%` : "—"}</span> compliance
          </>}
          actions={
            <Link to="/fleet-workspace" className="btn-ghost h-10">
              Fleet Workspace
            </Link>
          }
        />

        <div className="grid gap-3 lg:grid-cols-[1.15fr_0.85fr]">
          <div className="space-y-3">

            <section className="fc-neumo p-5">
              <div className="flex flex-wrap items-center justify-between gap-3">
                <div>
                  <p className="section-title">Guardrails</p>
                  <h2 className="mt-1 text-xl font-black text-slate-950">Temperature policies &amp; breach history</h2>
                </div>
                <span className="rounded-full bg-cyan-50 px-3 py-1 text-xs font-bold text-cyan-700">
                  {summary.summary.policyCount ?? summary.policies.length} policies
                </span>
              </div>
              <div className="mt-4 grid gap-3 sm:grid-cols-3">
                <div className="rounded-2xl border border-slate-200 bg-white p-4">
                  <p className="text-[11px] font-bold uppercase tracking-[0.24em] text-slate-400">Policies</p>
                  <p className="mt-2 text-2xl font-black text-slate-950">{summary.summary.policyCount ?? summary.policies.length}</p>
                </div>
                <div className="rounded-2xl border border-slate-200 bg-white p-4">
                  <p className="text-[11px] font-bold uppercase tracking-[0.24em] text-slate-400">Event log</p>
                  <p className="mt-2 text-2xl font-black text-slate-950">{summary.summary.eventLogCount ?? events.length}</p>
                </div>
                <div className="rounded-2xl border border-slate-200 bg-white p-4">
                  <p className="text-[11px] font-bold uppercase tracking-[0.24em] text-slate-400">Breach rate</p>
                  <p className="mt-2 text-2xl font-black text-slate-950">{summary.summary.totalReadings === 0 ? '0%' : `${Math.round((summary.summary.breachReadings / summary.summary.totalReadings) * 100)}%`}</p>
                </div>
              </div>
              <div className="mt-4 space-y-3">
                {summary.policies.slice(0, 3).map((policy) => (
                  <div key={policy.id} className="rounded-2xl border border-slate-200/80 bg-slate-50 p-4">
                    <div className="flex flex-wrap items-center justify-between gap-2">
                      <div>
                        <p className="font-bold text-slate-950">{policy.policyCode}</p>
                        <p className="text-sm text-slate-500">{policy.scopeType} · {policy.scopeKey || 'default scope'}</p>
                      </div>
                      <span className="rounded-full bg-cyan-50 px-3 py-1 text-xs font-bold text-cyan-700">{policy.severity}</span>
                    </div>
                    <p className="mt-2 text-sm text-slate-600">
                      {policy.minCelsius ?? '—'}°C to {policy.maxCelsius ?? '—'}°C · {policy.requiresAcknowledgement ? 'Acknowledgement required' : 'Auto-apply allowed'} · {policy.status}
                    </p>
                    {policy.notes ? <p className="mt-2 text-sm text-slate-500">{policy.notes}</p> : null}
                  </div>
                ))}
              </div>
            </section>

            <div className="grid grid-cols-2 gap-3 xl:grid-cols-4">
              {metrics.map((metric, i) => (
                <ClayStat key={metric.label} Icon={metric.icon}
                  tone={["fc-clay-sky", "fc-clay-teal", "fc-clay-red", "fc-clay-emerald"][i % 4]}
                  iconCls={["text-sky-700", "text-teal-700", "text-rose-700", "text-emerald-700"][i % 4]}
                  label={metric.label} value={metric.value}
                  alert={metric.label === "Open alerts"} />
              ))}
            </div>

            <div className="grid gap-6 xl:grid-cols-[1fr_0.95fr]">
              <section className="rounded-[28px] border border-white/75 bg-white/75 p-6 shadow-[0_24px_50px_rgba(15,23,42,0.08)] backdrop-blur">
                <div className="flex items-center justify-between">
                  <div>
                    <p className="text-xs font-bold uppercase tracking-[0.24em] text-slate-500">Temperature devices</p>
                    <h2 className="mt-2 text-2xl font-black text-slate-950">Operational sensors</h2>
                  </div>
                  <BatteryCharging className="h-5 w-5 text-emerald-500" />
                </div>
                <div className="mt-5 space-y-3">
                  {devices.slice(0, 5).map((device) => (
                    <div key={device.id} className="rounded-2xl border border-slate-200/80 bg-white/80 p-4">
                      <div className="flex flex-wrap items-center justify-between gap-2">
                        <div>
                          <p className="font-bold text-slate-950">{device.deviceCode} · {device.name}</p>
                          <p className="text-sm text-slate-500">{device.zoneCode || 'Unzoned'} · {device.vehicleNumber || 'No vehicle linked'}</p>
                        </div>
                        <span className="rounded-full bg-cyan-50 px-3 py-1 text-xs font-bold text-cyan-700">{device.status}</span>
                      </div>
                      <div className="mt-3 grid grid-cols-3 gap-3 text-sm text-slate-600">
                        <div>
                          <p className="text-[11px] uppercase tracking-[0.2em] text-slate-400">Temp</p>
                          <p className="font-bold text-slate-900">{device.lastReportedTemperatureCelsius.toFixed(1)}°C</p>
                        </div>
                        <div>
                          <p className="text-[11px] uppercase tracking-[0.2em] text-slate-400">Battery</p>
                          <p className="font-bold text-slate-900">{device.batteryPercent.toFixed(0)}%</p>
                        </div>
                        <button onClick={() => logReading(device.id)} className="rounded-full bg-slate-950 px-3 py-2 text-xs font-bold text-white transition hover:bg-slate-800">
                          Log reading
                        </button>
                      </div>
                    </div>
                  ))}
                </div>
              </section>

              <section className="rounded-[28px] border border-white/75 bg-white/75 p-6 shadow-[0_24px_50px_rgba(15,23,42,0.08)] backdrop-blur">
                <p className="text-xs font-bold uppercase tracking-[0.24em] text-slate-500">Control inputs</p>
                <h2 className="mt-2 text-2xl font-black text-slate-950">Add device and sample reading</h2>
                <div className="mt-5 space-y-4">
                  <div className="grid gap-4 sm:grid-cols-2">
                    <input value={form.deviceCode} onChange={(e) => setForm((current) => ({ ...current, deviceCode: e.target.value }))} placeholder="Device code" className="rounded-2xl border border-slate-200 bg-white px-4 py-3 outline-none transition focus:border-cyan-400" />
                    <input value={form.name} onChange={(e) => setForm((current) => ({ ...current, name: e.target.value }))} placeholder="Device name" className="rounded-2xl border border-slate-200 bg-white px-4 py-3 outline-none transition focus:border-cyan-400" />
                    <input value={form.vehicleNumber} onChange={(e) => setForm((current) => ({ ...current, vehicleNumber: e.target.value }))} placeholder="Vehicle number" className="rounded-2xl border border-slate-200 bg-white px-4 py-3 outline-none transition focus:border-cyan-400" />
                    <Select value={selectedZoneId} onChange={(e) => setSelectedZoneId(e.target.value)} className="rounded-2xl border border-slate-200 bg-white px-4 py-3 outline-none transition focus:border-cyan-400">
                      {summary.zones.map((zone) => <option key={zone.id} value={zone.id}>{zone.name}</option>)}
                    </Select>
                    <input value={form.temperature} onChange={(e) => setForm((current) => ({ ...current, temperature: e.target.value }))} placeholder="Temperature °C" className="rounded-2xl border border-slate-200 bg-white px-4 py-3 outline-none transition focus:border-cyan-400" />
                    <input value={form.batteryPercent} onChange={(e) => setForm((current) => ({ ...current, batteryPercent: e.target.value }))} placeholder="Battery %" className="rounded-2xl border border-slate-200 bg-white px-4 py-3 outline-none transition focus:border-cyan-400" />
                  </div>
                  <textarea value={form.notes} onChange={(e) => setForm((current) => ({ ...current, notes: e.target.value }))} rows={3} placeholder="Device notes" className="w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 outline-none transition focus:border-cyan-400" />
                  <textarea value={form.readingNotes} onChange={(e) => setForm((current) => ({ ...current, readingNotes: e.target.value }))} rows={3} placeholder="Reading notes" className="w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 outline-none transition focus:border-cyan-400" />
                  <button disabled={saving} onClick={createDevice} className="inline-flex w-full items-center justify-center rounded-2xl bg-gradient-to-r from-cyan-600 to-blue-600 px-4 py-3 font-bold text-white shadow-lg transition hover:from-cyan-500 hover:to-blue-500 disabled:opacity-60">
                    {saving ? 'Saving...' : 'Create device'}
                  </button>
                </div>
              </section>
            </div>
          </div>

          <aside className="space-y-6">
            <section className="rounded-[28px] border border-white/75 bg-slate-950/95 p-6 text-white shadow-[0_28px_60px_rgba(15,23,42,0.32)]">
              <div className="flex items-center justify-between">
                <div>
                  <p className="text-xs font-bold uppercase tracking-[0.24em] text-cyan-200/70">Zones</p>
                  <h2 className="mt-2 text-2xl font-black">Temperature bands</h2>
                </div>
                <Truck className="h-5 w-5 text-cyan-300" />
              </div>
              <div className="mt-5 space-y-3">
                {summary.zones.map((zone) => (
                  <div key={zone.id} className="rounded-2xl border border-white/10 bg-white/5 p-4">
                    <div className="flex items-center justify-between gap-3">
                      <div>
                        <p className="font-bold">{zone.name}</p>
                        <p className="text-sm text-slate-300">{zone.code} · {zone.minCelsius}°C to {zone.maxCelsius}°C</p>
                      </div>
                      <span className="h-3 w-3 rounded-full" style={{ backgroundColor: zone.color }} />
                    </div>
                    <p className="mt-2 text-sm text-slate-400">{zone.notes}</p>
                  </div>
                ))}
              </div>
            </section>

              <section className="rounded-[28px] border border-white/75 bg-white/80 p-6 shadow-[0_24px_50px_rgba(15,23,42,0.08)] backdrop-blur">
              <div className="flex items-center justify-between">
                <div>
                  <p className="text-xs font-bold uppercase tracking-[0.24em] text-slate-500">Alerts</p>
                  <h2 className="mt-2 text-2xl font-black text-slate-950">Open breaches</h2>
                </div>
                <BellRing className="h-5 w-5 text-rose-500" />
              </div>
              <div className="mt-5 space-y-3">
                {alerts.slice(0, 4).map((alert) => (
                  <div key={alert.id} className="rounded-2xl border border-slate-200 bg-white p-4">
                    <div className="flex items-center justify-between gap-2">
                      <div>
                        <p className="font-bold text-slate-950">{alert.alertType}</p>
                        <p className="text-sm text-slate-500">{alert.deviceCode || 'Sensor'} · {alert.shipmentNumber || 'No shipment'}</p>
                      </div>
                      <span className="rounded-full bg-rose-50 px-3 py-1 text-xs font-bold text-rose-700">{alert.severity}</span>
                    </div>
                    <p className="mt-2 text-sm text-slate-600">Measured {alert.measuredTemperature.toFixed(1)}°C against {alert.thresholdMin.toFixed(1)}°C to {alert.thresholdMax.toFixed(1)}°C.</p>
                    <div className="mt-3 flex items-center justify-between gap-3">
                      <input value={form.alertNotes} onChange={(e) => setForm((current) => ({ ...current, alertNotes: e.target.value }))} placeholder="Resolution notes" className="min-w-0 flex-1 rounded-full border border-slate-200 bg-white px-3 py-2 text-sm outline-none" />
                      <button onClick={() => resolveAlert(alert.id)} className="rounded-full bg-slate-950 px-3 py-2 text-xs font-bold text-white transition hover:bg-slate-800">
                        Resolve
                      </button>
                    </div>
                  </div>
                ))}
              </div>
              </section>

              <section className="rounded-[28px] border border-white/75 bg-white/80 p-6 shadow-[0_24px_50px_rgba(15,23,42,0.08)] backdrop-blur">
                <div className="flex items-center justify-between">
                  <div>
                    <p className="text-xs font-bold uppercase tracking-[0.24em] text-slate-500">Event log</p>
                    <h2 className="mt-2 text-2xl font-black text-slate-950">Recent policy and telemetry events</h2>
                  </div>
                  <Layers3 className="h-5 w-5 text-cyan-600" />
                </div>
                <div className="mt-5 space-y-3">
                  {events.length === 0 ? (
                    <div className="rounded-2xl border border-dashed border-slate-300 bg-white p-4 text-sm text-slate-500">
                      No cold-chain event log entries yet.
                    </div>
                  ) : (
                    events.map((event) => (
                      <div key={event.id} className="rounded-2xl border border-slate-200 bg-white p-4">
                        <div className="flex flex-wrap items-center justify-between gap-2">
                          <div>
                            <p className="font-bold text-slate-950">{event.eventType}</p>
                            <p className="text-sm text-slate-500">{event.aggregateType} · {event.aggregateId}</p>
                          </div>
                          <span className="rounded-full bg-slate-100 px-3 py-1 text-xs font-bold text-slate-700">{event.status}</span>
                        </div>
                        <p className="mt-2 text-sm text-slate-600">
                          {new Date(event.occurredAtUtc).toLocaleString()} {event.correlationId ? `· correlation ${event.correlationId}` : ''}
                        </p>
                      </div>
                    ))
                  )}
                </div>
              </section>

              <section className="rounded-[28px] border border-white/75 bg-white/80 p-6 shadow-[0_24px_50px_rgba(15,23,42,0.08)] backdrop-blur">
                <div className="flex items-center justify-between">
                  <div>
                    <p className="text-xs font-bold uppercase tracking-[0.24em] text-slate-500">Reports</p>
                    <h2 className="mt-2 text-2xl font-black text-slate-950">Shipment compliance</h2>
                </div>
                <Layers3 className="h-5 w-5 text-cyan-600" />
              </div>
              <div className="mt-5 space-y-3">
                {shipments.slice(0, 4).map((shipment) => (
                  <button key={shipment.id} onClick={() => generateReport(shipment.id)} className="w-full rounded-2xl border border-slate-200 bg-gradient-to-r from-white to-slate-50 p-4 text-left transition hover:border-cyan-300 hover:shadow-md">
                    <div className="flex items-center justify-between">
                      <p className="font-bold text-slate-950">{shipment.shipmentNumber}</p>
                      <span className="text-xs font-bold text-cyan-700">{shipment.mode}</span>
                    </div>
                    <p className="mt-1 text-sm text-slate-500">{shipment.customerName} · {shipment.status}</p>
                  </button>
                ))}
              </div>
            </section>
          </aside>
        </div>
      </section>
    </main>
  );
}

export default FleetColdChainPage;
