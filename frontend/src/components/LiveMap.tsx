import { useEffect, useRef } from "react";
import L from "leaflet";
import "leaflet/dist/leaflet.css";
import type { AnyRecord } from "@/types";

// Returns the vehicle's real GPS coordinate, or null when there is no genuine fix.
// We never fabricate a position — a dot a dispatcher can't trust poisons the whole map.
function getCoords(entity: AnyRecord): [number, number] | null {
  const lat = Number(entity.lat ?? entity.latitude);
  const lng = Number(entity.lng ?? entity.longitude);
  if (Number.isFinite(lat) && Number.isFinite(lng) && lat !== 0 && lng !== 0) return [lat, lng];
  return null;
}

function isMovingState(status: string, speed: number): boolean {
  return speed > 3 || /active|on route|moving|driving|en route/i.test(status);
}

function markerColor(risk: string, status: string, speed: number, isStale: boolean): string {
  if (isStale) return "#94a3b8"; // offline / no recent ping
  if (/high|critical/i.test(risk)) return "#ef4444";
  if (/medium|warning/i.test(risk)) return "#f59e0b";
  return isMovingState(status, speed) ? "#14b8a6" : "#6366f1";
}

function makeVehicleIcon(risk: string, status: string, speed: number, heading: number, isStale: boolean): L.DivIcon {
  const moving = isMovingState(status, speed) && !isStale;
  const color = markerColor(risk, status, speed, isStale);

  // Moving units render as a heading-aware arrow; stationary as a dot; offline as a hollow ring.
  const inner = moving
    ? `<div style="
        width:0;height:0;
        border-left:6px solid transparent;border-right:6px solid transparent;
        border-bottom:13px solid ${color};
        filter:drop-shadow(0 1px 2px rgba(0,0,0,.4));
        transform:rotate(${Number.isFinite(heading) ? heading : 0}deg);
        transform-origin:center 70%;
      "></div>`
    : `<div style="
        width:14px;height:14px;border-radius:50%;
        background:${isStale ? "white" : color};
        border:2.5px solid ${isStale ? color : "white"};
        box-shadow:0 1px 6px rgba(0,0,0,0.35);
      "></div>`;

  return L.divIcon({
    className: "opstrax-vehicle-marker",
    html: `<div style="
      display:flex;align-items:center;justify-content:center;width:18px;height:18px;
      ${moving ? "animation:opstrax-pulse 2s ease-in-out infinite;border-radius:50%;" : ""}
    ">${inner}</div>`,
    iconSize: [18, 18],
    iconAnchor: [9, 9],
    popupAnchor: [0, -11],
  });
}

function entityKey(entity: AnyRecord, index: number): string {
  return String(entity.id ?? entity.vehicleId ?? entity.vehicle_id ?? entity.label ?? entity.vehicleCode ?? entity.vehicle_code ?? `idx-${index}`);
}

function makeGeofenceCircle(zone: AnyRecord, index: number): L.Circle | null {
  const lat = Number(zone.lat ?? zone.latitude ?? zone.center_lat);
  const lng = Number(zone.lng ?? zone.longitude ?? zone.center_lng);
  if (isNaN(lat) || isNaN(lng) || (lat === 0 && lng === 0)) return null;
  return L.circle([lat, lng], {
    radius: Number(zone.radius_meters ?? 8000),
    color: "#14b8a6",
    fillColor: "#14b8a6",
    fillOpacity: 0.06,
    weight: 1.5,
  });
}

interface LiveMapProps {
  entities: AnyRecord[];
  geofences: AnyRecord[];
  onSelect: (entity: AnyRecord) => void;
  /** When set, the map pans/zooms to this vehicle (matched by id/label). */
  focusId?: string | null;
}

