import { useEffect, useRef, useState } from "react";
import { chart } from "@/styles/tokens";
import L from "leaflet";
import "leaflet/dist/leaflet.css";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowRight, Download, MapPin, Pencil, Pentagon, Plus, Trash2, X } from "lucide-react";
import { apiClient, unwrap } from "@/services/apiClient";
import { exportCsv, LoadingState, ErrorState, EmptyState, PageHeader } from "@/components/ui";
import { useHasPermission } from "@/hooks/usePermission";
import { useDialogFocus } from "@/hooks/useDialogFocus";
import type { AnyRecord } from "@/types";

// ── API ───────────────────────────────────────────────────────────────────────
const geoApi = {
  list: () => unwrap<AnyRecord[]>(apiClient.get("/api/geofences")),
  summary: () => unwrap<AnyRecord>(apiClient.get("/api/geofences/summary")),
  events: (id: number) => unwrap<AnyRecord[]>(apiClient.get(`/api/geofences/${id}/events`)),
  create: (payload: AnyRecord) => unwrap<AnyRecord>(apiClient.post("/api/geofences", payload)),
  update: (id: number, payload: AnyRecord) => unwrap<AnyRecord>(apiClient.put(`/api/geofences/${id}`, payload)),
  remove: (id: number) => unwrap<AnyRecord>(apiClient.delete(`/api/geofences/${id}`)),
};

// ── Map component ─────────────────────────────────────────────────────────────

const US_CENTER: [number, number] = [38.8, -77.2];

// Parse a zone's polygonJson into an array of [lat, lng] pairs (or null if not a polygon).
function parsePolygon(z: AnyRecord): [number, number][] | null {
  const raw = z.polygonJson ?? z.polygon_json;
  if (!raw) return null;
  let arr: unknown = raw;
  if (typeof raw === "string") {
    try { arr = JSON.parse(raw); } catch { return null; }
  }
  if (!Array.isArray(arr) || arr.length < 3) return null;
  const pts: [number, number][] = [];
  for (const p of arr) {
    if (!Array.isArray(p) || p.length < 2) return null;
    const lat = Number(p[0]);
    const lng = Number(p[1]);
    if (!Number.isFinite(lat) || !Number.isFinite(lng)) return null;
    pts.push([lat, lng]);
  }
  return pts.length >= 3 ? pts : null;
}

