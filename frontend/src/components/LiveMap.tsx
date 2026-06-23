import { useEffect, useRef } from "react";
import L from "leaflet";
import "leaflet/dist/leaflet.css";
import type { AnyRecord } from "@/types";

// Realistic US logistics corridor demo coordinates (used when backend has no GPS data)
const DEMO_COORDS: [number, number][] = [
  [33.749, -84.388],    // Atlanta
  [32.779, -96.808],    // Dallas
  [41.878, -87.629],    // Chicago
  [39.742, -104.984],   // Denver
  [34.052, -118.243],   // Los Angeles
  [29.760, -95.369],    // Houston
  [35.467, -97.516],    // Oklahoma City
  [36.174, -86.767],    // Nashville
  [39.952, -75.165],    // Philadelphia
  [30.332, -81.655],    // Jacksonville
  [44.977, -93.265],    // Minneapolis
  [47.608, -122.335],   // Seattle
  [37.338, -121.886],   // San Jose
  [38.627, -90.197],    // St. Louis
  [36.162, -86.781],    // Nashville alt
  [43.048, -76.147],    // Syracuse
];

function getCoords(entity: AnyRecord, index: number): [number, number] {
  const lat = Number(entity.lat ?? entity.latitude);
  const lng = Number(entity.lng ?? entity.longitude);
  if (!isNaN(lat) && !isNaN(lng) && lat !== 0 && lng !== 0) return [lat, lng];
  // Deterministic jitter so vehicles don't stack
  const base = DEMO_COORDS[index % DEMO_COORDS.length];
  const spread = (index * 0.037) % 0.45;
  const angle = (index * 1.618) % (2 * Math.PI);
  return [base[0] + spread * Math.sin(angle), base[1] + spread * Math.cos(angle)];
}

function makeVehicleIcon(risk: string, status: string): L.DivIcon {
  const isMoving = /active|on route|moving/i.test(status);
  const color =
    /high|critical/i.test(risk) ? "#ef4444" :
    /medium|warning/i.test(risk) ? "#f59e0b" :
    isMoving ? "#14b8a6" : "#6366f1";

  return L.divIcon({
    className: "",
    html: `<div style="
      position:relative;width:16px;height:16px;
      border-radius:50%;background:${color};
      border:2.5px solid white;
      box-shadow:0 1px 6px rgba(0,0,0,0.35);
      ${isMoving ? `animation:opstrax-pulse 2s ease-in-out infinite;` : ""}
    "></div>`,
    iconSize: [16, 16],
    iconAnchor: [8, 8],
    popupAnchor: [0, -10],
  });
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
}

export function LiveMap({ entities, geofences, onSelect }: LiveMapProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  const mapRef = useRef<L.Map | null>(null);
  const markersRef = useRef<L.Marker[]>([]);
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

    // Inject pulse animation keyframes once
    if (!document.getElementById("opstrax-map-styles")) {
      const style = document.createElement("style");
      style.id = "opstrax-map-styles";
      style.textContent = `
        @keyframes opstrax-pulse {
          0%,100%{box-shadow:0 1px 6px rgba(0,0,0,.35),0 0 0 0 rgba(20,184,166,.5)}
          50%{box-shadow:0 1px 6px rgba(0,0,0,.35),0 0 0 6px rgba(20,184,166,0)}
        }
      `;
      document.head.appendChild(style);
    }

    mapRef.current = map;

    return () => {
      map.remove();
      mapRef.current = null;
    };
  }, []);

  // Update vehicle markers when entities change
  useEffect(() => {
    const map = mapRef.current;
    if (!map) return;

    markersRef.current.forEach((m) => m.remove());
    markersRef.current = [];

    const validMarkers: L.Marker[] = [];

    entities.forEach((entity, index) => {
      const [lat, lng] = getCoords(entity, index);
      const risk = String(entity.riskLevel ?? entity.risk_level ?? "Low");
      const status = String(entity.status ?? "");
      const label = String(entity.label ?? entity.vehicleCode ?? entity.vehicle_code ?? "Vehicle");
      const driver = String(entity.driverName ?? entity.driver_name ?? "Unassigned");
      const speed = entity.speedMph ?? entity.speed_mph;
      const deviceStatus = String(entity.deviceStatus ?? entity.device_status ?? "--");
      const camStatus = String(entity.cameraStatus ?? entity.camera_status ?? "--");

      const marker = L.marker([lat, lng], { icon: makeVehicleIcon(risk, status) })
        .addTo(map)
        .bindPopup(
          `<div style="font-family:system-ui;font-size:12px;min-width:190px;line-height:1.5">
            <p style="font-weight:700;margin:0 0 2px;font-size:13px">${label}</p>
            <p style="margin:0;color:#475569">${driver}</p>
            <p style="margin:2px 0 0;color:#64748b">${speed != null ? `${speed} mph &bull; ` : ""}${status}</p>
            <p style="margin:2px 0 0;color:#94a3b8;font-size:11px">Device: ${deviceStatus} &bull; Cam: ${camStatus}</p>
          </div>`,
          { closeButton: false }
        );

      const capturedEntity = entity;
      marker.on("click", () => onSelectRef.current(capturedEntity));
      markersRef.current.push(marker);
      validMarkers.push(marker);
    });

    if (validMarkers.length > 0) {
      try {
        const group = L.featureGroup(validMarkers);
        const bounds = group.getBounds();
        if (bounds.isValid()) {
          map.fitBounds(bounds.pad(0.15), { maxZoom: 9, animate: true, duration: 0.5 });
        }
      } catch {
        // bounds may be invalid if all entities have identical coords
      }
    }
  }, [entities]);

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
