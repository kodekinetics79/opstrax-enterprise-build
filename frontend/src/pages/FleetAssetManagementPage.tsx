import { useEffect, useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { Archive, Barcode, Boxes, CheckCheck, Layers3, SquareStack, Truck } from 'lucide-react';
import { ClayStat, ConsoleRail } from '@/components/console';
import { notifyApiError } from '@/services/fleetTmsApi';
import { fleetApi, fleetAssetApi, type Asset, type AssetAssignment, type AssetEvent, type AssetType } from '@/services/fleetTmsApi';

type AssetDetail = {
  asset: Asset;
  assignments: AssetAssignment[];
  events: AssetEvent[];
};

export function FleetAssetManagementPage() {
  const [assetTypes, setAssetTypes] = useState<AssetType[]>([]);
  const [assets, setAssets] = useState<Asset[]>([]);
  const [shipments, setShipments] = useState<Array<{ id: string; shipmentNumber: string; customerName: string; status: string }>>([]);
  const [detail, setDetail] = useState<AssetDetail | null>(null);
  const [selectedAssetId, setSelectedAssetId] = useState('');
  const [selectedTypeId, setSelectedTypeId] = useState('');
  const [selectedShipmentId, setSelectedShipmentId] = useState('');
  const [loading, setLoading] = useState(true);
  const [loadWarnings, setLoadWarnings] = useState<string[]>([]);
  const [saving, setSaving] = useState(false);
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
      fleetAssetApi.assets(),
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
    apply(assetsRes, 'Assets', (value) => setAssets(value.items));
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
    const assigned = assets.filter((asset) => asset.status === 'Assigned' || asset.status === 'InUse').length;
    const available = assets.filter((asset) => asset.status === 'Available').length;
    const maintenance = assets.filter((asset) => asset.condition !== 'Good').length;
    return [
      { label: 'Asset types', value: assetTypes.length, tone: 'text-cyan-700' },
      { label: 'Assigned', value: assigned, tone: 'text-blue-700' },
      { label: 'Available', value: available, tone: 'text-emerald-700' },
      { label: 'Needs review', value: maintenance, tone: 'text-amber-700' },
    ];
  }, [assetTypes, assets]);

  const createAssetType = async () => {
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
    if (!selectedTypeId) return;
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
    try {
      await fleetAssetApi.scan({
        kind: 'Barcode',
        assetId: selectedAssetId || undefined,
        shipmentId: selectedShipmentId || undefined,
        scannedValue: forms.scanValue,
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
    if (!selectedAssetId) return;
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

  if (loading) return <div className="min-h-screen bg-slate-950" />;

  return (
    <main className="fleet-console text-slate-900">
      <section className="relative mx-auto flex w-full max-w-7xl flex-col gap-3">
        <ConsoleRail
          eyebrow="Fleet · Returnable Assets"
          icon={<Boxes className="h-3.5 w-3.5 text-teal-700" />}
          title="Returnable Assets"
          meta={<>
            <span className="font-bold text-slate-700 tabular-nums">{assets.length}</span> assets in custody ·{" "}
            <span className="font-bold text-emerald-600 tabular-nums">{assets.filter((asset) => asset.status === 'Available').length}</span> available ·{" "}
            <span className="font-bold text-amber-600 tabular-nums">{assets.filter((asset) => asset.condition !== 'Good').length}</span> need review
          </>}
          actions={
            <Link to="/fleet-workspace" className="btn-ghost h-10">
              Fleet Workspace
            </Link>
          }
        />

        <div className="grid gap-3 lg:grid-cols-[1.1fr_0.9fr]">
          <div className="space-y-3">
            {loadWarnings.length ? (
              <div className="rounded-2xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-900">
                <p className="font-semibold">Some asset workspace sections were not available.</p>
                <ul className="mt-2 space-y-1 text-xs text-amber-800">
                  {loadWarnings.slice(0, 3).map((warning) => <li key={warning}>• {warning}</li>)}
                </ul>
              </div>
            ) : null}

            <div className="grid grid-cols-2 gap-3 xl:grid-cols-4">
              {metrics.map((metric, i) => (
                <ClayStat key={metric.label} Icon={SquareStack}
                  tone={["fc-clay-sky", "fc-clay-teal", "fc-clay-emerald", "fc-clay-amber"][i % 4]}
                  iconCls={metric.tone}
                  label={metric.label} value={metric.value}
                  alert={metric.label === "Needs review"} />
              ))}
            </div>

            <div className="grid gap-6 xl:grid-cols-[0.92fr_1.08fr]">
              <section className="rounded-[28px] border border-white/75 bg-white/75 p-6 shadow-[0_24px_50px_rgba(15,23,42,0.08)] backdrop-blur">
                <p className="text-xs font-bold uppercase tracking-[0.24em] text-slate-500">Asset types</p>
                <h2 className="mt-2 text-2xl font-black text-slate-950">Categories in the tenant</h2>
                <div className="mt-5 space-y-3">
                  {assetTypes.map((type) => (
                    <div key={type.id} className="rounded-2xl border border-slate-200 bg-white p-4">
                      <div className="flex items-center justify-between">
                        <div>
                          <p className="font-bold text-slate-950">{type.name}</p>
                          <p className="text-sm text-slate-500">{type.code} · {type.description}</p>
                        </div>
                        <span className="rounded-full bg-cyan-50 px-3 py-1 text-xs font-bold text-cyan-700">{type.isReturnable ? 'Returnable' : 'Consumable'}</span>
                      </div>
                    </div>
                  ))}
                </div>
              </section>

              <section className="rounded-[28px] border border-white/75 bg-white/75 p-6 shadow-[0_24px_50px_rgba(15,23,42,0.08)] backdrop-blur">
                <p className="text-xs font-bold uppercase tracking-[0.24em] text-slate-500">Create asset</p>
                <h2 className="mt-2 text-2xl font-black text-slate-950">Inventory intake</h2>
                <div className="mt-5 grid gap-4 sm:grid-cols-2">
                  <select value={selectedTypeId} onChange={(e) => setSelectedTypeId(e.target.value)} className="rounded-2xl border border-slate-200 bg-white px-4 py-3 outline-none focus:border-cyan-400 sm:col-span-2">
                    {assetTypes.map((type) => <option key={type.id} value={type.id}>{type.name}</option>)}
                  </select>
                  <input value={forms.assetTag} onChange={(e) => setForms((current) => ({ ...current, assetTag: e.target.value }))} placeholder="Asset tag" className="rounded-2xl border border-slate-200 bg-white px-4 py-3 outline-none focus:border-cyan-400" />
                  <input value={forms.assetName} onChange={(e) => setForms((current) => ({ ...current, assetName: e.target.value }))} placeholder="Asset name" className="rounded-2xl border border-slate-200 bg-white px-4 py-3 outline-none focus:border-cyan-400" />
                  <input value={forms.assetLocation} onChange={(e) => setForms((current) => ({ ...current, assetLocation: e.target.value }))} placeholder="Location" className="rounded-2xl border border-slate-200 bg-white px-4 py-3 outline-none focus:border-cyan-400" />
                  <input value={forms.assetCondition} onChange={(e) => setForms((current) => ({ ...current, assetCondition: e.target.value }))} placeholder="Condition" className="rounded-2xl border border-slate-200 bg-white px-4 py-3 outline-none focus:border-cyan-400" />
                  <input value={forms.assetQuantity} onChange={(e) => setForms((current) => ({ ...current, assetQuantity: e.target.value }))} placeholder="Quantity" className="rounded-2xl border border-slate-200 bg-white px-4 py-3 outline-none focus:border-cyan-400" />
                  <input value={forms.typeCode} onChange={(e) => setForms((current) => ({ ...current, typeCode: e.target.value }))} placeholder="New type code" className="rounded-2xl border border-slate-200 bg-white px-4 py-3 outline-none focus:border-cyan-400" />
                  <input value={forms.typeName} onChange={(e) => setForms((current) => ({ ...current, typeName: e.target.value }))} placeholder="New type name" className="rounded-2xl border border-slate-200 bg-white px-4 py-3 outline-none focus:border-cyan-400" />
                  <textarea value={forms.typeDescription} onChange={(e) => setForms((current) => ({ ...current, typeDescription: e.target.value }))} rows={3} placeholder="Type description" className="sm:col-span-2 rounded-2xl border border-slate-200 bg-white px-4 py-3 outline-none focus:border-cyan-400" />
                </div>
                <div className="mt-4 grid gap-3 sm:grid-cols-2">
                  <button disabled={saving} onClick={createAsset} className="rounded-2xl bg-gradient-to-r from-blue-600 to-cyan-600 px-4 py-3 font-bold text-white shadow-lg transition hover:from-blue-500 hover:to-cyan-500 disabled:opacity-60">
                    {saving ? 'Saving...' : 'Create asset'}
                  </button>
                  <button disabled={saving} onClick={createAssetType} className="rounded-2xl border border-slate-200 bg-white px-4 py-3 font-bold text-slate-700 transition hover:border-cyan-300 hover:text-cyan-700 disabled:opacity-60">
                    Add asset type
                  </button>
                </div>
              </section>
            </div>
          </div>

          <aside className="space-y-6">
            <section className="rounded-[28px] border border-white/75 bg-slate-950/95 p-6 text-white shadow-[0_28px_60px_rgba(15,23,42,0.32)]">
              <div className="flex items-center justify-between">
                <div>
                  <p className="text-xs font-bold uppercase tracking-[0.24em] text-cyan-200/70">Assets</p>
                  <h2 className="mt-2 text-2xl font-black">Inventory list</h2>
                </div>
                <Archive className="h-5 w-5 text-cyan-300" />
              </div>
              <div className="mt-5 space-y-3">
                {assets.slice(0, 7).map((asset) => (
                  <button key={asset.id} onClick={() => setSelectedAssetId(asset.id)} className={`w-full rounded-2xl border p-4 text-left transition ${selectedAssetId === asset.id ? 'border-cyan-300 bg-white/10' : 'border-white/10 bg-white/5 hover:bg-white/8'}`}>
                    <div className="flex items-center justify-between">
                      <div>
                        <p className="font-bold">{asset.assetTag}</p>
                        <p className="text-sm text-slate-300">{asset.name} · {asset.currentLocation}</p>
                      </div>
                      <span className="rounded-full bg-white/10 px-3 py-1 text-xs font-bold">{asset.status}</span>
                    </div>
                  </button>
                ))}
              </div>
            </section>

            <section className="rounded-[28px] border border-white/75 bg-white/80 p-6 shadow-[0_24px_50px_rgba(15,23,42,0.08)] backdrop-blur">
              <p className="text-xs font-bold uppercase tracking-[0.24em] text-slate-500">Scan & custody</p>
              <h2 className="mt-2 text-2xl font-black text-slate-950">Barcode / RFID actions</h2>
              <div className="mt-5 space-y-3">
                <input value={forms.scanValue} onChange={(e) => setForms((current) => ({ ...current, scanValue: e.target.value }))} placeholder="Scan value / tag" className="w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 outline-none focus:border-cyan-400" />
                <textarea value={forms.scanNotes} onChange={(e) => setForms((current) => ({ ...current, scanNotes: e.target.value }))} rows={3} placeholder="Scan notes" className="w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 outline-none focus:border-cyan-400" />
                <button onClick={scan} className="inline-flex w-full items-center justify-center gap-2 rounded-2xl bg-slate-950 px-4 py-3 font-bold text-white transition hover:bg-slate-800">
                  <Barcode className="h-4 w-4" />
                  Capture barcode scan
                </button>
              </div>
            </section>

            <section className="rounded-[28px] border border-white/75 bg-white/80 p-6 shadow-[0_24px_50px_rgba(15,23,42,0.08)] backdrop-blur">
              <p className="text-xs font-bold uppercase tracking-[0.24em] text-slate-500">Assignment</p>
              <h2 className="mt-2 text-2xl font-black text-slate-950">Move selected asset</h2>
              <div className="mt-5 space-y-3">
                <input value={forms.assignName} onChange={(e) => setForms((current) => ({ ...current, assignName: e.target.value }))} placeholder="Assignee name" className="w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 outline-none focus:border-cyan-400" />
                <input value={forms.assignLocation} onChange={(e) => setForms((current) => ({ ...current, assignLocation: e.target.value }))} placeholder="Current location" className="w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 outline-none focus:border-cyan-400" />
                <select value={selectedShipmentId} onChange={(e) => setSelectedShipmentId(e.target.value)} aria-label="Shipment" className="w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 outline-none focus:border-cyan-400">
                  {shipments.map((shipment) => <option key={shipment.id} value={shipment.id}>{shipment.shipmentNumber}</option>)}
                </select>
                <div className="grid gap-3 sm:grid-cols-2">
                  <button onClick={() => assign('checkOut')} className="inline-flex items-center justify-center gap-2 rounded-2xl bg-gradient-to-r from-blue-600 to-cyan-600 px-4 py-3 font-bold text-white transition hover:from-blue-500 hover:to-cyan-500">
                    <CheckCheck className="h-4 w-4" />
                    Check out
                  </button>
                  <button onClick={() => assign('checkIn')} className="inline-flex items-center justify-center gap-2 rounded-2xl border border-slate-200 bg-white px-4 py-3 font-bold text-slate-700 transition hover:border-cyan-300 hover:text-cyan-700">
                    <Truck className="h-4 w-4" />
                    Check in
                  </button>
                </div>
                <textarea value={forms.movementNotes} onChange={(e) => setForms((current) => ({ ...current, movementNotes: e.target.value }))} rows={3} placeholder="Movement notes" className="w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 outline-none focus:border-cyan-400" />
              </div>
            </section>

            <section className="rounded-[28px] border border-white/75 bg-white/80 p-6 shadow-[0_24px_50px_rgba(15,23,42,0.08)] backdrop-blur">
              <p className="text-xs font-bold uppercase tracking-[0.24em] text-slate-500">Selected asset</p>
              {detail ? (
                <div className="mt-4 space-y-4">
                  <div>
                    <p className="text-xl font-black text-slate-950">{detail.asset.assetTag}</p>
                    <p className="text-sm text-slate-500">{detail.asset.name} · {detail.asset.currentLocation}</p>
                  </div>
                  <div className="grid grid-cols-2 gap-3 text-sm">
                    <div className="rounded-2xl bg-slate-50 p-3">
                      <p className="text-[11px] uppercase tracking-[0.2em] text-slate-400">Status</p>
                      <p className="font-bold text-slate-900">{detail.asset.status}</p>
                    </div>
                    <div className="rounded-2xl bg-slate-50 p-3">
                      <p className="text-[11px] uppercase tracking-[0.2em] text-slate-400">Condition</p>
                      <p className="font-bold text-slate-900">{detail.asset.condition}</p>
                    </div>
                    <div className="rounded-2xl bg-slate-50 p-3">
                      <p className="text-[11px] uppercase tracking-[0.2em] text-slate-400">Quantity</p>
                      <p className="font-bold text-slate-900">{detail.asset.quantity}</p>
                    </div>
                    <div className="rounded-2xl bg-slate-50 p-3">
                      <p className="text-[11px] uppercase tracking-[0.2em] text-slate-400">Assignments</p>
                      <p className="font-bold text-slate-900">{detail.assignments.length}</p>
                    </div>
                  </div>

                  <div>
                    <p className="text-xs font-bold uppercase tracking-[0.24em] text-slate-500">Assignments</p>
                    <div className="mt-3 space-y-2">
                      {detail.assignments.slice(0, 3).map((assignment) => (
                        <div key={assignment.id} className="rounded-2xl border border-slate-200 bg-white p-3 text-sm">
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
                    <p className="text-xs font-bold uppercase tracking-[0.24em] text-slate-500">Events</p>
                    <div className="mt-3 space-y-2">
                      {detail.events.slice(0, 4).map((event) => (
                        <div key={event.id} className="rounded-2xl border border-slate-200 bg-slate-50 p-3 text-sm">
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
                <p className="mt-4 text-sm text-slate-500">Select an asset to inspect custody, scan history, and assignment records.</p>
              )}
            </section>
          </aside>
        </div>
      </section>
    </main>
  );
}

export default FleetAssetManagementPage;