export function LiveMap({ entities, geofences, onSelect, focusId }: LiveMapProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  const mapRef = useRef<L.Map | null>(null);
  const markersRef = useRef<Map<string, L.Marker>>(new Map());
  const coordsRef = useRef<Map<string, [number, number]>>(new Map());
  const hasFitRef = useRef(false);
  const geofenceLayersRef = useRef<L.Circle[]>([]);
  // Store onSelect in a ref so markers don't need to be recreated when it changes
  const onSelectRef = useRef(onSelect);
  onSelectRef.current = onSelect;

  // Initialize map once
  useEffect(() => {
    if (!containerRef.current || mapRef.current) return;

    const map = L.map(containerRef.current, {
      center: [39.5, -98.35],
      zoom: 4,
      zoomControl: true,
      attributionControl: true,
    });

    L.tileLayer("https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png", {
      attribution: '&copy; <a href="https://www.openstreetmap.org/copyright" target="_blank">OpenStreetMap</a>',
      maxZoom: 18,
    }).addTo(map);

    // Inject pulse animation keyframes + smooth marker movement once
    if (!document.getElementById("opstrax-map-styles")) {
      const style = document.createElement("style");
      style.id = "opstrax-map-styles";
      style.textContent = `
        @keyframes opstrax-pulse {
          0%,100%{box-shadow:0 1px 6px rgba(0,0,0,.35),0 0 0 0 rgba(20,184,166,.5)}
          50%{box-shadow:0 1px 6px rgba(0,0,0,.35),0 0 0 6px rgba(20,184,166,0)}
        }
        /* Smoothly glide markers to their new GPS position between telemetry frames.
           Suppressed during zoom so markers don't lag the tiles. */
        .leaflet-marker-icon.opstrax-vehicle-marker{transition:transform .9s linear}
        .leaflet-zoom-anim .leaflet-marker-icon.opstrax-vehicle-marker{transition:none}
      `;
      document.head.appendChild(style);
    }

    mapRef.current = map;

    return () => {
      map.remove();
      mapRef.current = null;
      markersRef.current.clear();
      coordsRef.current.clear();
      hasFitRef.current = false;
    };
  }, []);

  // Reconcile vehicle markers when entities change — reuse existing markers so the
  // map doesn't flicker, popups stay open, and positions glide between telemetry frames.
  useEffect(() => {
    const map = mapRef.current;
    if (!map) return;

    const markers = markersRef.current;
    const coords = coordsRef.current;
    const seen = new Set<string>();

    entities.forEach((entity, index) => {
      const coord = getCoords(entity);
      if (!coord) return; // no real GPS fix — don't place a fabricated dot
      const key = entityKey(entity, index);
      seen.add(key);
      const [lat, lng] = coord;
      coords.set(key, [lat, lng]);

      const risk = String(entity.riskLevel ?? entity.risk_level ?? "Low");
      const status = String(entity.status ?? "");
      const label = String(entity.label ?? entity.vehicleCode ?? entity.vehicle_code ?? "Vehicle");
      const driver = String(entity.driverName ?? entity.driver_name ?? "Unassigned");
      const speedRaw = entity.speedMph ?? entity.speed_mph;
      const speed = Number(speedRaw ?? 0);
      const heading = Number(entity.heading ?? 0);
      const isStale = Boolean(entity.isStale);
      const deviceStatus = String(entity.deviceStatus ?? entity.device_status ?? "--");
      const camStatus = String(entity.cameraStatus ?? entity.camera_status ?? "--");

      const popupHtml =
        `<div style="font-family:system-ui;font-size:12px;min-width:190px;line-height:1.5">
          <p style="font-weight:700;margin:0 0 2px;font-size:13px">${label}</p>
          <p style="margin:0;color:#475569">${driver}</p>
          <p style="margin:2px 0 0;color:#64748b">${speedRaw != null ? `${Math.round(speed)} mph &bull; ` : ""}${isStale ? "Offline" : status}</p>
          <p style="margin:2px 0 0;color:#94a3b8;font-size:11px">Device: ${deviceStatus} &bull; Cam: ${camStatus}</p>
        </div>`;

      let marker = markers.get(key);
      if (marker) {
        marker.setLatLng([lat, lng]);
        marker.setIcon(makeVehicleIcon(risk, status, speed, heading, isStale));
        marker.setPopupContent(popupHtml);
      } else {
        marker = L.marker([lat, lng], { icon: makeVehicleIcon(risk, status, speed, heading, isStale) })
          .addTo(map)
          .bindPopup(popupHtml, { closeButton: false });
        markers.set(key, marker);
      }
      // Rebind click each pass so it always carries the latest entity snapshot.
      const capturedEntity = entity;
      marker.off("click");
      marker.on("click", () => onSelectRef.current(capturedEntity));
    });

    // Drop markers for vehicles no longer present.
    for (const [key, marker] of markers) {
      if (!seen.has(key)) {
        marker.remove();
        markers.delete(key);
        coords.delete(key);
      }
    }

    // Auto-fit the view to the fleet only once, on first load — never on live updates,
    // which would yank the map around every few seconds.
    if (!hasFitRef.current && markers.size > 0) {
      try {
        const bounds = L.featureGroup([...markers.values()]).getBounds();
        if (bounds.isValid()) {
          map.fitBounds(bounds.pad(0.15), { maxZoom: 9, animate: false });
          hasFitRef.current = true;
        }
      } catch {
        // bounds may be invalid if all entities have identical coords
      }
    }
  }, [entities]);

  // Pan/zoom to a specific vehicle when the roster requests focus.
  useEffect(() => {
    const map = mapRef.current;
    if (!map || !focusId) return;
    const target = coordsRef.current.get(focusId);
    if (!target) return;
    map.flyTo(target, Math.max(map.getZoom(), 11), { duration: 0.6 });
    const marker = markersRef.current.get(focusId);
    marker?.openPopup();
  }, [focusId]);

  // Update geofence overlays when geofences change
  useEffect(() => {
    const map = mapRef.current;
    if (!map) return;

    geofenceLayersRef.current.forEach((c) => c.remove());
    geofenceLayersRef.current = [];

    geofences.forEach((zone, index) => {
      const circle = makeGeofenceCircle(zone, index);
      if (circle) {
        circle.addTo(map).bindTooltip(String(zone.name ?? "Zone"), { permanent: false, direction: "center" });
        geofenceLayersRef.current.push(circle);
      }
    });
  }, [geofences]);

  return (
    <div
      ref={containerRef}
      style={{ height: "100%", width: "100%" }}
      // Override leaflet default font so it matches app
      className="[&_.leaflet-popup-content-wrapper]:rounded-xl [&_.leaflet-popup-content-wrapper]:shadow-lg"
    />
  );
}