function GeofenceMap({
  zones,
  selected,
  onSelect,
  onMapClick,
  placing,
  drawingPolygon,
  polygonPoints,
  onAddVertex,
}: {
  zones: AnyRecord[];
  selected: AnyRecord | null;
  onSelect: (z: AnyRecord) => void;
  onMapClick: (lat: number, lng: number) => void;
  placing: boolean;
  drawingPolygon: boolean;
  polygonPoints: [number, number][];
  onAddVertex: (lat: number, lng: number) => void;
}) {
  const mapRef = useRef<L.Map | null>(null);
  const containerRef = useRef<HTMLDivElement>(null);
  const circleLayerRef = useRef<L.LayerGroup | null>(null);
  const draftLayerRef = useRef<L.LayerGroup | null>(null);

  useEffect(() => {
    if (mapRef.current || !containerRef.current) return;
    const map = L.map(containerRef.current, { zoomControl: true, scrollWheelZoom: true });
    L.tileLayer("https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png", {
      attribution: "© OpenStreetMap",
      maxZoom: 18,
    }).addTo(map);
    map.setView(US_CENTER, 9);
    circleLayerRef.current = L.layerGroup().addTo(map);
    draftLayerRef.current = L.layerGroup().addTo(map);
    mapRef.current = map;
  }, []);

  useEffect(() => {
    const map = mapRef.current;
    if (!map) return;
    const handler = (e: L.LeafletMouseEvent) => {
      if (drawingPolygon) onAddVertex(e.latlng.lat, e.latlng.lng);
      else if (placing) onMapClick(e.latlng.lat, e.latlng.lng);
    };
    map.on("click", handler);
    return () => { map.off("click", handler); };
  }, [placing, drawingPolygon, onMapClick, onAddVertex]);

  useEffect(() => {
    const layer = circleLayerRef.current;
    if (!layer) return;
    layer.clearLayers();
    for (const z of zones) {
      const isSelected = selected && Number(selected.id) === Number(z.id);
      const isActive = String(z.status) === "Active";

      const polygon = parsePolygon(z);
      if (polygon) {
        const color = isSelected ? chart.teal600 : chart.violet500;
        const poly = L.polygon(polygon, {
          color,
          weight: isSelected ? 3 : 2,
          fillColor: color,
          fillOpacity: isSelected ? 0.25 : isActive ? 0.12 : 0.06,
        });
        poly.bindTooltip(String(z.name), { permanent: false, direction: "top" });
        poly.on("click", () => onSelect(z));
        layer.addLayer(poly);
        continue;
      }

      const lat = Number(z.centerLat ?? z.center_lat);
      const lng = Number(z.centerLng ?? z.center_lng);
      const radius = Number(z.radiusMeters ?? z.radius_meters ?? 500);
      if (!lat || !lng) continue;
      const circle = L.circle([lat, lng], {
        radius,
        color: isSelected ? chart.teal600 : isActive ? chart.indigo500 : chart.slate400,
        weight: isSelected ? 3 : 2,
        fillColor: isSelected ? chart.teal600 : isActive ? chart.indigo500 : chart.slate400,
        fillOpacity: isSelected ? 0.2 : 0.08,
      });
      const marker = L.circleMarker([lat, lng], {
        radius: 5,
        color: isSelected ? chart.teal600 : isActive ? chart.indigo500 : chart.slate400,
        fillOpacity: 1,
      });
      circle.bindTooltip(String(z.name), { permanent: false, direction: "top" });
      circle.on("click", () => onSelect(z));
      marker.on("click", () => onSelect(z));
      layer.addLayer(circle);
      layer.addLayer(marker);
    }
  }, [zones, selected, onSelect]);

  // Render the in-progress polygon as the user clicks.
  useEffect(() => {
    const layer = draftLayerRef.current;
    if (!layer) return;
    layer.clearLayers();
    if (!drawingPolygon || polygonPoints.length === 0) return;
    if (polygonPoints.length >= 3) {
      layer.addLayer(L.polygon(polygonPoints, {
        color: chart.violet500,
        weight: 2,
        dashArray: "4 4",
        fillColor: chart.violet500,
        fillOpacity: 0.15,
      }));
    } else if (polygonPoints.length === 2) {
      layer.addLayer(L.polyline(polygonPoints, {
        color: chart.violet500,
        weight: 2,
        dashArray: "4 4",
      }));
    }
    polygonPoints.forEach(([lat, lng]) => {
      layer.addLayer(L.circleMarker([lat, lng], {
        radius: 5,
        color: chart.violet500,
        fillColor: "#fff",
        fillOpacity: 1,
        weight: 2,
      }));
    });
  }, [drawingPolygon, polygonPoints]);

  return (
    <div className="relative">
      <div
        ref={containerRef}
        className="w-full overflow-hidden rounded-xl focus:outline-none focus:ring-2 focus:ring-teal-400 focus:ring-offset-2"
        style={{ height: 440 }}
        tabIndex={0}
        role="region"
        aria-label={drawingPolygon ? `Geofence map. Pan with arrow keys and press Enter or Space to add polygon vertex ${polygonPoints.length + 1}.` : placing ? "Geofence map. Pan with arrow keys and press Enter or Space to place the zone center." : "Geofence map showing configured zones."}
        onKeyDown={(event) => {
          if ((!placing && !drawingPolygon) || !mapRef.current || !["Enter", " "].includes(event.key)) return;
          event.preventDefault();
          const center = mapRef.current.getCenter();
          if (drawingPolygon) onAddVertex(center.lat, center.lng);
          else onMapClick(center.lat, center.lng);
        }}
      />
      {placing && !drawingPolygon && (
        <div className="pointer-events-none absolute left-1/2 top-3 z-10 -translate-x-1/2 rounded-lg bg-slate-900 px-3 py-1.5 text-xs font-semibold text-white shadow" aria-live="polite">
          Click, or pan with arrow keys and press Enter, to place the zone center
        </div>
      )}
      {drawingPolygon && (
        <div className="pointer-events-none absolute left-1/2 top-3 z-10 -translate-x-1/2 rounded-lg bg-slate-900 px-3 py-1.5 text-xs font-semibold text-white shadow" aria-live="polite">
          Click, or pan and press Enter, to add polygon vertices ({polygonPoints.length})
        </div>
      )}
    </div>
  );
}

