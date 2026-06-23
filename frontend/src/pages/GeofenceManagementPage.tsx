import { useEffect, useRef, useState } from "react";
import L from "leaflet";
import "leaflet/dist/leaflet.css";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiClient, unwrap } from "@/services/apiClient";
import { withFallback } from "@/services/fleetDomainApi";
import { exportCsv, LoadingState, ErrorState, EmptyState } from "@/components/ui";
import { useHasPermission } from "@/hooks/usePermission";
import type { AnyRecord } from "@/types";

// ── API ───────────────────────────────────────────────────────────────────────

const SEED: AnyRecord[] = [
  { id: 1, name: "Manassas Yard",        geofenceType: "Circle", centerLat: 38.7509, centerLng: -77.4753, radiusMeters: 800, status: "Active", eventCount: 34, eventsToday: 4 },
  { id: 2, name: "Dulles Cargo Hub",     geofenceType: "Circle", centerLat: 38.9531, centerLng: -77.4565, radiusMeters: 1200, status: "Active", eventCount: 22, eventsToday: 3 },
  { id: 3, name: "Alexandria DC",        geofenceType: "Circle", centerLat: 38.8048, centerLng: -77.0469, radiusMeters: 600, status: "Active", eventCount: 18, eventsToday: 2 },
  { id: 4, name: "Fairfax Depot",        geofenceType: "Circle", centerLat: 38.8462, centerLng: -77.3064, radiusMeters: 500, status: "Active", eventCount: 11, eventsToday: 1 },
  { id: 5, name: "Arlington Urban Zone", geofenceType: "Circle", centerLat: 38.8799, centerLng: -77.1068, radiusMeters: 400, status: "Inactive", eventCount: 5, eventsToday: 0 },
  { id: 6, name: "Woodbridge I-95 Bay",  geofenceType: "Circle", centerLat: 38.6609, centerLng: -77.2511, radiusMeters: 700, status: "Active", eventCount: 29, eventsToday: 5 },
];
const SEED_SUMMARY: AnyRecord = { total: 6, activeCount: 5, inactiveCount: 1, entryEventsToday: 12, exitEventsToday: 9, vehiclesTriggered: 7 };

const geoApi = {
  list: () => withFallback(unwrap<AnyRecord[]>(apiClient.get("/api/geofences")), () => SEED),
  summary: () => withFallback(unwrap<AnyRecord>(apiClient.get("/api/geofences/summary")), () => SEED_SUMMARY),
  events: (id: number) => withFallback(unwrap<AnyRecord[]>(apiClient.get(`/api/geofences/${id}/events`)), () => []),
  create: (payload: AnyRecord) => withFallback(unwrap<AnyRecord>(apiClient.post("/api/geofences", payload)), () => ({ id: Date.now(), ...payload })),
  update: (id: number, payload: AnyRecord) => withFallback(unwrap<AnyRecord>(apiClient.put(`/api/geofences/${id}`, payload)), () => ({ id, ...payload })),
  remove: (id: number) => withFallback(unwrap<AnyRecord>(apiClient.delete(`/api/geofences/${id}`)), () => ({ id, deleted: true })),
};

// ── Map component ─────────────────────────────────────────────────────────────

const US_CENTER: [number, number] = [38.8, -77.2];

