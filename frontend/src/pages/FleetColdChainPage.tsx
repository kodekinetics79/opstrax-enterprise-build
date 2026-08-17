import { useEffect, useMemo, useState } from 'react';
import { Link } from 'react-router';
import { BellRing, BatteryCharging, FlaskConical, Gauge, Layers3, RadioTower, Thermometer, Truck } from 'lucide-react';
import { ClayStat, ConsoleRail } from '@/components/console';
import { notifyApiError } from '@/services/fleetTmsApi';
import { fleetApi, fleetColdChainApi, type ColdChainEvent, type ColdChainSummaryResponse, type TemperatureAlert, type TemperatureDevice } from '@/services/fleetTmsApi';
import { useHasPermission } from '@/hooks/usePermission';

function formatMeasurement(value: unknown, digits: number, suffix: string, empty: string) {
  if (value === null || value === undefined || value === '') return empty;
  const numeric = Number(value);
  return Number.isFinite(numeric) ? `${numeric.toFixed(digits)}${suffix}` : empty;
}

export function FleetColdChainPage() {
  const hasPermission = useHasPermission();
  const canManageFleet = hasPermission('fleet:manage');
  const [summary, setSummary] = useState<ColdChainSummaryResponse | null>(null);
  const [devices, setDevices] = useState<TemperatureDevice[]>([]);
  const [alerts, setAlerts] = useState<TemperatureAlert[]>([]);
  const [shipments, setShipments] = useState<Array<{ id: string; shipmentNumber: string; status: string; customerName: string; mode: string }>>([]);
  const [events, setEvents] = useState<ColdChainEvent[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [shipmentWarning, setShipmentWarning] = useState<string | null>(null);
  const [selectedReadingDeviceId, setSelectedReadingDeviceId] = useState('');
  const [alertNotes, setAlertNotes] = useState<Record<string, string>>({});
  const [selectedShipmentId, setSelectedShipmentId] = useState('');
  const [selectedZoneId, setSelectedZoneId] = useState('');
  const [form, setForm] = useState({
    deviceCode: '',
    name: '',
    vehicleNumber: '',
    temperature: '',
    humidityPercent: '',
    notes: '',
    readingNotes: '',
  });

  // Core shipment workspace is tenant-wide. A branch-scoped operator can still
  // run standalone cold-chain devices safely when that endpoint returns 403.
  const loadShipmentOptions = async () => {
    try {
      setShipmentWarning(null);
      return await fleetApi.shipments({ pageSize: 8 });
    } catch {
      setShipmentWarning('Shipment options are unavailable for this operator scope. Standalone devices remain available.');
      return { items: [] };
    }
  };

  const refresh = async () => {
    const [summaryRes, devicesRes, alertsRes, shipmentsRes] = await Promise.all([
      fleetColdChainApi.summary(),
      fleetColdChainApi.devices(),
      fleetColdChainApi.alerts('Open'),
      loadShipmentOptions(),
    ]);
    setSummary(summaryRes);
    setDevices(devicesRes.items);
    setAlerts(alertsRes.items);
    setShipments(shipmentsRes.items as Array<{ id: string; shipmentNumber: string; status: string; customerName: string; mode: string }>);
    const eventsRes = await fleetColdChainApi.events();
    setEvents(eventsRes.items.slice(0, 6));
    setSelectedZoneId((current) => current || summaryRes.zones[0]?.id || '');
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
          fleetColdChainApi.alerts('Open'),
          loadShipmentOptions(),
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
          setError('Unable to load tenant cold-chain data. Retry the request or contact an administrator if access should be available.');
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
    if (!canManageFleet) return;
    if (!form.deviceCode.trim() || !form.name.trim() || !selectedZoneId) {
      setActionError('Device code, device name, and temperature zone are required.');
      return;
    }
    setActionError(null);
    setSaving(true);
    try {
      await fleetColdChainApi.createDevice({
        deviceCode: form.deviceCode,
        name: form.name,
        zoneId: selectedZoneId ? Number(selectedZoneId) : undefined,
        shipmentId: selectedShipmentId ? Number(selectedShipmentId) : undefined,
        vehicleNumber: form.vehicleNumber,
        notes: form.notes.trim() || undefined,
      });
      setForm((current) => ({ ...current, deviceCode: '', name: '', vehicleNumber: '', notes: '' }));
      setNotice('Cold-chain device registered. No reading was inferred during registration.');
      await refresh();
    } catch (err) {
      setActionError(err instanceof Error ? err.message : 'The device was not registered.');
      notifyApiError(err, 'Unable to create device.');
    } finally {
      setSaving(false);
    }
  };

  const logReading = async () => {
    if (!canManageFleet) return;
    const device = devices.find((item) => String(item.id) === selectedReadingDeviceId);
    const temperature = Number(form.temperature);
    const humidity = form.humidityPercent.trim() ? Number(form.humidityPercent) : null;
    if (!device || !Number.isFinite(temperature) || (humidity != null && (!Number.isFinite(humidity) || humidity < 0 || humidity > 100))) {
      setActionError('Select a device and enter a valid temperature. Optional humidity must be between 0 and 100%.');
      return;
    }
    setActionError(null);
    setSaving(true);
    try {
      await fleetColdChainApi.createReading({
        deviceId: Number(device.id),
        shipmentId: device.shipmentId ? Number(device.shipmentId) : undefined,
        zoneId: device.zoneId ? Number(device.zoneId) : undefined,
        temperatureCelsius: temperature,
        humidityPercent: humidity,
        source: 'Manual',
        sourceChannel: 'Operator console',
        notes: form.readingNotes.trim() || undefined,
      });
      setForm((current) => ({ ...current, temperature: '', humidityPercent: '', readingNotes: '' }));
      setNotice('Manual observation recorded. Temperature policy status is determined by the service.');
      await refresh();
    } catch (err) {
      setActionError(err instanceof Error ? err.message : 'The manual observation was not recorded.');
      notifyApiError(err, 'Unable to log temperature reading.');
    } finally {
      setSaving(false);
    }
  };

  const resolveAlert = async (alertId: string) => {
    if (!canManageFleet) return;
    const resolutionNotes = alertNotes[alertId]?.trim() ?? '';
    if (!resolutionNotes) {
      setActionError('Resolution notes are required so the alert audit trail records what was verified.');
      return;
    }
    setActionError(null);
    try {
      await fleetColdChainApi.resolveAlert(alertId, { resolutionNotes });
      setAlertNotes((current) => ({ ...current, [alertId]: '' }));
      setNotice('Alert resolved with the supplied audit notes.');
      await refresh();
    } catch (err) {
      setActionError(err instanceof Error ? err.message : 'The alert was not resolved.');
      notifyApiError(err, 'Unable to resolve alert.');
    }
  };

  const generateReport = async (shipmentId: string) => {
    if (!canManageFleet) return;
    try {
      await fleetColdChainApi.report(shipmentId);
      setNotice('Shipment compliance report generated from persisted readings.');
      await refresh();
    } catch (err) {
      setActionError(err instanceof Error ? err.message : 'The compliance report was not generated.');
      notifyApiError(err, 'Unable to generate report.');
    }
  };

  if (loading || !summary) {
    if (error) {
      return (
        <main className="min-h-screen bg-[linear-gradient(135deg,_#f8fbff_0%,_#e8f2ff_50%,_#eff6ff_100%)] px-6 py-8 text-slate-900">
          <section className="mx-auto flex w-full max-w-4xl flex-col gap-4 rounded-[30px] border border-rose-200 bg-white/85 p-8 shadow-xl backdrop-blur">
            <p className="text-xs font-bold uppercase tracking-[0.24em] text-rose-500">Cold chain workspace</p>
            <h1 className="text-3xl font-black tracking-tight text-slate-950">Cold-chain data is unavailable.</h1>
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
            <div className="flex flex-wrap gap-2">
              <Link to="/iot-devices" className="btn-ghost h-10"><RadioTower className="h-4 w-4" /> Device Health</Link>
              <Link to="/cold-chain" className="btn-ghost h-10"><Thermometer className="h-4 w-4" /> Telemetry View</Link>
              <Link to="/fleet-workspace" className="btn-ghost h-10">Fleet Workspace</Link>
            </div>
          }
        />

        {actionError ? <div role="alert" className="rounded-2xl border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-800">{actionError}</div> : null}
        {notice ? <div role="status" className="flex items-center justify-between gap-3 rounded-2xl border border-emerald-200 bg-emerald-50 px-4 py-3 text-sm text-emerald-900"><span>{notice}</span><button type="button" aria-label="Dismiss action message" onClick={() => setNotice(null)} className="font-bold">Dismiss</button></div> : null}
        {shipmentWarning ? <div role="status" className="rounded-2xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-900">{shipmentWarning}</div> : null}

        {!canManageFleet && (
          <div className="rounded-2xl border border-sky-200 bg-sky-50 px-4 py-3 text-sm text-sky-900" role="status">
            <p className="font-semibold">Read-only Cold Chain Monitor</p>
            <p className="mt-0.5 text-xs text-sky-800">Fleet manage permission is required to add sensors, record readings, resolve alerts, or generate compliance reports.</p>
          </div>
        )}

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
                    <p className="mt-1 text-xs text-slate-500">Humidity {policy.humidityMinPercent ?? '—'}% to {policy.humidityMaxPercent ?? '—'}%</p>
                    {policy.notes ? <p className="mt-2 text-sm text-slate-500">{policy.notes}</p> : null}
                    <details className="mt-2 text-xs text-slate-500"><summary className="cursor-pointer font-semibold">Policy audit details</summary><p className="mt-1">Source {policy.sourceChannel || 'Not reported'} · created {policy.createdAtUtc ? new Date(policy.createdAtUtc).toLocaleString() : 'unavailable'} · updated {policy.updatedAtUtc ? new Date(policy.updatedAtUtc).toLocaleString() : 'unavailable'}</p></details>
                  </div>
                ))}
                {!summary.policies.length ? <div className="rounded-2xl border border-dashed border-slate-300 bg-white p-4 text-sm text-slate-500">No cold-chain policies are configured for this tenant.</div> : null}
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
                  {!devices.length ? <div className="rounded-2xl border border-dashed border-slate-300 bg-white p-4 text-sm text-slate-500">No cold-chain devices are registered for this tenant.</div> : null}
                  {devices.map((device) => (
                    <div key={device.id} className="rounded-2xl border border-slate-200/80 bg-white/80 p-4">
                      <div className="flex flex-wrap items-center justify-between gap-2">
                        <div>
                          <p className="font-bold text-slate-950">{device.deviceCode} · {device.name}</p>
                          <p className="text-sm text-slate-500">{device.zoneCode || 'Unzoned'} · {device.vehicleNumber || 'No vehicle linked'}</p>
                        </div>
                        <span className="rounded-full bg-cyan-50 px-3 py-1 text-xs font-bold text-cyan-700">{device.status}</span>
                      </div>
                      <div className="mt-3 grid grid-cols-2 gap-3 text-sm text-slate-600 sm:grid-cols-4">
                        <div>
                          <p className="text-[11px] uppercase tracking-[0.2em] text-slate-400">Temp</p>
                          <p className="font-bold text-slate-900">{formatMeasurement(device.lastReportedTemperatureCelsius, 1, '°C', 'No reading')}</p>
                        </div>
                        <div>
                          <p className="text-[11px] uppercase tracking-[0.2em] text-slate-400">Battery</p>
                          <p className="font-bold text-slate-900">{formatMeasurement(device.batteryPercent, 0, '%', 'Not reported')}</p>
                        </div>
                        <div><p className="text-[11px] uppercase tracking-[0.2em] text-slate-400">Last report</p><p className="font-bold text-slate-900">{device.lastPingAtUtc ? new Date(device.lastPingAtUtc).toLocaleString() : 'Never reported'}</p></div>
                        <button onClick={() => setSelectedReadingDeviceId(String(device.id))} disabled={!canManageFleet} title={canManageFleet ? 'Select this device for an operator-entered reading' : 'Requires fleet manage permission'} className="rounded-full bg-slate-950 px-3 py-2 text-xs font-bold text-white transition hover:bg-slate-800 disabled:cursor-not-allowed disabled:opacity-50">
                          {selectedReadingDeviceId === String(device.id) ? 'Selected' : 'Record manually'}
                        </button>
                      </div>
                      <dl className="mt-3 grid gap-2 border-t border-slate-100 pt-3 text-xs text-slate-600 sm:grid-cols-2">
                        <div><dt className="font-semibold text-slate-500">Shipment</dt><dd>{device.shipmentNumber || 'Not linked'}</dd></div>
                        <div><dt className="font-semibold text-slate-500">Source channel</dt><dd>{device.sourceChannel || 'Not reported'}</dd></div>
                        <div><dt className="font-semibold text-slate-500">Created</dt><dd>{device.createdAtUtc ? new Date(device.createdAtUtc).toLocaleString() : 'Unavailable'}</dd></div>
                        <div><dt className="font-semibold text-slate-500">Updated</dt><dd>{device.updatedAtUtc ? new Date(device.updatedAtUtc).toLocaleString() : 'Unavailable'}</dd></div>
                      </dl>
                      {device.notes ? <p className="mt-3 text-sm text-slate-500">{device.notes}</p> : null}
                      <details className="mt-3 border-t border-slate-100 pt-3 text-xs text-slate-500">
                        <summary className="cursor-pointer font-semibold">Integration and audit details</summary>
                        <dl className="mt-2 grid gap-2 sm:grid-cols-2">
                          <div><dt className="font-semibold">Device reference</dt><dd>{device.id}</dd></div>
                          <div><dt className="font-semibold">Zone reference</dt><dd>{device.zoneId || 'Not linked'}</dd></div>
                          <div><dt className="font-semibold">Shipment reference</dt><dd>{device.shipmentId || 'Not linked'}</dd></div>
                          <div><dt className="font-semibold">Client reference</dt><dd>{device.clientGeneratedId || 'Not reported'}</dd></div>
                          <div><dt className="font-semibold">Correlation</dt><dd>{device.correlationId || 'Not reported'}</dd></div>
                          <div><dt className="font-semibold">Causation</dt><dd>{device.causationId || 'Not reported'}</dd></div>
                          <div><dt className="font-semibold">Idempotency</dt><dd>{device.idempotencyKey || 'Not reported'}</dd></div>
                          <div><dt className="font-semibold">Metadata</dt><dd className="break-all">{device.metadataJson || 'Not reported'}</dd></div>
                        </dl>
                      </details>
                    </div>
                  ))}
                </div>
              </section>

              <section className="rounded-[28px] border border-white/75 bg-white/75 p-6 shadow-[0_24px_50px_rgba(15,23,42,0.08)] backdrop-blur">
                <p className="text-xs font-bold uppercase tracking-[0.24em] text-slate-500">Control inputs</p>
                <h2 className="mt-2 text-2xl font-black text-slate-950">Register a device</h2>
                <div className="mt-5 space-y-4">
                  <div className="grid gap-4 sm:grid-cols-2">
                    <label className="text-sm font-semibold text-slate-700">Device code<input value={form.deviceCode} onChange={(e) => setForm((current) => ({ ...current, deviceCode: e.target.value }))} className="mt-1 w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 outline-none transition focus:border-cyan-400" required /></label>
                    <label className="text-sm font-semibold text-slate-700">Device name<input value={form.name} onChange={(e) => setForm((current) => ({ ...current, name: e.target.value }))} className="mt-1 w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 outline-none transition focus:border-cyan-400" required /></label>
                    <label className="text-sm font-semibold text-slate-700">Vehicle number (optional)<input value={form.vehicleNumber} onChange={(e) => setForm((current) => ({ ...current, vehicleNumber: e.target.value }))} className="mt-1 w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 outline-none transition focus:border-cyan-400" /></label>
                    <label className="text-sm font-semibold text-slate-700">Temperature zone<select value={selectedZoneId} onChange={(e) => setSelectedZoneId(e.target.value)} className="mt-1 w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 outline-none transition focus:border-cyan-400" required>
                      <option value="">Select a zone</option>
                      {summary.zones.map((zone) => <option key={zone.id} value={zone.id}>{zone.name}</option>)}
                    </select></label>
                  </div>
                  <textarea value={form.notes} onChange={(e) => setForm((current) => ({ ...current, notes: e.target.value }))} rows={3} placeholder="Device notes" className="w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 outline-none transition focus:border-cyan-400" />
                  <button disabled={!canManageFleet || saving} onClick={createDevice} title={canManageFleet ? undefined : 'Requires fleet manage permission'} className="inline-flex w-full items-center justify-center rounded-2xl bg-gradient-to-r from-cyan-600 to-blue-600 px-4 py-3 font-bold text-white shadow-lg transition hover:from-cyan-500 hover:to-blue-500 disabled:opacity-60">
                    {saving ? 'Saving...' : 'Create device'}
                  </button>
                  <div className="border-t border-slate-200 pt-4">
                    <p className="text-xs font-bold uppercase tracking-[0.2em] text-slate-500">Operator-entered reading</p>
                    <p className="mt-1 text-sm text-slate-600">Saved with Manual provenance. Policy status is derived by the service; this form does not claim a sensor measurement.</p>
                    <div className="mt-3 grid gap-3 sm:grid-cols-2">
                      <label className="text-sm font-semibold text-slate-700">Temperature °C<input type="number" step="0.1" value={form.temperature} onChange={(e) => setForm((current) => ({ ...current, temperature: e.target.value }))} className="mt-1 w-full rounded-2xl border border-slate-200 bg-white px-4 py-3" /></label>
                      <label className="text-sm font-semibold text-slate-700">Humidity % (optional)<input type="number" min="0" max="100" step="0.1" value={form.humidityPercent} onChange={(e) => setForm((current) => ({ ...current, humidityPercent: e.target.value }))} className="mt-1 w-full rounded-2xl border border-slate-200 bg-white px-4 py-3" /></label>
                    </div>
                    <label className="mt-3 block text-sm font-semibold text-slate-700">Observation notes (optional)<textarea value={form.readingNotes} onChange={(e) => setForm((current) => ({ ...current, readingNotes: e.target.value }))} rows={2} className="mt-1 w-full rounded-2xl border border-slate-200 bg-white px-4 py-3" /></label>
                    <button type="button" onClick={logReading} disabled={!canManageFleet || !selectedReadingDeviceId || saving} className="mt-3 w-full rounded-2xl bg-slate-950 px-4 py-3 font-bold text-white disabled:opacity-50">Record manual reading{selectedReadingDeviceId ? ` for ${devices.find((device) => String(device.id) === selectedReadingDeviceId)?.deviceCode ?? 'selected device'}` : ''}</button>
                  </div>
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
                    <p className="mt-2 text-sm text-slate-400">{zone.notes || 'No zone notes'} · {zone.isActive ? 'Active' : 'Inactive'}</p>
                  </div>
                ))}
                {!summary.zones.length ? <div className="rounded-2xl border border-dashed border-white/20 p-4 text-sm text-slate-300">No temperature zones are configured.</div> : null}
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
                {!alerts.length ? <div className="rounded-2xl border border-dashed border-slate-300 bg-white p-4 text-sm text-slate-500">No open temperature alerts.</div> : null}
                {alerts.slice(0, 4).map((alert) => (
                  <div key={alert.id} className="rounded-2xl border border-slate-200 bg-white p-4">
                    <div className="flex items-center justify-between gap-2">
                      <div>
                        <p className="font-bold text-slate-950">{alert.alertType}</p>
                        <p className="text-sm text-slate-500">{alert.deviceCode || 'Sensor'} · {alert.shipmentNumber || 'No shipment'}</p>
                      </div>
                      <span className="rounded-full bg-rose-50 px-3 py-1 text-xs font-bold text-rose-700">{alert.severity}</span>
                    </div>
                    <p className="mt-2 text-sm text-slate-600">Measured {formatMeasurement(alert.measuredTemperature, 1, '°C', 'temperature unavailable')} against {formatMeasurement(alert.thresholdMin, 1, '°C', 'no minimum')} to {formatMeasurement(alert.thresholdMax, 1, '°C', 'no maximum')}.</p>
                    <p className="mt-1 text-xs text-slate-500">Triggered {alert.triggeredAtUtc ? new Date(alert.triggeredAtUtc).toLocaleString() : 'at an unavailable time'} · {alert.status}</p>
                    {alert.notes ? <p className="mt-2 text-sm text-slate-500">{alert.notes}</p> : null}
                    <p className="mt-1 text-xs text-slate-500">Reading reference {alert.readingId || 'Unavailable'}</p>
                    <div className="mt-3 flex items-center justify-between gap-3">
                      <label className="min-w-0 flex-1 text-xs font-semibold text-slate-600">Resolution notes<input value={alertNotes[alert.id] ?? ''} onChange={(e) => setAlertNotes((current) => ({ ...current, [alert.id]: e.target.value }))} className="mt-1 w-full rounded-full border border-slate-200 bg-white px-3 py-2 text-sm font-normal outline-none" required /></label>
                      <button onClick={() => resolveAlert(alert.id)} disabled={!canManageFleet} title={canManageFleet ? undefined : 'Requires fleet manage permission'} className="rounded-full bg-slate-950 px-3 py-2 text-xs font-bold text-white transition hover:bg-slate-800 disabled:cursor-not-allowed disabled:opacity-50">
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
                        {event.processedAtUtc ? <p className="mt-1 text-xs text-slate-500">Processed {new Date(event.processedAtUtc).toLocaleString()}</p> : null}
                        {event.errorMessage ? <p className="mt-2 text-sm text-rose-700">{event.errorMessage}</p> : null}
                        <details className="mt-2 text-xs text-slate-500"><summary className="cursor-pointer font-semibold">Event evidence</summary><p className="mt-1">Retry count {event.retryCount ?? 0} · causation {event.causationId || 'not reported'} · idempotency {event.idempotencyKey || 'not reported'}</p><p className="mt-1 break-all">Payload {event.payloadJson || 'not reported'}</p></details>
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
                {!shipments.length ? <div className="rounded-2xl border border-dashed border-slate-300 bg-white p-4 text-sm text-slate-500">No accessible shipments are available for report generation.</div> : null}
                {shipments.slice(0, 4).map((shipment) => (
                  <button key={shipment.id} onClick={() => generateReport(shipment.id)} disabled={!canManageFleet} title={canManageFleet ? undefined : 'Requires fleet manage permission'} className="w-full rounded-2xl border border-slate-200 bg-gradient-to-r from-white to-slate-50 p-4 text-left transition hover:border-cyan-300 hover:shadow-md disabled:cursor-not-allowed disabled:opacity-60">
                    <div className="flex items-center justify-between">
                      <p className="font-bold text-slate-950">{shipment.shipmentNumber}</p>
                      <span className="text-xs font-bold text-cyan-700">{shipment.mode}</span>
                    </div>
                    <p className="mt-1 text-sm text-slate-500">{shipment.customerName} · {shipment.status}</p>
                  </button>
                ))}
                {summary.reports.length ? (
                  <div className="border-t border-slate-200 pt-4">
                    <p className="mb-3 text-xs font-bold uppercase tracking-[0.2em] text-slate-500">Generated reports</p>
                    {summary.reports.slice(0, 4).map((report) => (
                      <div key={report.id} className="mb-2 rounded-2xl border border-slate-200 bg-white p-4 text-sm">
                        <div className="flex justify-between gap-3"><strong>{report.shipmentNumber || 'Shipment report'}</strong><span>{report.compliancePercent}% compliant</span></div>
                        <p className="mt-1 text-slate-500">{report.totalReadings} readings · {report.breachCount} breaches · generated {report.generatedAtUtc ? new Date(report.generatedAtUtc).toLocaleString() : 'time unavailable'}</p>
                        <p className="mt-1 text-slate-500">Observed range {formatMeasurement(report.minTemperatureCelsius, 1, '°C', 'unavailable')} to {formatMeasurement(report.maxTemperatureCelsius, 1, '°C', 'unavailable')}</p>
                        {report.notes ? <p className="mt-1 text-slate-500">{report.notes}</p> : null}
                        <details className="mt-2 text-xs text-slate-500"><summary className="cursor-pointer font-semibold">Report evidence</summary><p className="mt-1 break-all">{report.summaryJson || 'No summary payload reported'}</p></details>
                      </div>
                    ))}
                  </div>
                ) : <div className="rounded-2xl border border-dashed border-slate-300 bg-white p-4 text-sm text-slate-500">No compliance reports have been generated.</div>}
              </div>
            </section>
          </aside>
        </div>
      </section>
    </main>
  );
}

export default FleetColdChainPage;