// ── Create/Edit modal ─────────────────────────────────────────────────────────

function GeofenceModal({
  initial,
  onClose,
  onSave,
  pending,
  polygonPoints,
}: {
  initial: Partial<AnyRecord> | null;
  onClose: () => void;
  onSave: (payload: AnyRecord) => void;
  pending: boolean;
  polygonPoints?: [number, number][];
}) {
  const isEdit = !!initial?.id;
  const isPolygon = !!polygonPoints && polygonPoints.length >= 3;
  const [name, setName] = useState(String(initial?.name ?? ""));
  const [lat, setLat] = useState(String(initial?.centerLat ?? initial?.center_lat ?? ""));
  const [lng, setLng] = useState(String(initial?.centerLng ?? initial?.center_lng ?? ""));
  const [radius, setRadius] = useState(String(initial?.radiusMeters ?? initial?.radius_meters ?? "500"));
  const [status, setStatus] = useState(String(initial?.status ?? "Active"));
  const dialogRef = useDialogFocus<HTMLFormElement>(true, onClose);

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (isPolygon) {
      onSave({ name, geofenceType: "Polygon", polygonJson: polygonPoints, status });
    } else {
      onSave({ name, centerLat: parseFloat(lat), centerLng: parseFloat(lng), radiusMeters: parseInt(radius), status, geofenceType: "Circle" });
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/30 backdrop-blur-sm" onClick={onClose}>
      <form ref={dialogRef} className="panel mx-4 flex w-full max-w-md flex-col gap-4" onClick={(e) => e.stopPropagation()} onSubmit={handleSubmit} role="dialog" aria-modal="true" aria-labelledby="geofence-editor-title">
        <div className="flex items-center justify-between">
          <h3 id="geofence-editor-title" className="text-base font-semibold text-slate-900">{isEdit ? "Edit Geofence" : isPolygon ? "Create Polygon Geofence" : "Create Geofence"}</h3>
          <button type="button" className="rounded-lg p-1 text-slate-400 transition hover:bg-slate-100 hover:text-slate-700" onClick={onClose} aria-label="Close geofence dialog"><X className="h-4 w-4" /></button>
        </div>
        <div className="flex flex-col gap-1">
          <label htmlFor="geofence-name" className="text-xs font-medium text-slate-700">Name <span className="text-red-500">*</span></label>
          <input id="geofence-name" autoFocus required className="rounded-lg border border-slate-200 px-3 py-2 text-sm text-slate-900 focus:border-teal-400 focus:outline-none focus:ring-2 focus:ring-teal-100" value={name} onChange={(e) => setName(e.target.value)} placeholder="Zone name" />
        </div>
        {isPolygon ? (
          <div className="rounded-lg border border-sky-200 bg-sky-50 px-3 py-2 text-xs text-sky-800">
            Polygon zone with {polygonPoints!.length} vertices. Boundary is defined by the points you drew on the map.
          </div>
        ) : (
          <>
            <div className="grid grid-cols-2 gap-3">
              <div className="flex flex-col gap-1">
                <label htmlFor="geofence-latitude" className="text-xs font-medium text-slate-700">Center Latitude</label>
                <input id="geofence-latitude" required type="number" min={-90} max={90} step="any" className="rounded-lg border border-slate-200 px-3 py-2 text-sm text-slate-900 focus:border-teal-400 focus:outline-none focus:ring-2 focus:ring-teal-100" value={lat} onChange={(e) => setLat(e.target.value)} placeholder="38.75" />
              </div>
              <div className="flex flex-col gap-1">
                <label htmlFor="geofence-longitude" className="text-xs font-medium text-slate-700">Center Longitude</label>
                <input id="geofence-longitude" required type="number" min={-180} max={180} step="any" className="rounded-lg border border-slate-200 px-3 py-2 text-sm text-slate-900 focus:border-teal-400 focus:outline-none focus:ring-2 focus:ring-teal-100" value={lng} onChange={(e) => setLng(e.target.value)} placeholder="-77.47" />
              </div>
            </div>
            <div className="flex flex-col gap-1">
              <label htmlFor="geofence-radius" className="text-xs font-medium text-slate-700">Radius (meters)</label>
              <input id="geofence-radius" required type="number" min={50} max={50000} className="rounded-lg border border-slate-200 px-3 py-2 text-sm text-slate-900 focus:border-teal-400 focus:outline-none focus:ring-2 focus:ring-teal-100" value={radius} onChange={(e) => setRadius(e.target.value)} />
            </div>
          </>
        )}
        {(isEdit || isPolygon) && (
          <div className="flex flex-col gap-1">
            <label htmlFor="geofence-status" className="text-xs font-medium text-slate-700">Status</label>
            <select id="geofence-status" className="rounded-lg border border-slate-200 px-3 py-2 text-sm text-slate-900 focus:border-teal-400 focus:outline-none focus:ring-2 focus:ring-teal-100" value={status} onChange={(e) => setStatus(e.target.value)}>
              <option>Active</option>
              <option>Inactive</option>
            </select>
          </div>
        )}
        <div className="flex justify-end gap-2 pt-1">
          <button type="button" className="btn-secondary text-sm" onClick={onClose}>Cancel</button>
          <button type="submit" disabled={pending || !name} className="btn-primary disabled:opacity-50">
            {pending ? "Saving…" : isEdit ? "Save Changes" : "Create Zone"}
          </button>
        </div>
      </form>
    </div>
  );
}