function GeofenceMap({
  zones,
  selected,
  onSelect,
  onMapClick,
  placing,
}: {
  zones: AnyRecord[];
  selected: AnyRecord | null;
  onSelect: (z: AnyRecord) => void;
  onMapClick: (lat: number, lng: number) => void;
  placing: boolean;
}) {
  const mapRef = useRef<L.Map | null>(null);
  const containerRef = useRef<HTMLDivElement>(null);
  const circleLayerRef = useRef<L.LayerGroup | null>(null);

  useEffect(() => {
    if (mapRef.current || !containerRef.current) return;
    const map = L.map(containerRef.current, { zoomControl: true, scrollWheelZoom: true });
    L.tileLayer("https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png", {
      attribution: "© OpenStreetMap",
      maxZoom: 18,
    }).addTo(map);
    map.setView(US_CENTER, 9);
    circleLayerRef.current = L.layerGroup().addTo(map);
    mapRef.current = map;
  }, []);

  useEffect(() => {
    const map = mapRef.current;
    if (!map) return;
    const handler = (e: L.LeafletMouseEvent) => {
      if (placing) onMapClick(e.latlng.lat, e.latlng.lng);
    };
    map.on("click", handler);
    return () => { map.off("click", handler); };
  }, [placing, onMapClick]);

  useEffect(() => {
    const layer = circleLayerRef.current;
    if (!layer) return;
    layer.clearLayers();
    for (const z of zones) {
      const lat = Number(z.centerLat ?? z.center_lat);
      const lng = Number(z.centerLng ?? z.center_lng);
      const radius = Number(z.radiusMeters ?? z.radius_meters ?? 500);
      if (!lat || !lng) continue;
      const isSelected = selected && Number(selected.id) === Number(z.id);
      const isActive = String(z.status) === "Active";
      const circle = L.circle([lat, lng], {
        radius,
        color: isSelected ? "#0d9488" : isActive ? "#6366f1" : "#94a3b8",
        weight: isSelected ? 3 : 2,
        fillColor: isSelected ? "#0d9488" : isActive ? "#6366f1" : "#94a3b8",
        fillOpacity: isSelected ? 0.2 : 0.08,
      });
      const marker = L.circleMarker([lat, lng], {
        radius: 5,
        color: isSelected ? "#0d9488" : isActive ? "#6366f1" : "#94a3b8",
        fillOpacity: 1,
      });
      circle.bindTooltip(String(z.name), { permanent: false, direction: "top" });
      circle.on("click", () => onSelect(z));
      marker.on("click", () => onSelect(z));
      layer.addLayer(circle);
      layer.addLayer(marker);
    }
  }, [zones, selected, onSelect]);

  return (
    <div className="relative">
      <div ref={containerRef} className="w-full rounded-xl overflow-hidden" style={{ height: 440 }} />
      {placing && (
        <div className="absolute top-3 left-1/2 -translate-x-1/2 bg-violet-700 text-white text-xs font-semibold px-3 py-1.5 rounded-lg shadow pointer-events-none z-10">
          Click on the map to place geofence center
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
}: {
  initial: Partial<AnyRecord> | null;
  onClose: () => void;
  onSave: (payload: AnyRecord) => void;
  pending: boolean;
}) {
  const isEdit = !!initial?.id;
  const [name, setName] = useState(String(initial?.name ?? ""));
  const [lat, setLat] = useState(String(initial?.centerLat ?? initial?.center_lat ?? ""));
  const [lng, setLng] = useState(String(initial?.centerLng ?? initial?.center_lng ?? ""));
  const [radius, setRadius] = useState(String(initial?.radiusMeters ?? initial?.radius_meters ?? "500"));
  const [status, setStatus] = useState(String(initial?.status ?? "Active"));

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    onSave({ name, centerLat: parseFloat(lat), centerLng: parseFloat(lng), radiusMeters: parseInt(radius), status, geofenceType: "Circle" });
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/30 backdrop-blur-sm" onClick={onClose}>
      <form className="panel w-full max-w-md mx-4 flex flex-col gap-4" onClick={(e) => e.stopPropagation()} onSubmit={handleSubmit}>
        <div className="flex items-center justify-between">
          <h3 className="text-base font-semibold text-slate-900">{isEdit ? "Edit Geofence" : "Create Geofence"}</h3>
          <button type="button" className="text-slate-400 hover:text-slate-600" onClick={onClose}>✕</button>
        </div>
        <div className="flex flex-col gap-1">
          <label className="text-xs font-medium text-slate-700">Name <span className="text-red-500">*</span></label>
          <input required className="border border-slate-200 rounded-lg px-3 py-2 text-sm text-slate-900 focus:outline-none focus:ring-2 focus:ring-violet-400" value={name} onChange={(e) => setName(e.target.value)} placeholder="Zone name" />
        </div>
        <div className="grid grid-cols-2 gap-3">
          <div className="flex flex-col gap-1">
            <label className="text-xs font-medium text-slate-700">Center Latitude</label>
            <input className="border border-slate-200 rounded-lg px-3 py-2 text-sm text-slate-900 focus:outline-none focus:ring-2 focus:ring-violet-400" value={lat} onChange={(e) => setLat(e.target.value)} placeholder="38.75" />
          </div>
          <div className="flex flex-col gap-1">
            <label className="text-xs font-medium text-slate-700">Center Longitude</label>
            <input className="border border-slate-200 rounded-lg px-3 py-2 text-sm text-slate-900 focus:outline-none focus:ring-2 focus:ring-violet-400" value={lng} onChange={(e) => setLng(e.target.value)} placeholder="-77.47" />
          </div>
        </div>
        <div className="flex flex-col gap-1">
          <label className="text-xs font-medium text-slate-700">Radius (meters)</label>
          <input type="number" min={50} max={50000} className="border border-slate-200 rounded-lg px-3 py-2 text-sm text-slate-900 focus:outline-none focus:ring-2 focus:ring-violet-400" value={radius} onChange={(e) => setRadius(e.target.value)} />
        </div>
        {isEdit && (
          <div className="flex flex-col gap-1">
            <label className="text-xs font-medium text-slate-700">Status</label>
            <select className="border border-slate-200 rounded-lg px-3 py-2 text-sm text-slate-900 focus:outline-none focus:ring-2 focus:ring-violet-400" value={status} onChange={(e) => setStatus(e.target.value)}>
              <option>Active</option>
              <option>Inactive</option>
            </select>
          </div>
        )}
        <div className="flex justify-end gap-2 pt-1">
          <button type="button" className="btn-secondary text-sm" onClick={onClose}>Cancel</button>
          <button type="submit" disabled={pending || !name} className="bg-violet-600 hover:bg-violet-700 disabled:opacity-50 text-white text-sm font-medium px-4 py-2 rounded-lg transition-colors">
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

  const SEED_EVT: AnyRecord[] = Array.from({ length: 8 }, (_, i) => ({
    id: i + 1,
    vehicleCode: `VH-00${i + 1}`,
    driverName: ["Marcus Johnson","Sofia Reyes","Liam Patel","Aisha Williams"][i % 4],
    eventType: i % 2 === 0 ? "Entry" : "Exit",
    eventTime: new Date(Date.now() - i * 1800000).toISOString(),
  }));

  const displayEvents = events.length > 0 ? events : SEED_EVT;

  return (
    <div className="fixed inset-0 z-40 flex justify-end" onClick={onClose}>
      <div className="bg-slate-950 w-full max-w-sm h-full flex flex-col overflow-y-auto shadow-2xl" onClick={(e) => e.stopPropagation()}>
        <div className="flex items-center justify-between px-5 py-4 border-b border-white/8">
          <span className="text-sm font-semibold text-white">{String(zone.name)} — Events</span>
          <button type="button" className="text-slate-400 hover:text-white" onClick={onClose} aria-label="Close">✕</button>
        </div>
        <div className="px-5 py-3 flex gap-4 border-b border-white/6">
          <div>
            <p className="text-xs text-slate-400">Radius</p>
            <p className="text-sm font-semibold text-white">{Number(zone.radiusMeters ?? zone.radius_meters ?? 0).toLocaleString()} m</p>
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
          {displayEvents.map((ev, i) => (
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
  const [search, setSearch] = useState("");
  const [statusFilter, setStatusFilter] = useState<"All" | "Active" | "Inactive">("All");
  const [toast, setToast] = useState<string | null>(null);

  const listQ = useQuery({ queryKey: ["geofences"], queryFn: geoApi.list, refetchInterval: 30_000 });
  const summaryQ = useQuery({ queryKey: ["geofences", "summary"], queryFn: geoApi.summary });

  const createMutation = useMutation({
    mutationFn: (payload: AnyRecord) => geoApi.create(payload),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ["geofences"] }); setModalData(null); showToast("Geofence created"); },
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

  const zones = (listQ.data ?? []) as AnyRecord[];
  const s = (summaryQ.data ?? {}) as AnyRecord;

  const filtered = zones.filter((z) => {
    if (statusFilter !== "All" && z.status !== statusFilter) return false;
    if (search) return String(z.name ?? "").toLowerCase().includes(search.toLowerCase());
    return true;
  });

  const [selectedZone, setSelectedZone] = useState<AnyRecord | null>(null);

  if (listQ.isLoading) return <LoadingState />;
  if (listQ.isError) return <ErrorState message={(listQ.error as Error)?.message} />;

  return (
    <div className="flex flex-col gap-6 py-6">
      {toast && (
        <div className="fixed top-4 right-4 z-50 bg-teal-600 text-white text-sm font-medium px-4 py-2.5 rounded-lg shadow-lg">{toast}</div>
      )}

      <div className="flex items-start justify-between gap-4 flex-wrap">
        <div>
          <h1 className="text-xl font-bold text-slate-900">Geofence Management</h1>
          <p className="text-sm text-slate-500 mt-0.5">Define zones, monitor entry/exit events and set boundary alerts for your fleet</p>
        </div>
        <div className="flex gap-2">
          <button type="button" className="btn-secondary text-sm" onClick={() => exportCsv("geofences", zones)}>Export CSV</button>
          {canEdit && (
            <button
              type="button"
              className={`text-sm px-4 py-2 rounded-lg font-medium border transition-colors ${placing ? "bg-violet-100 border-violet-300 text-violet-700" : "bg-violet-600 text-white border-violet-600 hover:bg-violet-700"}`}
              onClick={() => setPlacing(!placing)}
            >
              {placing ? "Cancel placement" : "+ Create Zone"}
            </button>
          )}
        </div>
      </div>

      {/* KPI strip */}
      <div className="flex flex-wrap gap-3">
        {[
          { label: "Total Zones",       val: s.total ?? zones.length },
          { label: "Active",            val: s.activeCount ?? zones.filter((z) => z.status === "Active").length, accent: "text-violet-600" },
          { label: "Entry Events Today",val: s.entryEventsToday ?? 0, accent: "text-teal-600" },
          { label: "Exit Events Today", val: s.exitEventsToday ?? 0, accent: "text-amber-600" },
          { label: "Vehicles Triggered",val: s.vehiclesTriggered ?? 0, accent: "text-slate-700" },
        ].map(({ label, val, accent }) => (
          <div key={label} className="panel flex flex-col gap-1 min-w-30">
            <span className={`text-2xl font-bold ${accent ?? "text-slate-900"}`}>{String(val)}</span>
            <span className="text-xs text-slate-500 font-medium">{label}</span>
          </div>
        ))}
      </div>

      {/* Map + list split */}
      <div className="grid gap-4 xl:grid-cols-[1fr_380px]">
        {/* Map */}
        <div className="panel p-4">
          <div className="flex items-center justify-between mb-3">
            <h2 className="text-sm font-semibold text-slate-900">Zone Map</h2>
            <span className="text-xs text-slate-400">{zones.filter((z) => z.status === "Active").length} active zones · click a zone to see events</span>
          </div>
          <GeofenceMap
            zones={zones}
            selected={selectedZone}
            onSelect={(z) => { setSelectedZone(z); setShowEvents(z); }}
            onMapClick={handleMapClick}
            placing={placing}
          />
        </div>

        {/* Zone list */}
        <div className="flex flex-col gap-3">
          <div className="panel flex gap-2 items-center">
            <div className="flex gap-1">
              {(["All", "Active", "Inactive"] as const).map((f) => (
                <button key={f} type="button" onClick={() => setStatusFilter(f)}
                  className={`px-2.5 py-1 rounded-lg text-xs font-medium border transition-colors ${statusFilter === f ? "bg-violet-50 border-violet-300 text-violet-700" : "bg-slate-50 border-slate-200 text-slate-600"}`}
                >{f}</button>
              ))}
            </div>
            <input
              type="search" placeholder="Search zones…" value={search}
              onChange={(e) => setSearch(e.target.value)}
              className="ml-auto border border-slate-200 rounded-lg px-2.5 py-1 text-xs text-slate-900 placeholder-slate-400 focus:outline-none focus:ring-2 focus:ring-violet-400 w-32"
            />
          </div>

          {filtered.length === 0 ? (
            <EmptyState title="No zones found" />
          ) : (
            <div className="flex flex-col gap-2 overflow-y-auto max-h-[420px]">
              {filtered.map((zone) => {
                const isActive = zone.status === "Active";
                const isSel = selectedZone && Number(selectedZone.id) === Number(zone.id);
                return (
                  <div
                    key={String(zone.id)}
                    className={`panel p-3 cursor-pointer transition-colors ${isSel ? "border-violet-400 bg-violet-50" : "hover:bg-slate-50"}`}
                    onClick={() => setSelectedZone(isSel ? null : zone)}
                  >
                    <div className="flex items-start justify-between gap-2">
                      <div className="flex-1 min-w-0">
                        <p className="font-medium text-slate-900 text-sm truncate">{String(zone.name)}</p>
                        <p className="text-xs text-slate-500 mt-0.5">
                          {Number(zone.radiusMeters ?? zone.radius_meters ?? 0).toLocaleString()} m radius · {String(zone.eventsToday ?? 0)} events today
                        </p>
                      </div>
                      <div className="flex items-center gap-1.5 shrink-0">
                        <span className={`text-xs px-1.5 py-0.5 rounded-full border font-medium ${isActive ? "bg-teal-50 border-teal-200 text-teal-700" : "bg-slate-100 border-slate-200 text-slate-500"}`}>
                          {String(zone.status)}
                        </span>
                        {canEdit && (
                          <>
                            <button type="button" className="text-xs text-slate-400 hover:text-violet-600 px-1" onClick={(e) => { e.stopPropagation(); setModalData(zone); }}>✎</button>
                            <button type="button" className="text-xs text-slate-400 hover:text-red-500 px-1" onClick={(e) => { e.stopPropagation(); if (confirm(`Delete "${String(zone.name)}"?`)) deleteMutation.mutate(Number(zone.id)); }}>✕</button>
                          </>
                        )}
                      </div>
                    </div>
                    <button
                      type="button"
                      className="mt-2 text-xs text-violet-600 hover:text-violet-800 font-medium"
                      onClick={(e) => { e.stopPropagation(); setShowEvents(zone); }}
                    >
                      View events →
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
          onClose={() => setModalData(null)}
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
