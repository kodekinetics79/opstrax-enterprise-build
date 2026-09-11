import { useEffect, useMemo, useState } from 'react';
import { Link } from 'react-router';
import { Archive, Barcode, Boxes, CheckCheck, ChevronLeft, ChevronRight, Search, Truck } from 'lucide-react';
import { ConsoleRail } from '@/components/console';
import { notifyApiError } from '@/services/fleetTmsApi';
import { fleetApi, fleetAssetApi, type Asset, type AssetAssignment, type AssetEvent, type AssetType } from '@/services/fleetTmsApi';
import { LoadingState } from '@/components/ui';
import { EntityImportExport } from '@/components/EntityImportExport';
import { PERMISSIONS, useHasPermission } from '@/hooks/usePermission';

type AssetDetail = {
  asset: Asset;
  assignments: AssetAssignment[];
  events: AssetEvent[];
};

export function FleetAssetManagementPage() {
  const hasPermission = useHasPermission();
  const canManageFleet = hasPermission(PERMISSIONS.FLEET_MANAGE);
  const [assetTypes, setAssetTypes] = useState<AssetType[]>([]);
  const [assets, setAssets] = useState<Asset[]>([]);
  const [assetTotal, setAssetTotal] = useState(0);
  const [assetSummary, setAssetSummary] = useState({ assigned: 0, available: 0, needsReview: 0 });
  const [assetPage, setAssetPage] = useState(1);
  const [assetSearch, setAssetSearch] = useState('');
  const [assetSort, setAssetSort] = useState<'assetTag' | 'name' | 'status' | 'location' | 'condition' | 'type' | 'lastSeen'>('assetTag');
  const [assetDirection, setAssetDirection] = useState<'asc' | 'desc'>('asc');
  const [shipments, setShipments] = useState<Array<{ id: string; shipmentNumber: string; customerName: string; status: string }>>([]);
  const [detail, setDetail] = useState<AssetDetail | null>(null);
  const [selectedAssetId, setSelectedAssetId] = useState('');
  const [selectedTypeId, setSelectedTypeId] = useState('');
  const [selectedShipmentId, setSelectedShipmentId] = useState('');
  const [loading, setLoading] = useState(true);
  const [loadWarnings, setLoadWarnings] = useState<string[]>([]);
  const [saving, setSaving] = useState(false);
  const [actionMessage, setActionMessage] = useState('');
  const [forms, setForms] = useState({
    typeCode: '',
    typeName: '',
    typeDescription: '',
    assetTag: '',
    assetName: '',
    assetLocation: 'Main Warehouse',
    assetCondition: 'Good',
    assetQuantity: '1',
    scanValue: '',
    scanNotes: '',
    assignName: '',
    assignLocation: '',
    movementNotes: '',
  });

  const loadWorkspaceData = async ({ showLoading = false }: { showLoading?: boolean } = {}) => {
    if (showLoading) setLoading(true);

    const warnings: string[] = [];
    const [typesRes, assetsRes, shipmentsRes] = await Promise.allSettled([
      fleetAssetApi.assetTypes(),
      fleetAssetApi.assets({ page: assetPage, pageSize: 100, search: assetSearch, sort: assetSort, direction: assetDirection }),
      fleetApi.shipments({ pageSize: 8 }),
    ]);

    const apply = <T,>(result: PromiseSettledResult<T>, label: string, setter: (value: T) => void) => {
      if (result.status === 'fulfilled') {
        setter(result.value);
        return;
      }
      warnings.push(`${label} could not load (${result.reason instanceof Error ? result.reason.message : 'request failed'}).`);
    };

    apply(typesRes, 'Asset types', (value) => setAssetTypes(value.items));
    apply(assetsRes, 'Assets', (value) => {
      setAssets(value.items);
      setAssetTotal(value.total);
      setAssetSummary(value.summary);
    });
    apply(shipmentsRes, 'Shipments', (value) => setShipments(value.items as Array<{ id: string; shipmentNumber: string; customerName: string; status: string }>));

    if (typesRes.status === 'fulfilled' && typesRes.value.items.length && !selectedTypeId) {
      setSelectedTypeId(typesRes.value.items[0].id ?? '');
    }
    if (shipmentsRes.status === 'fulfilled' && shipmentsRes.value.items.length && !selectedShipmentId) {
      setSelectedShipmentId(shipmentsRes.value.items[0].id ?? '');
    }

    const currentSelectedAssetId = selectedAssetId || (assetsRes.status === 'fulfilled' ? assetsRes.value.items[0]?.id : '') || '';
    if (currentSelectedAssetId) {
      try {
        setSelectedAssetId(currentSelectedAssetId);
        const selected = await fleetAssetApi.asset(currentSelectedAssetId);
        setDetail(selected);
      } catch (err) {
        warnings.push(`Selected asset details could not load (${err instanceof Error ? err.message : 'request failed'}).`);
      }
    } else {
      setDetail(null);
    }

    setLoadWarnings(warnings);
    if (showLoading) setLoading(false);
  };

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        await loadWorkspaceData({ showLoading: true });
        if (cancelled) return;
      } catch (err) {
        if (!cancelled) notifyApiError(err, 'Unable to load asset management.');
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    let cancelled = false;
    const timer = window.setTimeout(() => {
      void fleetAssetApi.assets({ page: assetPage, pageSize: 100, search: assetSearch, sort: assetSort, direction: assetDirection })
        .then((result) => {
          if (cancelled) return;
          setAssets(result.items);
          setAssetTotal(result.total);
          setAssetSummary(result.summary);
        })
        .catch((error) => { if (!cancelled) notifyApiError(error, 'Unable to load the requested asset page.'); });
    }, 250);
    return () => {
      cancelled = true;
      window.clearTimeout(timer);
    };
  }, [assetDirection, assetPage, assetSearch, assetSort]);

  const refresh = async () => {
    await loadWorkspaceData();
  };

  useEffect(() => {
    if (!selectedAssetId) return;
    let cancelled = false;
    (async () => {
      try {
        const selected = await fleetAssetApi.asset(selectedAssetId);
        if (!cancelled) setDetail(selected);
      } catch (err) {
        if (!cancelled) notifyApiError(err, 'Unable to open asset details.');
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [selectedAssetId]);

  const metrics = useMemo(() => {
    return [
      { label: 'Asset types', value: assetTypes.length, tone: 'text-cyan-700' },
      { label: 'Assigned', value: assetSummary.assigned, tone: 'text-blue-700' },
      { label: 'Available', value: assetSummary.available, tone: 'text-emerald-700' },
      { label: 'Needs review', value: assetSummary.needsReview, tone: 'text-amber-700' },
    ];
  }, [assetSummary, assetTypes]);

  const assetPageCount = Math.max(1, Math.ceil(assetTotal / 100));

  const createAssetType = async () => {
    if (!canManageFleet) return;
    setSaving(true);
    try {
      await fleetAssetApi.createAssetType({
        code: forms.typeCode,
        name: forms.typeName,
        description: forms.typeDescription,
        isReturnable: true,
      });
      setForms((current) => ({ ...current, typeCode: '', typeName: '', typeDescription: '' }));
      await refresh();
    } catch (err) {
      notifyApiError(err, 'Unable to create asset type.');
    } finally {
      setSaving(false);
    }
  };

  const createAsset = async () => {
    if (!canManageFleet || !selectedTypeId) return;
    setSaving(true);
    try {
      await fleetAssetApi.createAsset({
        assetTypeId: selectedTypeId,
        assetTag: forms.assetTag,
        name: forms.assetName,
        status: 'Available',
        currentLocation: forms.assetLocation,
        condition: forms.assetCondition,
        quantity: Number(forms.assetQuantity),
        unitOfMeasure: 'Each',
        notes: 'Created from the asset management console.',
      });
      setForms((current) => ({ ...current, assetTag: '', assetName: '' }));
      await refresh();
    } catch (err) {
      notifyApiError(err, 'Unable to create asset.');
    } finally {
      setSaving(false);
    }
  };

  const scan = async () => {
    if (!canManageFleet) return;
    const scannedValue = forms.scanValue.trim();
    if (!scannedValue) {
      setActionMessage('Enter or scan a barcode value before capturing the event.');
      return;
    }
    setActionMessage('');
    try {
      await fleetAssetApi.scan({
        kind: 'Barcode',
        assetId: selectedAssetId || undefined,
        shipmentId: selectedShipmentId || undefined,
        scannedValue,
        scannerId: 'WEB-SCAN-01',
        eventType: 'Scan',
        status: 'Captured',
        notes: forms.scanNotes,
      });
      setForms((current) => ({ ...current, scanValue: '', scanNotes: '' }));
      await refresh();
    } catch (err) {
      notifyApiError(err, 'Unable to capture scan.');
    }
  };

  const assign = async (mode: 'checkIn' | 'checkOut') => {
    if (!canManageFleet || !selectedAssetId) return;
    try {
      if (mode === 'checkIn') {
        await fleetAssetApi.checkInAsset(selectedAssetId, {
          location: forms.assignLocation || 'Main Warehouse',
          condition: forms.assetCondition,
          notes: forms.movementNotes,
          shipmentId: selectedShipmentId || undefined,
          assigneeName: forms.assignName || 'Warehouse',
        });
      } else {
        await fleetAssetApi.checkOutAsset(selectedAssetId, {
          location: forms.assignLocation || 'Dispatch Yard',
          condition: forms.assetCondition,
          notes: forms.movementNotes,
          shipmentId: selectedShipmentId || undefined,
          assigneeName: forms.assignName || (shipments.find((s) => s.id === selectedShipmentId)?.shipmentNumber ?? 'Dispatch'),
        });
      }
      await refresh();
    } catch (err) {
      notifyApiError(err, `Unable to ${mode === 'checkIn' ? 'check in' : 'check out'} asset.`);
    }
  };

  if (loading) return <LoadingState />;

  return (
    <div className="fleet-console page-stack text-slate-900">
        <ConsoleRail
          eyebrow="Fleet · Returnable Assets"
          icon={<Boxes className="h-3.5 w-3.5 text-teal-700" />}
          title="Returnable Assets"
          meta={<>
            <span className="font-bold text-slate-700 tabular-nums">{assetTotal}</span> assets in custody ·{" "}
            <span className="font-bold text-emerald-600 tabular-nums">{assetSummary.available}</span> available ·{" "}
            <span className="font-bold text-amber-600 tabular-nums">{assetSummary.needsReview}</span> need review
          </>}
          actions={
            <>
              <EntityImportExport
                canImport={canManageFleet}
                canExport={canManageFleet}
                config={{
                  entity: 'assets',
                  columns: ['assetTag', 'branchCode', 'name', 'assetTypeCode', 'status', 'currentLocation', 'condition', 'isReturnable', 'quantity', 'unitOfMeasure', 'notes'],
                  requiredColumns: ['assetTag', 'name', 'assetTypeCode'],
                  templateEndpoint: '/api/fleet-tms/assets/import-template',
                  exportEndpoint: '/api/fleet-tms/assets/export',
                  importPreview: fleetAssetApi.previewImport,
                  importCommit: fleetAssetApi.commitImport,
                  invalidateKey: 'fleet-assets',
                  onImported: refresh,
                }}
              />
              <Link to="/fleet-workspace" className="btn-ghost btn-compact">Fleet Workspace</Link>
            </>
          }
        />

        {!canManageFleet && (
          <div className="rounded-2xl border border-sky-200 bg-sky-50 px-4 py-3 text-sm text-sky-900" role="status">
            <p className="font-semibold">Read-only Returnable Assets</p>
            <p className="mt-0.5 text-xs text-sky-800">Fleet manage permission is required to create inventory, capture scans, or change asset custody.</p>
          </div>
        )}

        {loadWarnings.length ? (
          <div className="rounded-xl border border-amber-200 bg-amber-50 px-3 py-2 text-sm text-amber-900">
            <p className="font-semibold">Some asset workspace sections were not available.</p>
            <ul className="mt-1 space-y-1 text-xs text-amber-800">
              {loadWarnings.slice(0, 3).map((warning) => <li key={warning}>• {warning}</li>)}
            </ul>
          </div>
        ) : null}

        <section className="panel p-3" aria-label="Asset inventory summary">
          <dl className="grid grid-cols-2 gap-2 lg:grid-cols-4">
            {metrics.map((metric) => (
              <div key={metric.label} className="min-w-0 rounded-xl border border-slate-200 bg-slate-50/70 px-3 py-2">
                <dt className="text-[10px] font-bold uppercase tracking-[0.12em] text-slate-500">{metric.label}</dt>
                <dd className={`mt-0.5 text-lg font-bold leading-none tabular-nums ${metric.tone}`}>{metric.value}</dd>
              </div>
            ))}
          </dl>
        </section>

        <div className="grid gap-3 xl:grid-cols-[minmax(0,1.35fr)_minmax(22rem,0.65fr)]">
          <section className="min-w-0 rounded-2xl border border-slate-800 bg-slate-950 p-3 text-white shadow-lg" aria-labelledby="asset-inventory-heading">
            <div className="flex items-center justify-between gap-3">
              <div>
                <p className="text-[10px] font-bold uppercase tracking-[0.16em] text-cyan-200/70">Assets</p>
                <h2 id="asset-inventory-heading" className="mt-0.5 text-lg font-bold">Inventory list</h2>
              </div>
              <Archive className="h-4 w-4 text-cyan-300" aria-hidden="true" />
            </div>
            <div className="mt-3 grid gap-2 sm:grid-cols-[minmax(0,1fr)_auto_auto]">
                <label className="relative">
                  <span className="sr-only">Search returnable assets</span>
                  <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
                  <input
                    value={assetSearch}
                    onChange={(event) => { setAssetSearch(event.target.value); setAssetPage(1); }}
                    placeholder="Search tag, name, type, status, location…"
                    className="min-h-11 w-full rounded-xl border border-white/15 bg-white/10 py-2 pl-9 pr-3 text-sm text-white placeholder:text-slate-400 sm:min-h-9"
                  />
                </label>
                <select
                  aria-label="Sort assets"
                  value={assetSort}
                  onChange={(event) => { setAssetSort(event.target.value as typeof assetSort); setAssetPage(1); }}
                  className="min-h-11 w-full rounded-xl border border-white/15 bg-slate-900 px-3 py-2 text-sm text-white sm:min-h-9 sm:w-auto"
                >
                  <option value="assetTag">Asset tag</option>
                  <option value="name">Name</option>
                  <option value="type">Type</option>
                  <option value="status">Status</option>
                  <option value="location">Location</option>
                  <option value="condition">Condition</option>
                  <option value="lastSeen">Last seen</option>
                </select>
                <button type="button" className="min-h-11 w-full rounded-xl border border-white/15 bg-white/10 px-3 py-2 text-sm font-semibold sm:min-h-9 sm:w-auto" onClick={() => { setAssetDirection((current) => current === 'asc' ? 'desc' : 'asc'); setAssetPage(1); }}>
                  {assetDirection === 'asc' ? 'Ascending' : 'Descending'}
                </button>
            </div>
            <div className="mt-3 space-y-2 xl:max-h-[34rem] xl:overflow-y-auto xl:pr-1">
                {assets.map((asset) => (
                <button key={asset.id} onClick={() => setSelectedAssetId(asset.id)} className={`min-h-11 w-full rounded-xl border p-3 text-left transition ${selectedAssetId === asset.id ? 'border-cyan-300 bg-white/10' : 'border-white/10 bg-white/5 hover:bg-white/8'}`}>
                    <div className="flex items-center justify-between gap-3">
                      <div>
                        <p className="font-bold">{asset.assetTag}</p>
                        <p className="text-sm text-slate-300">{asset.name} · {asset.currentLocation}</p>
                      </div>
                      <span className="rounded-full bg-white/10 px-2 py-0.5 text-xs font-bold">{asset.status}</span>
                    </div>
                  </button>
                ))}
            </div>
            <div className="mt-3 flex items-center justify-between gap-3 border-t border-white/10 pt-3 text-xs text-slate-300">
                <span>Page {assetPage} of {assetPageCount} · {assets.length} shown · {assetTotal} total</span>
                <div className="flex gap-2">
                <button type="button" aria-label="Previous asset page" disabled={assetPage <= 1} className="grid min-h-11 min-w-11 place-items-center rounded-lg border border-white/15 disabled:opacity-40 sm:min-h-8 sm:min-w-8" onClick={() => setAssetPage((current) => Math.max(1, current - 1))}><ChevronLeft className="h-4 w-4" /></button>
                <button type="button" aria-label="Next asset page" disabled={assetPage >= assetPageCount} className="grid min-h-11 min-w-11 place-items-center rounded-lg border border-white/15 disabled:opacity-40 sm:min-h-8 sm:min-w-8" onClick={() => setAssetPage((current) => Math.min(assetPageCount, current + 1))}><ChevronRight className="h-4 w-4" /></button>
                </div>
            </div>
          </section>

          <aside className="space-y-3">
            <section className="panel p-3">
              <p className="text-[10px] font-bold uppercase tracking-[0.16em] text-slate-500">Selected asset</p>
              {detail ? (
                <div className="mt-2 space-y-3">
                  <div>
                    <p className="text-lg font-bold text-slate-950">{detail.asset.assetTag}</p>
                    <p className="text-sm text-slate-500">{detail.asset.name} · {detail.asset.currentLocation}</p>
                  </div>
                  <div className="grid grid-cols-2 gap-2 text-sm">
                    <div className="rounded-xl bg-slate-50 p-2">
                      <p className="text-[10px] uppercase tracking-[0.12em] text-slate-400">Status</p>
                      <p className="font-bold text-slate-900">{detail.asset.status}</p>
                    </div>
                    <div className="rounded-xl bg-slate-50 p-2">
                      <p className="text-[10px] uppercase tracking-[0.12em] text-slate-400">Condition</p>
                      <p className="font-bold text-slate-900">{detail.asset.condition}</p>
                    </div>
                    <div className="rounded-xl bg-slate-50 p-2">
                      <p className="text-[10px] uppercase tracking-[0.12em] text-slate-400">Quantity</p>
                      <p className="font-bold text-slate-900">{detail.asset.quantity}</p>
                    </div>
                    <div className="rounded-xl bg-slate-50 p-2">
                      <p className="text-[10px] uppercase tracking-[0.12em] text-slate-400">Assignments</p>
                      <p className="font-bold text-slate-900">{detail.assignments.length}</p>
                    </div>
                  </div>

                  <div>
                    <p className="text-[10px] font-bold uppercase tracking-[0.16em] text-slate-500">Assignments</p>
                    <div className="mt-2 space-y-2">
                      {detail.assignments.slice(0, 3).map((assignment) => (
                        <div key={assignment.id} className="rounded-xl border border-slate-200 bg-white p-2 text-sm">
                          <div className="flex items-center justify-between">
                            <p className="font-bold text-slate-950">{assignment.assigneeType}</p>
                            <span className="text-xs font-bold text-cyan-700">{assignment.status}</span>
                          </div>
                          <p className="text-slate-500">{assignment.assigneeName}</p>
                        </div>
                      ))}
                    </div>
                  </div>

                  <div>
                    <p className="text-[10px] font-bold uppercase tracking-[0.16em] text-slate-500">Events</p>
                    <div className="mt-2 space-y-2">
                      {detail.events.slice(0, 4).map((event) => (
                        <div key={event.id} className="rounded-xl border border-slate-200 bg-slate-50 p-2 text-sm">
                          <div className="flex items-center justify-between">
                            <p className="font-bold text-slate-950">{event.type}</p>
                            <span className="text-xs text-slate-500">{event.occurredAtUtc}</span>
                          </div>
                          <p className="text-slate-500">{event.eventType} · {event.location}</p>
                        </div>
                      ))}
                    </div>
                  </div>
                </div>
              ) : (
                <p className="mt-2 text-sm text-slate-500">Select an asset to inspect custody, scan history, and assignment records.</p>
              )}
            </section>

            {canManageFleet ? <section className="panel p-3">
              <p className="text-[10px] font-bold uppercase tracking-[0.16em] text-slate-500">Scan & custody</p>
              <h2 className="mt-0.5 text-base font-bold text-slate-950">Barcode / RFID actions</h2>
              <div className="mt-2 grid gap-2">
                <label><span className="sr-only">Scan value or tag</span><input value={forms.scanValue} onChange={(e) => { setForms((current) => ({ ...current, scanValue: e.target.value })); if (actionMessage) setActionMessage(''); }} placeholder="Scan value / tag" className="field" /></label>
                <label><span className="sr-only">Scan notes</span><textarea value={forms.scanNotes} onChange={(e) => setForms((current) => ({ ...current, scanNotes: e.target.value }))} rows={2} placeholder="Scan notes" className="field" /></label>
                {actionMessage && <p className="text-sm font-medium text-rose-600" role="alert">{actionMessage}</p>}
                <button onClick={scan} disabled={!forms.scanValue.trim()} className="btn-primary min-h-11 w-full gap-2 sm:min-h-9 sm:w-auto">
                  <Barcode className="h-4 w-4" />
                  Capture scan
                </button>
              </div>
            </section> : null}

            {canManageFleet ? <section className="panel p-3">
              <p className="text-[10px] font-bold uppercase tracking-[0.16em] text-slate-500">Assignment</p>
              <h2 className="mt-0.5 text-base font-bold text-slate-950">Move selected asset</h2>
              <div className="mt-2 grid gap-2">
                <label><span className="sr-only">Assignee name</span><input value={forms.assignName} onChange={(e) => setForms((current) => ({ ...current, assignName: e.target.value }))} placeholder="Assignee name" className="field" /></label>
                <label><span className="sr-only">Current location</span><input value={forms.assignLocation} onChange={(e) => setForms((current) => ({ ...current, assignLocation: e.target.value }))} placeholder="Current location" className="field" /></label>
                <label><span className="sr-only">Shipment</span><select value={selectedShipmentId} onChange={(e) => setSelectedShipmentId(e.target.value)} aria-label="Shipment" className="field">
                  {shipments.map((shipment) => <option key={shipment.id} value={shipment.id}>{shipment.shipmentNumber}</option>)}
                </select></label>
                <label><span className="sr-only">Movement notes</span><textarea value={forms.movementNotes} onChange={(e) => setForms((current) => ({ ...current, movementNotes: e.target.value }))} rows={2} placeholder="Movement notes" className="field" /></label>
                <div className="grid gap-2 sm:grid-cols-2">
                  <button onClick={() => assign('checkOut')} disabled={!selectedAssetId} className="btn-primary min-h-11 w-full gap-2 sm:min-h-9">
                    <CheckCheck className="h-4 w-4" />
                    Check out
                  </button>
                  <button onClick={() => assign('checkIn')} disabled={!selectedAssetId} className="btn-ghost min-h-11 w-full gap-2 sm:min-h-9">
                    <Truck className="h-4 w-4" />
                    Check in
                  </button>
                </div>
              </div>
            </section> : null}
          </aside>
        </div>

        <div className="grid gap-3 xl:grid-cols-[0.8fr_1.2fr]">
          <section className="panel p-3">
            <p className="text-[10px] font-bold uppercase tracking-[0.16em] text-slate-500">Asset types</p>
            <h2 className="mt-0.5 text-base font-bold text-slate-950">Categories in the tenant</h2>
            <div className="mt-2 grid gap-2 sm:grid-cols-2 xl:grid-cols-1">
              {assetTypes.map((type) => (
                <div key={type.id} className="rounded-xl border border-slate-200 bg-white p-2.5">
                  <div className="flex items-start justify-between gap-3">
                    <div className="min-w-0">
                      <p className="font-semibold text-slate-950">{type.name}</p>
                      <p className="truncate text-xs text-slate-500" title={`${type.code} · ${type.description}`}>{type.code} · {type.description}</p>
                    </div>
                    <span className="rounded-full bg-cyan-50 px-2 py-0.5 text-[11px] font-bold text-cyan-700">{type.isReturnable ? 'Returnable' : 'Consumable'}</span>
                  </div>
                </div>
              ))}
            </div>
          </section>

          {canManageFleet ? <section className="panel p-3">
            <p className="text-[10px] font-bold uppercase tracking-[0.16em] text-slate-500">Create asset</p>
            <h2 className="mt-0.5 text-base font-bold text-slate-950">Inventory intake</h2>
            <div className="mt-2 grid gap-2 sm:grid-cols-2 lg:grid-cols-3">
              <label className="sm:col-span-2 lg:col-span-1"><span className="sr-only">Asset type</span><select aria-label="Asset type" value={selectedTypeId} onChange={(e) => setSelectedTypeId(e.target.value)} className="field">
                {assetTypes.map((type) => <option key={type.id} value={type.id}>{type.name}</option>)}
              </select></label>
              <label><span className="sr-only">Asset tag</span><input value={forms.assetTag} onChange={(e) => setForms((current) => ({ ...current, assetTag: e.target.value }))} placeholder="Asset tag" className="field" /></label>
              <label><span className="sr-only">Asset name</span><input value={forms.assetName} onChange={(e) => setForms((current) => ({ ...current, assetName: e.target.value }))} placeholder="Asset name" className="field" /></label>
              <label><span className="sr-only">Asset location</span><input value={forms.assetLocation} onChange={(e) => setForms((current) => ({ ...current, assetLocation: e.target.value }))} placeholder="Location" className="field" /></label>
              <label><span className="sr-only">Asset condition</span><input value={forms.assetCondition} onChange={(e) => setForms((current) => ({ ...current, assetCondition: e.target.value }))} placeholder="Condition" className="field" /></label>
              <label><span className="sr-only">Asset quantity</span><input value={forms.assetQuantity} onChange={(e) => setForms((current) => ({ ...current, assetQuantity: e.target.value }))} placeholder="Quantity" className="field" /></label>
            </div>
            <div className="mt-2 flex justify-end">
              <button disabled={saving} onClick={createAsset} className="btn-primary min-h-11 w-full sm:min-h-9 sm:w-auto">
                {saving ? 'Saving...' : 'Create asset'}
              </button>
            </div>

            <div className="mt-3 border-t border-slate-200 pt-3">
              <p className="text-xs font-semibold text-slate-700">Add asset type</p>
              <div className="mt-2 grid gap-2 sm:grid-cols-2 lg:grid-cols-[0.7fr_1fr_1.5fr_auto]">
                <label><span className="sr-only">New type code</span><input value={forms.typeCode} onChange={(e) => setForms((current) => ({ ...current, typeCode: e.target.value }))} placeholder="Type code" className="field" /></label>
                <label><span className="sr-only">New type name</span><input value={forms.typeName} onChange={(e) => setForms((current) => ({ ...current, typeName: e.target.value }))} placeholder="Type name" className="field" /></label>
                <label><span className="sr-only">Type description</span><input value={forms.typeDescription} onChange={(e) => setForms((current) => ({ ...current, typeDescription: e.target.value }))} placeholder="Description" className="field" /></label>
                <button disabled={saving} onClick={createAssetType} className="btn-ghost min-h-11 w-full sm:min-h-9 sm:w-auto">Add type</button>
              </div>
            </div>
          </section> : null}
        </div>
    </div>
  );
}

export default FleetAssetManagementPage;