// ── Events panel ──────────────────────────────────────────────────────────────

function EventsPanel({ zone, onClose }: { zone: AnyRecord; onClose: () => void }) {
  const eventsQ = useQuery({ queryKey: ["geofences", "events", zone.id], queryFn: () => geoApi.events(Number(zone.id)) });
  const events = (eventsQ.data ?? []) as AnyRecord[];
  const dialogRef = useDialogFocus<HTMLDivElement>(true, onClose);

  return (
    <div className="fixed inset-0 z-40 flex justify-end" onClick={onClose}>
      <div ref={dialogRef} className="flex h-full w-full max-w-sm flex-col overflow-y-auto bg-slate-950 shadow-2xl" onClick={(e) => e.stopPropagation()} role="dialog" aria-modal="true" aria-labelledby="geofence-events-title">
        <div className="flex items-center justify-between px-5 py-4 border-b border-white/8">
          <h2 id="geofence-events-title" className="text-sm font-semibold text-white">{String(zone.name)} — Events</h2>
          <button type="button" className="rounded-lg p-1 text-slate-400 transition hover:bg-white/10 hover:text-white" onClick={onClose} aria-label="Close"><X className="h-4 w-4" /></button>
        </div>
        <div className="px-5 py-3 flex gap-4 border-b border-white/6">
          <div>
            <p className="text-xs text-slate-400">Radius</p>
            <p className="text-sm font-semibold text-white">{zone.radiusMeters != null || zone.radius_meters != null ? `${Number(zone.radiusMeters ?? zone.radius_meters).toLocaleString()} m` : "—"}</p>
          </div>
          <div>
            <p className="text-xs text-slate-400">Total Events</p>
            <p className="text-sm font-semibold text-white">{String(zone.eventCount ?? "--")}</p>
          </div>
          <div>
            <p className="text-xs text-slate-400">Today</p>
            <p className="text-sm font-semibold text-teal-400">{String(zone.eventsToday ?? "--")}</p>
          </div>
        </div>
        <div className="flex flex-col divide-y divide-white/6 px-5">
          {eventsQ.isLoading ? (
            <div className="py-6"><LoadingState /></div>
          ) : eventsQ.isError ? (
            <div className="py-6"><ErrorState message={(eventsQ.error as Error)?.message ?? "Unable to load geofence events."} onRetry={() => void eventsQ.refetch()} /></div>
          ) : events.length === 0 ? (
            <div className="py-6">
              <EmptyState title="No geofence events yet" subtitle="Geofence activity will appear here once the backend has events for this zone." />
            </div>
          ) : events.map((ev, i) => (
            <div key={String(ev.id ?? i)} className="py-3">
              <div className="flex items-center justify-between">
                <span className={`text-xs font-semibold px-2 py-0.5 rounded-full ${
                  String(ev.eventType) === "Entry"
                    ? "bg-teal-900/50 text-teal-400"
                    : "bg-amber-900/50 text-amber-400"
                }`}>{String(ev.eventType)}</span>
                <span className="text-xs text-slate-500">{new Date(String(ev.eventTime)).toLocaleString()}</span>
              </div>
              <p className="text-sm text-white mt-1">{String(ev.vehicleCode ?? "--")}</p>
              <p className="text-xs text-slate-400">{String(ev.driverName ?? "--")}</p>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}

// ── Main page ─────────────────────────────────────────────────────────────────

export function GeofenceManagementPage() {
  const qc = useQueryClient();
  const hasPermission = useHasPermission();
  const canEdit = hasPermission("map:manage") || hasPermission("fleet:manage") || hasPermission("admin:system");

  const [modalData, setModalData] = useState<Partial<AnyRecord> | null>(null);
  const [showEvents, setShowEvents] = useState<AnyRecord | null>(null);
  const [placing, setPlacing] = useState(false);
  const [drawingPolygon, setDrawingPolygon] = useState(false);
  const [polygonPoints, setPolygonPoints] = useState<[number, number][]>([]);
  const [pendingPolygon, setPendingPolygon] = useState<[number, number][] | null>(null);
  const [search, setSearch] = useState("");
  const [statusFilter, setStatusFilter] = useState<"All" | "Active" | "Inactive">("All");
  const [toast, setToast] = useState<string | null>(null);

  const listQ = useQuery({ queryKey: ["geofences"], queryFn: geoApi.list, refetchInterval: 30_000 });
  const summaryQ = useQuery({ queryKey: ["geofences", "summary"], queryFn: geoApi.summary });

  const createMutation = useMutation({
    mutationFn: (payload: AnyRecord) => geoApi.create(payload),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["geofences"] });
      setModalData(null);
      setPendingPolygon(null);
      setPolygonPoints([]);
      setDrawingPolygon(false);
      showToast("Geofence created");
    },
  });
  const updateMutation = useMutation({
    mutationFn: ({ id, payload }: { id: number; payload: AnyRecord }) => geoApi.update(id, payload),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ["geofences"] }); setModalData(null); showToast("Geofence updated"); },
  });
  const deleteMutation = useMutation({
    mutationFn: (id: number) => geoApi.remove(id),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ["geofences"] }); showToast("Geofence deleted"); },
  });

  function showToast(msg: string) { setToast(msg); setTimeout(() => setToast(null), 3500); }

  function handleMapClick(lat: number, lng: number) {
    setPlacing(false);
    setModalData({ centerLat: lat.toFixed(6), centerLng: lng.toFixed(6) });
  }

  function startPolygonDraw() {
    setPlacing(false);
    setPolygonPoints([]);
    setDrawingPolygon(true);
  }

  function cancelPolygonDraw() {
    setDrawingPolygon(false);
    setPolygonPoints([]);
  }

  function handleAddVertex(lat: number, lng: number) {
    setPolygonPoints((prev) => [...prev, [Number(lat.toFixed(6)), Number(lng.toFixed(6))]]);
  }

  function undoLastVertex() {
    setPolygonPoints((prev) => prev.slice(0, -1));
  }

  function finishPolygon() {
    if (polygonPoints.length < 3) return;
    setPendingPolygon(polygonPoints);
    setDrawingPolygon(false);
    setModalData({});
  }

  const zones = (listQ.data ?? []) as AnyRecord[];
  const s = summaryQ.data as AnyRecord | undefined;

  const filtered = zones.filter((z) => {
    if (statusFilter !== "All" && z.status !== statusFilter) return false;
    if (search) return String(z.name ?? "").toLowerCase().includes(search.toLowerCase());
    return true;
  });

  const [selectedZone, setSelectedZone] = useState<AnyRecord | null>(null);

  if (listQ.isLoading) return <LoadingState />;
  if (listQ.isError) return <ErrorState message={(listQ.error as Error)?.message} />;

  return (
    <div className="control-tower flex flex-col gap-3">
      {toast && (
        <div className="fixed top-4 right-4 z-50 bg-teal-600 text-white text-sm font-medium px-4 py-2.5 rounded-lg shadow-lg">{toast}</div>
      )}

      <PageHeader
        eyebrow="Operations"
        title="Geofence Management"
        description="Define fleet zones, monitor recorded entry and exit events, and configure boundary alerts."
        actions={
          <>
            <button type="button" className="btn-secondary btn-compact" onClick={() => exportCsv("geofences", zones)}>
              <Download className="h-4 w-4" /> Export CSV
            </button>
            {canEdit && (
              <>
                <button
                  type="button"
                  className="btn-primary btn-compact"
                  onClick={() => { setDrawingPolygon(false); setPolygonPoints([]); setPlacing(false); setModalData({}); }}
                >
                  <Plus className="h-4 w-4" /> Create zone
                </button>
                <button
                  type="button"
                  className={`${drawingPolygon ? "btn-secondary" : "btn-ghost"} btn-compact`}
                  onClick={() => (drawingPolygon ? cancelPolygonDraw() : startPolygonDraw())}
                >
                  <Pentagon className="h-4 w-4" /> {drawingPolygon ? "Cancel drawing" : "Draw polygon"}
                </button>
              </>
            )}
          </>
        }
      />

      {/* KPI strip */}
      <section className="panel flex flex-wrap items-center gap-x-6 gap-y-2 px-3 py-2" aria-label="Geofence summary">
        {[
          { label: "Total Zones",       val: s?.total ?? zones.length },
          { label: "Active",            val: s?.activeCount ?? zones.filter((z) => z.status === "Active").length, accent: "text-teal-600" },
          { label: "Entry Events Today",val: summaryQ.isSuccess && s?.entryEventsToday != null ? s.entryEventsToday : "—", accent: "text-teal-600" },
          { label: "Exit Events Today", val: summaryQ.isSuccess && s?.exitEventsToday != null ? s.exitEventsToday : "—", accent: "text-amber-600" },
          { label: "Vehicles Triggered",val: summaryQ.isSuccess && s?.vehiclesTriggered != null ? s.vehiclesTriggered : "—", accent: "text-slate-700" },
        ].map(({ label, val, accent }) => (
          <div key={label} className="flex min-w-[118px] items-baseline gap-2">
            <span className="text-[10px] font-bold uppercase tracking-[0.12em] text-slate-500">{label}</span>
            <span className={`text-sm font-black tabular-nums ${accent ?? "text-slate-900"}`}>{String(val)}</span>
          </div>
        ))}
      </section>
      {summaryQ.isLoading ? (
        <p className="text-xs text-slate-500" role="status">Loading recorded geofence activity…</p>
      ) : summaryQ.isError ? (
        <div className="flex flex-wrap items-center justify-between gap-2 rounded-xl border border-amber-200 bg-amber-50 px-3 py-2 text-xs text-amber-900" role="alert">
          <span>Recorded geofence activity is unavailable. Zone configuration remains visible.</span>
          <button type="button" className="btn-ghost btn-compact" onClick={() => void summaryQ.refetch()}>Retry activity summary</button>
        </div>
      ) : null}

      {/* Map + list split */}
      <div className="grid items-start gap-3 xl:grid-cols-[minmax(0,1fr)_360px]">
        {/* Map */}
        <div className="panel p-3">
          <div className="flex items-center justify-between mb-3">
            <h2 className="text-sm font-semibold text-slate-900">Zone Map</h2>
            <div className="flex items-center gap-2">
              <span className="text-xs text-slate-400">{zones.filter((z) => z.status === "Active").length} active zones</span>
              {canEdit && (
                <button type="button" className={`${placing ? "btn-secondary" : "btn-ghost"} btn-compact`} onClick={() => { setDrawingPolygon(false); setPolygonPoints([]); setPlacing(!placing); }}>
                  <MapPin className="h-3.5 w-3.5" /> {placing ? "Cancel placement" : "Place on map"}
                </button>
              )}
            </div>
          </div>
          {drawingPolygon && (
            <div className="mb-3 flex flex-wrap items-center gap-2 rounded-xl border border-sky-200 bg-sky-50 px-3 py-2">
              <span className="text-xs font-semibold text-sky-800">Drawing polygon · {polygonPoints.length} {polygonPoints.length === 1 ? "point" : "points"}</span>
              <span className="text-xs text-sky-700">Click the map, or pan with arrow keys and press Enter, to add at least three vertices</span>
              <div className="ml-auto flex gap-1.5">
                <button
                  type="button"
                  disabled={polygonPoints.length === 0}
                  className="btn-ghost btn-compact disabled:opacity-40"
                  onClick={undoLastVertex}
                >
                  Undo last point
                </button>
                <button
                  type="button"
                  disabled={polygonPoints.length === 0}
                  className="btn-ghost btn-compact disabled:opacity-40"
                  onClick={() => setPolygonPoints([])}
                >
                  Clear
                </button>
                <button
                  type="button"
                  disabled={polygonPoints.length < 3}
                  className="btn-primary btn-compact disabled:opacity-40"
                  onClick={finishPolygon}
                >
                  Finish polygon
                </button>
              </div>
            </div>
          )}
          <GeofenceMap
            zones={zones}
            selected={selectedZone}
            onSelect={(z) => { setSelectedZone(z); setShowEvents(z); }}
            onMapClick={handleMapClick}
            placing={placing}
            drawingPolygon={drawingPolygon}
            polygonPoints={polygonPoints}
            onAddVertex={handleAddVertex}
          />
        </div>

        {/* Zone list */}
        <div className="flex flex-col gap-3">
          <div className="panel flex items-center gap-2 p-2">
            <div className="flex gap-1" role="group" aria-label="Geofence status">
              {(["All", "Active", "Inactive"] as const).map((f) => (
                <button
                  key={f}
                  type="button"
                  aria-pressed={statusFilter === f}
                  onClick={() => setStatusFilter(f)}
                  className={`${statusFilter === f ? "btn-primary" : "btn-ghost"} btn-compact`}
                >{f}</button>
              ))}
            </div>
            <input
              type="search" placeholder="Search zones…" value={search}
              onChange={(e) => setSearch(e.target.value)}
              className="ml-auto w-36 rounded-lg border border-slate-200 bg-white px-2.5 py-1.5 text-xs text-slate-900 placeholder-slate-400 focus:border-teal-400 focus:outline-none focus:ring-2 focus:ring-teal-100"
            />
          </div>

          {filtered.length === 0 ? (
            <EmptyState title="No zones found" />
          ) : (
            <div className="flex max-h-[520px] flex-col gap-2 overflow-y-auto">
              {filtered.map((zone) => {
                const isActive = zone.status === "Active";
                const isSel = selectedZone && Number(selectedZone.id) === Number(zone.id);
                const zonePoly = parsePolygon(zone);
                return (
                  <div key={String(zone.id)} className={`panel p-3 transition-colors ${isSel ? "border-teal-300 bg-teal-50/60" : "hover:bg-slate-50"}`}>
                    <div className="flex items-start justify-between gap-2">
                      <button type="button" className="min-w-0 flex-1 rounded-lg text-left focus:outline-none focus:ring-2 focus:ring-teal-400 focus:ring-offset-2" aria-pressed={Boolean(isSel)} onClick={() => setSelectedZone(isSel ? null : zone)}>
                        <p className="font-medium text-slate-900 text-sm truncate">{String(zone.name)}</p>
                        <p className="text-xs text-slate-500 mt-0.5">
                          {zonePoly
                            ? `${zonePoly.length}-vertex polygon`
                            : zone.radiusMeters != null || zone.radius_meters != null
                              ? `${Number(zone.radiusMeters ?? zone.radius_meters).toLocaleString()} m radius`
                              : "Radius unavailable"} · {zone.eventsToday == null ? "Event count unavailable" : `${String(zone.eventsToday)} events today`}
                        </p>
                      </button>
                      <div className="flex items-center gap-1.5 shrink-0">
                        <span className={`text-xs px-1.5 py-0.5 rounded-full border font-medium ${isActive ? "bg-teal-50 border-teal-200 text-teal-700" : "bg-slate-100 border-slate-200 text-slate-500"}`}>
                          {String(zone.status)}
                        </span>
                        {canEdit && (
                          <>
                            <button type="button" className="rounded-md p-1 text-slate-400 transition hover:bg-white hover:text-teal-700" aria-label={`Edit geofence ${String(zone.name ?? zone.id)}`} onClick={(e) => { e.stopPropagation(); setModalData(zone); }}><Pencil className="h-3.5 w-3.5" /></button>
                            <button type="button" className="rounded-md p-1 text-slate-400 transition hover:bg-white hover:text-red-600" aria-label={`Delete geofence ${String(zone.name ?? zone.id)}`} onClick={(e) => { e.stopPropagation(); if (confirm(`Delete "${String(zone.name)}"?`)) deleteMutation.mutate(Number(zone.id)); }}><Trash2 className="h-3.5 w-3.5" /></button>
                          </>
                        )}
                      </div>
                    </div>
                    <button
                      type="button"
                      className="mt-2 inline-flex items-center gap-1 text-xs font-semibold text-teal-700 hover:text-teal-900"
                      aria-label={`View events for geofence ${String(zone.name ?? zone.id)}`}
                      onClick={(e) => { e.stopPropagation(); setShowEvents(zone); }}
                    >
                      View events <ArrowRight className="h-3.5 w-3.5" />
                    </button>
                  </div>
                );
              })}
            </div>
          )}
        </div>
      </div>

      {/* Create / edit modal */}
      {modalData !== null && (
        <GeofenceModal
          initial={modalData}
          polygonPoints={pendingPolygon ?? undefined}
          onClose={() => { setModalData(null); setPendingPolygon(null); }}
          pending={createMutation.isPending || updateMutation.isPending}
          onSave={(payload) => {
            if (modalData.id) {
              updateMutation.mutate({ id: Number(modalData.id), payload });
            } else {
              createMutation.mutate(payload);
            }
          }}
        />
      )}

      {/* Events drawer */}
      {showEvents && <EventsPanel zone={showEvents} onClose={() => setShowEvents(null)} />}
    </div>
  );
}
