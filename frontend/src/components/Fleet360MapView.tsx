import { useMemo, useState } from "react";
import {
  AlertTriangle,
  BatteryCharging,
  Bell,
  Camera,
  CheckCircle2,
  ClipboardCheck,
  Download,
  Eye,
  Fuel,
  Gauge,
  MapPin,
  MessageSquare,
  Phone,
  RadioTower,
  Route,
  Search,
  Share2,
  ShieldAlert,
  Snowflake,
  Sparkles,
  Thermometer,
  Timer,
  Truck,
  User,
  Wrench,
  Zap,
} from "lucide-react";
import { AiInsightCard, EmptyState, KpiCard, PageHeader, RiskBadge, StatusBadge } from "@/components/ui";
import { developmentFleetSeedData } from "@/data/developmentFleetSeedData";
import type { AnyRecord } from "@/types";
import { calculateProfitability, formatCurrency } from "@/utils/formatters";

const {
  bookings,
  contracts,
  customers,
  devices,
  drivers,
  incidents,
  invoices,
  maintenance,
  shipments,
  supportTickets,
  vehicles,
} = developmentFleetSeedData;

type ViewMode = "Internal Operations View" | "Customer View";
type FleetEntity = ReturnType<typeof buildFleetEntities>[number];

const mapLayers = [
  "Vehicles",
  "Drivers",
  "Shipments",
  "Trailers",
  "Assets",
  "Cold chain sensors",
  "AI dashcam events",
  "Incidents",
  "Geofences",
  "Routes",
  "Traffic",
  "Weather",
  "Customer locations",
  "Warehouses / hubs",
  "Fuel stations",
  "Service workshops",
];

const positions: Record<string, { left: number; top: number; lat: number; lng: number; heading: string; speed: string; engineHours: number; fuelLevel: number; batteryVoltage: string; lastGpsUpdate: string }> = {
  "KSA-REEFER-214": { left: 72, top: 55, lat: 24.7136, lng: 46.6753, heading: "E", speed: "82 km/h", engineHours: 2950, fuelLevel: 72, batteryVoltage: "12.7V", lastGpsUpdate: "38 sec ago" },
  "KSA-REEFER-119": { left: 44, top: 31, lat: 21.4858, lng: 39.1925, heading: "NE", speed: "0 km/h", engineHours: 5120, fuelLevel: 48, batteryVoltage: "12.3V", lastGpsUpdate: "2 min ago" },
  "DXB-VAN-045": { left: 24, top: 72, lat: 25.2048, lng: 55.2708, heading: "SW", speed: "0 km/h", engineHours: 2140, fuelLevel: 64, batteryVoltage: "12.9V", lastGpsUpdate: "1 min ago" },
  "BOX-106": { left: 78, top: 60, lat: 38.9072, lng: -77.0369, heading: "NE", speed: "18 mph", engineHours: 7410, fuelLevel: 29, batteryVoltage: "11.9V", lastGpsUpdate: "8 min ago" },
};

const hubs = [
  { name: "Riyadh DC", left: 68, top: 50, type: "Warehouse / hub" },
  { name: "Jeddah Pharma Hub", left: 39, top: 29, type: "Cold chain hub" },
  { name: "Dubai FC", left: 22, top: 77, type: "Fulfillment center" },
  { name: "Manassas Yard", left: 74, top: 66, type: "Service yard" },
  { name: "Washington DC Customer Zone", left: 84, top: 55, type: "Customer site" },
];

const geofences = [
  { name: "Riyadh Dispatch Zone", left: 66, top: 48, width: 145, height: 95, color: "border-blue-400 bg-blue-400/10" },
  { name: "Jeddah Cold Chain Zone", left: 34, top: 24, width: 130, height: 90, color: "border-purple-400 bg-purple-400/10" },
  { name: "DC Delay / Safety Zone", left: 72, top: 51, width: 165, height: 110, color: "border-red-400 bg-red-400/10" },
];

const liveEvents = [
  { time: "04:12", severity: "High", type: "shipment.delayed", entity: "SHP-6204", body: "Shipment delayed by 18 minutes near Washington DC.", action: "Send ETA" },
  { time: "04:10", severity: "Critical", type: "temperature.breach", entity: "KSA-REEFER-119", body: "Reefer temperature exceeded threshold for 6 minutes.", action: "Open evidence" },
  { time: "04:08", severity: "Medium", type: "driver.hos", entity: "DRV-KSA-302", body: "Bilal Ansari has 42 minutes HOS buffer remaining.", action: "Review HOS" },
  { time: "04:06", severity: "Low", type: "pod.uploaded", entity: "SHP-6203", body: "POD uploaded for DesertCart final-mile delivery.", action: "Invoice" },
  { time: "04:04", severity: "High", type: "device.offline", entity: "CAM-VA-106", body: "AI dashcam has not uploaded since 08:10.", action: "Create task" },
];

const whatIfCandidates = [
  { vehicle: "KSA-REEFER-214", eta: "38 min", cost: "SAR 610", risk: "Low", margin: "27%", reason: "Reefer capable, strong driver score, device online" },
  { vehicle: "KSA-REEFER-119", eta: "12 min", cost: "SAR 420", risk: "High", margin: "19%", reason: "Closest, but HOS and temperature watch" },
  { vehicle: "DXB-VAN-045", eta: "44 min", cost: "AED 260", risk: "Medium", margin: "22%", reason: "Available, but wrong vehicle type for cold-chain load" },
];

function findContract(customerName?: unknown) {
  return contracts.find((contract) => String(contract.customer) === String(customerName));
}

function buildFleetEntities() {
  return vehicles.map((vehicle) => {
    const vehicleId = String(vehicle.vehicleId);
    const shipment = shipments.find((item) => item.vehicle === vehicleId);
    const booking = shipment ? bookings.find((item) => item.bookingId === shipment.bookingId) : undefined;
    const driver = drivers.find((item) => item.name === vehicle.assignedDriver || item.assignedVehicle === vehicleId);
    const device = devices.find((item) => item.linkedVehicleTrailer === vehicleId || item.deviceId === vehicle.assignedDevice);
    const contract = findContract(shipment?.customer);
    const invoice = shipment ? invoices.find((item) => String(item.shipmentBooking) === shipment.shipmentId) : undefined;
    const openTicket = shipment ? supportTickets.find((ticket) => ticket.shipment === shipment.shipmentId) : undefined;
    const workOrder = maintenance.find((item) => item.vehicle === vehicleId);
    const incident = incidents.find((item) => item.vehicle === vehicleId || item.shipment === shipment?.shipmentId);
    const position = positions[vehicleId] ?? { left: 50, top: 50, lat: 0, lng: 0, heading: "N", speed: "0", engineHours: 0, fuelLevel: 0, batteryVoltage: "--", lastGpsUpdate: "Unknown" };
    const profit = shipment ? calculateProfitability(Number(shipment.revenue), Number(shipment.cost)) : undefined;
    const isColdChain = String(vehicle.vehicleType).includes("Reefer") || String(shipment?.slaRisk).includes("Temperature") || String(booking?.cargoType).match(/Vaccines|Fresh|Cold/i);
    const isDelayed = String(shipment?.currentStatus).match(/Delayed/i) || String(shipment?.delayRisk).match(/High/i);
    const deviceOffline = Boolean(device && !String(device.status).match(/Online/i));
    const maintenanceRisk = String(vehicle.maintenanceStatus).match(/Critical|Due|Maintenance/i);
    const hosRisk = String(driver?.hosStatus).match(/Risk|Violation/i);
    const marginRisk = Boolean(profit && profit.margin < 20);
    const alertCount = [isDelayed, deviceOffline, maintenanceRisk, hosRisk, marginRisk, String(incident?.severity).match(/High|Critical/i)].filter(Boolean).length;
    const severity = alertCount >= 3 || String(incident?.severity).match(/Critical/i) ? "Critical" : alertCount >= 2 ? "High" : alertCount === 1 ? "Medium" : "Low";
    const dutyStatus = driver?.availability === "Available" ? "Driving" : driver?.availability === "Idle" ? "Off Duty" : driver?.availability === "At Pickup" ? "On Duty" : "HOS Risk";
    const shipmentStatus = shipment?.currentStatus === "At Pickup" ? "At Pickup" : shipment?.currentStatus === "In Transit" ? "In Transit" : shipment?.currentStatus === "Delayed" ? "Delayed" : shipment?.currentStatus === "Delivered" ? "Delivered" : "Awaiting Dispatch";

    return {
      id: vehicleId,
      vehicle,
      shipment,
      booking,
      driver,
      device,
      contract,
      invoice,
      openTicket,
      workOrder,
      incident,
      position,
      profit,
      dutyStatus,
      shipmentStatus,
      isColdChain,
      isDelayed,
      deviceOffline,
      maintenanceRisk,
      hosRisk,
      marginRisk,
      alertCount,
      severity,
      etaConfidence: severity === "Critical" ? 54 : severity === "High" ? 68 : severity === "Medium" ? 82 : 94,
      delayProbability: severity === "Critical" ? 72 : severity === "High" ? 48 : severity === "Medium" ? 24 : 8,
      stops: shipment
        ? [
            { label: "Pickup", location: String(shipment.origin), status: shipment.currentStatus === "At Pickup" ? "Active" : "Completed", time: "08:00-09:30" },
            { label: "In-transit checkpoint", location: shipment.origin === "Jeddah" ? "Taif corridor" : shipment.origin === "Manassas" ? "I-395 corridor" : "Route midpoint", status: shipment.currentStatus === "In Transit" || shipment.currentStatus === "Delayed" ? "Active" : "Pending", time: "Live" },
            { label: "Delivery", location: String(shipment.destination), status: shipment.currentStatus === "Delivered" ? "Completed" : "Pending", time: String(shipment.eta) },
          ]
        : [
            { label: "Current location", location: String(vehicle.currentLocation), status: "Idle", time: "Now" },
            { label: "Next assignment", location: "Unassigned", status: "Pending", time: "Pending" },
          ],
      violations: [
        ...(isDelayed ? ["Late shipment / SLA risk"] : []),
        ...(deviceOffline ? [`Device blind spot: ${String(device?.status)}`] : []),
        ...(maintenanceRisk ? [`Maintenance risk: ${String(vehicle.maintenanceStatus)}`] : []),
        ...(hosRisk ? [`Driver HOS risk: ${String(driver?.hosStatus)}`] : []),
        ...(marginRisk ? [`Margin at risk: ${profit?.marginText ?? "--"}`] : []),
        ...(isColdChain && String(shipment?.slaRisk).includes("Temperature") ? ["Temperature compliance watch"] : []),
      ],
    };
  });
}

function markerColor(entity: FleetEntity) {
  if (entity.deviceOffline) return "bg-slate-500";
  if (entity.severity === "Critical") return "bg-red-600";
  if (entity.maintenanceRisk) return "bg-orange-500";
  if (entity.isColdChain) return "bg-purple-600";
  if (entity.isDelayed) return "bg-yellow-500";
  if (String(entity.vehicle.status).match(/Idle/i)) return "bg-blue-600";
  return "bg-emerald-600";
}

function FleetTopKpiBar({ entities, setQuickFilter }: { entities: FleetEntity[]; setQuickFilter: (filter: string) => void }) {
  const activeShipments = entities.filter((entity) => entity.shipment && entity.shipment.currentStatus !== "Delivered");
  const revenueAtRisk = entities.filter((entity) => entity.marginRisk || entity.isDelayed).reduce((sum, entity) => sum + Number(entity.shipment?.revenue ?? 0), 0);
  const kpis = [
    ["Total Fleet", entities.length, "All"],
    ["Active Vehicles", entities.filter((entity) => String(entity.vehicle.status).match(/Active/i)).length, "Active"],
    ["Idle Vehicles", entities.filter((entity) => String(entity.vehicle.status).match(/Idle/i)).length, "Idle"],
    ["In Maintenance", entities.filter((entity) => String(entity.vehicle.status).match(/Maintenance/i)).length, "Maintenance"],
    ["Out of Service", 0, "Out of Service"],
    ["Active Shipments", activeShipments.length, "Active Shipments"],
    ["Delayed Shipments", entities.filter((entity) => entity.isDelayed).length, "Delayed"],
    ["Available Drivers", drivers.filter((driver) => String(driver.availability).match(/Available|Idle/i)).length, "Available Drivers"],
    ["HOS Risk Drivers", entities.filter((entity) => entity.hosRisk).length, "HOS Risk"],
    ["Critical Alerts", entities.filter((entity) => entity.severity === "Critical").length, "Critical"],
    ["Temperature Breaches", entities.filter((entity) => entity.isColdChain && entity.violations.some((violation) => violation.includes("Temperature"))).length, "Cold Chain"],
    ["Safety Events", incidents.length, "Safety"],
    ["Devices Offline", entities.filter((entity) => entity.deviceOffline).length, "Device Risk"],
    ["Revenue at Risk", formatCurrency(revenueAtRisk, "SAR"), "Margin Risk"],
    ["On-Time Delivery %", "94.6%", "On Time"],
  ];
  return (
    <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-5">
      {kpis.map(([label, value, filter]) => (
        <button key={String(label)} className="text-left" onClick={() => setQuickFilter(String(filter))}>
          <KpiCard label={String(label)} value={String(value)} status={String(label).match(/Risk|Critical|Delayed|Offline|Breach|Maintenance/) ? "Risk" : "Active"} />
        </button>
      ))}
    </div>
  );
}

function CustomerMapMode({ mode, setMode, customerScope, setCustomerScope }: { mode: ViewMode; setMode: (mode: ViewMode) => void; customerScope: string; setCustomerScope: (value: string) => void }) {
  return (
    <div className="panel flex flex-col gap-3 p-3 lg:flex-row lg:items-center lg:justify-between">
      <div className="flex flex-wrap gap-2">
        {(["Internal Operations View", "Customer View"] as ViewMode[]).map((item) => (
          <button key={item} className={mode === item ? "btn-primary py-2 text-xs" : "btn-ghost py-2 text-xs"} onClick={() => setMode(item)}>
            {item}
          </button>
        ))}
      </div>
      {mode === "Customer View" && (
        <label className="flex items-center gap-2 text-sm font-semibold text-slate-700">
          Customer scope
          <select className="field h-9 w-72 py-0" value={customerScope} onChange={(event) => setCustomerScope(event.target.value)}>
            {customers.map((customer) => <option key={String(customer.companyName)}>{String(customer.companyName)}</option>)}
          </select>
        </label>
      )}
    </div>
  );
}

function MapLayerToggle({ layers, setLayers }: { layers: Set<string>; setLayers: (layers: Set<string>) => void }) {
  const toggle = (layer: string) => {
    const next = new Set(layers);
    if (next.has(layer)) next.delete(layer);
    else next.add(layer);
    setLayers(next);
  };
  return (
    <div className="panel p-4">
      <p className="section-title">Map Layers</p>
      <div className="mt-3 grid grid-cols-2 gap-2">
        {mapLayers.map((layer) => (
          <button key={layer} className={`rounded-lg border px-2.5 py-2 text-left text-xs font-semibold transition ${layers.has(layer) ? "border-blue-200 bg-blue-50 text-blue-700" : "border-slate-100 bg-white text-slate-500"}`} onClick={() => toggle(layer)}>
            {layer}
          </button>
        ))}
      </div>
    </div>
  );
}

function MapFilterPanel({
  search,
  setSearch,
  quickFilter,
  setQuickFilter,
  toggles,
  setToggles,
  layers,
  setLayers,
}: {
  search: string;
  setSearch: (value: string) => void;
  quickFilter: string;
  setQuickFilter: (value: string) => void;
  toggles: Set<string>;
  setToggles: (value: Set<string>) => void;
  layers: Set<string>;
  setLayers: (value: Set<string>) => void;
}) {
  const filterGroups = [
    ["Vehicle status", ["All", "Active", "Idle", "In Transit", "Loading", "Unloading", "Maintenance", "Out of Service", "Offline"]],
    ["Shipment status", ["Awaiting Dispatch", "En Route to Pickup", "At Pickup", "Loaded", "In Transit", "Delayed", "Arrived Delivery", "Delivered"]],
    ["Vehicle type", ["Reefer Truck", "Dry Van", "Flatbed", "Box Truck", "Last Mile Van", "Trailer"]],
    ["Driver duty/HOS", ["Available", "Driving", "On Duty", "Off Duty", "Sleeper", "HOS Risk", "Violation"]],
    ["Alert severity", ["Critical", "High", "Medium", "Low"]],
    ["Region / city", ["Riyadh", "Jeddah", "Dammam", "Dubai", "Abu Dhabi", "Doha", "Manassas", "Washington DC", "Toronto", "Chicago"]],
  ];
  const toggleItems = ["Cold chain only", "Delayed only", "Safety events only", "Devices offline only", "Available for dispatch", "Profitable / margin risk"];
  const toggleItem = (item: string) => {
    const next = new Set(toggles);
    if (next.has(item)) next.delete(item);
    else next.add(item);
    setToggles(next);
  };
  return (
    <aside className="space-y-4">
      <div className="panel p-4">
        <p className="section-title">Fleet Filters</p>
        <div className="relative mt-3">
          <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
          <input className="field pl-10" value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Vehicle, driver, shipment, customer..." />
        </div>
        <div className="mt-3 rounded-xl border border-violet-100 bg-violet-50 p-3">
          <p className="text-xs font-bold uppercase tracking-[0.16em] text-violet-700">Natural language search</p>
          <p className="mt-1 text-xs text-violet-700/80">Try: “Show delayed cold-chain loads near Riyadh” or “Find vehicles with offline devices”.</p>
        </div>
      </div>
      {filterGroups.map(([title, items]) => (
        <div key={String(title)} className="panel p-4">
          <p className="section-title">{String(title)}</p>
          <div className="mt-3 flex flex-wrap gap-2">
            {(items as string[]).map((item) => (
              <button key={item} className={quickFilter === item ? "btn-primary py-1.5 text-xs" : "btn-ghost py-1.5 text-xs"} onClick={() => setQuickFilter(item)}>
                {item}
              </button>
            ))}
          </div>
        </div>
      ))}
      <div className="panel p-4">
        <p className="section-title">Operational Toggles</p>
        <div className="mt-3 space-y-2">
          {toggleItems.map((item) => (
            <label key={item} className="flex items-center gap-2 rounded-lg border border-slate-100 bg-white px-3 py-2 text-sm font-semibold text-slate-600">
              <input type="checkbox" checked={toggles.has(item)} onChange={() => toggleItem(item)} />
              {item}
            </label>
          ))}
        </div>
      </div>
      <MapLayerToggle layers={layers} setLayers={setLayers} />
    </aside>
  );
}

function MapMarker({ entity, isSelected, onClick, customerMode }: { entity: FleetEntity; isSelected: boolean; onClick: () => void; customerMode: boolean }) {
  const publicVehicle = customerMode ? `${String(entity.vehicle.vehicleType)} ${String(entity.vehicle.plateNumber).slice(-3)}` : String(entity.vehicle.vehicleId);
  return (
    <button
      className="absolute z-20 text-left"
      style={{ left: `${entity.position.left}%`, top: `${entity.position.top}%` }}
      onClick={onClick}
      title={`${String(entity.vehicle.vehicleId)} · ${String(entity.vehicle.plateNumber)} · ${String(entity.driver?.name ?? "--")} · ${String(entity.shipment?.shipmentId ?? "No active shipment")} · ETA ${String(entity.shipment?.eta ?? "--")} · ${entity.position.speed} · Updated ${entity.position.lastGpsUpdate}`}
    >
      <div className={`relative rounded-full border-2 border-white p-2 shadow-xl ${markerColor(entity)} ${isSelected ? "ring-4 ring-blue-200" : ""}`}>
        <Truck className="h-5 w-5 text-white" />
        <span className="absolute -right-1 -top-1 grid h-4 w-4 place-items-center rounded-full bg-white text-[9px] font-black text-slate-900 shadow">{entity.alertCount}</span>
        <span className="absolute -bottom-1 -left-1 rounded-full border border-white bg-white px-1 text-[9px] font-black text-slate-700">{entity.position.heading}</span>
      </div>
      <div className={`mt-2 min-w-[178px] rounded-xl border bg-white/95 px-3 py-2 shadow-lg ${isSelected ? "border-blue-300" : "border-slate-200"}`}>
        <div className="flex items-center justify-between gap-2">
          <p className="text-xs font-extrabold text-slate-900">{publicVehicle}</p>
          {entity.isColdChain && <span className="rounded-full bg-purple-100 px-1.5 py-0.5 text-[9px] font-bold text-purple-700">{String(entity.shipment?.slaRisk).includes("Temperature") ? "TEMP" : "REEFER"}</span>}
        </div>
        <p className="mt-1 text-[11px] text-slate-500">{entity.shipmentStatus} · {String(entity.shipment?.eta ?? "No ETA")}</p>
      </div>
    </button>
  );
}

function RiskHeatmapLegend() {
  return (
    <div className="absolute left-5 top-5 z-30 rounded-2xl border border-white/80 bg-white/95 p-4 shadow-sm">
      <p className="section-title">Risk Heatmap</p>
      <div className="mt-3 grid gap-2 text-xs text-slate-600">
        {[
          ["Delay risk", "bg-red-500/20 border-red-400"],
          ["Accident-prone zone", "bg-orange-500/20 border-orange-400"],
          ["Temperature breach risk", "bg-purple-500/20 border-purple-400"],
          ["SLA risk", "bg-amber-500/20 border-amber-400"],
        ].map(([label, cls]) => (
          <div key={label} className="flex items-center gap-2">
            <span className={`h-3 w-6 rounded border ${cls}`} />
            {label}
          </div>
        ))}
      </div>
    </div>
  );
}

function MapCanvas({ entities, selectedId, setSelectedId, layers, mode }: { entities: FleetEntity[]; selectedId: string; setSelectedId: (id: string) => void; layers: Set<string>; mode: ViewMode }) {
  const showVehicles = layers.has("Vehicles");
  return (
    <div className="map-surface relative min-h-[560px] overflow-hidden p-5 lg:min-h-[640px]">
      <svg className="absolute inset-0 h-full w-full" viewBox="0 0 1200 760" preserveAspectRatio="none">
        {layers.has("Routes") && (
          <>
            <path d="M70 555 C230 370 350 455 505 275 S850 155 1110 410" fill="none" stroke="#2563eb" strokeWidth="5" strokeDasharray="12 12" opacity=".74" />
            <path d="M140 155 C330 195 455 410 690 355 S890 530 1100 175" fill="none" stroke="#0d9488" strokeWidth="5" opacity=".62" />
            <path d="M185 615 C350 535 430 590 585 515 S820 450 1010 575" fill="none" stroke="#7c3aed" strokeWidth="4" opacity=".45" />
          </>
        )}
        {layers.has("Risk Heatmap") && (
          <>
            <circle cx="860" cy="415" r="116" fill="#ef4444" opacity=".10" stroke="#ef4444" strokeWidth="2" />
            <circle cx="470" cy="235" r="85" fill="#a855f7" opacity=".12" stroke="#a855f7" strokeWidth="2" />
            <circle cx="705" cy="348" r="75" fill="#f59e0b" opacity=".12" stroke="#f59e0b" strokeWidth="2" />
          </>
        )}
      </svg>

      {layers.has("Risk Heatmap") && <RiskHeatmapLegend />}

      {layers.has("Geofences") && geofences.map((zone) => (
        <div key={zone.name} className={`absolute rounded-[28px] border-2 ${zone.color}`} style={{ left: `${zone.left}%`, top: `${zone.top}%`, width: zone.width, height: zone.height }}>
          <span className="absolute -top-3 left-4 rounded-full bg-white px-2 py-0.5 text-[10px] font-bold text-slate-600 shadow-sm">{zone.name}</span>
        </div>
      ))}

      {layers.has("Warehouses / hubs") && hubs.map((hub) => (
        <div key={hub.name} className="absolute z-10" style={{ left: `${hub.left}%`, top: `${hub.top}%` }}>
          <div className="rounded-xl border border-slate-200 bg-white/95 px-2 py-1 text-[10px] font-bold text-slate-700 shadow-sm">
            <MapPin className="mr-1 inline h-3 w-3 text-blue-700" /> {hub.name}
          </div>
        </div>
      ))}

      {showVehicles && entities.map((entity) => (
        <MapMarker key={entity.id} entity={entity} isSelected={selectedId === entity.id} onClick={() => setSelectedId(entity.id)} customerMode={mode === "Customer View"} />
      ))}

      {layers.has("Incidents") && entities.filter((entity) => entity.incident).map((entity) => (
        <div key={`incident-${entity.id}`} className="absolute z-10 rounded-full border-2 border-white bg-red-600 p-1.5 shadow-lg" style={{ left: `${entity.position.left + 3}%`, top: `${entity.position.top + 5}%` }}>
          <ShieldAlert className="h-4 w-4 text-white" />
        </div>
      ))}

      {layers.has("Cold chain sensors") && entities.filter((entity) => entity.isColdChain).map((entity) => (
        <div key={`temp-${entity.id}`} className="absolute z-10 rounded-full border-2 border-white bg-purple-600 p-1.5 shadow-lg" style={{ left: `${entity.position.left - 3}%`, top: `${entity.position.top + 4}%` }}>
          <Thermometer className="h-4 w-4 text-white" />
        </div>
      ))}

      <div className="absolute bottom-5 left-5 right-5 z-30 grid gap-3 lg:grid-cols-3">
        {entities.filter((entity) => entity.alertCount > 0).slice(0, 3).map((entity) => (
          <button key={`alert-card-${entity.id}`} className="rounded-xl border border-white/80 bg-white/95 p-3 text-left shadow-sm" onClick={() => setSelectedId(entity.id)}>
            <div className="flex items-start justify-between gap-2">
              <p className="text-sm font-bold text-slate-900">{entity.id} · {entity.violations[0]}</p>
              <RiskBadge risk={entity.severity} />
            </div>
            <p className="mt-1 text-xs text-slate-500">{String(entity.vehicle.currentLocation)} · {entity.position.lastGpsUpdate}</p>
          </button>
        ))}
      </div>
    </div>
  );
}

function PublicField({ label, value }: { label: string; value: unknown }) {
  return (
    <div className="rounded-xl border border-slate-100 bg-slate-50 p-3">
      <p className="text-xs text-slate-500">{label}</p>
      <p className="mt-1 font-bold text-slate-900">{String(value ?? "--")}</p>
    </div>
  );
}

function Vehicle360Drawer({ entity, mode }: { entity: FleetEntity; mode: ViewMode }) {
  const customerMode = mode === "Customer View";
  const publicDriverName = String(entity.driver?.name ?? "Assigned driver").split(" ")[0];
  const margin = entity.profit?.marginText ?? "--";
  return (
    <aside className="space-y-5">
      <div className="panel p-5">
        <div className="flex items-start justify-between gap-3">
          <div>
            <p className="section-title">Vehicle 360 Summary</p>
            <h2 className="mt-2 text-2xl font-bold text-slate-900">{customerMode ? `${String(entity.vehicle.vehicleType)} ${String(entity.vehicle.plateNumber).slice(-3)}` : entity.id}</h2>
            <p className="mt-1 text-sm text-slate-500">{customerMode ? "Public shipment vehicle" : `${String(entity.vehicle.plateNumber)} · ${String(entity.vehicle.makeModel)} · ${String(entity.vehicle.year)}`}</p>
          </div>
          <RiskBadge risk={entity.severity} />
        </div>
        <div className="mt-4 grid gap-3 sm:grid-cols-2">
          <PublicField label="Current status" value={entity.shipmentStatus} />
          <PublicField label="Current city/location" value={entity.vehicle.currentLocation} />
          <PublicField label="Last GPS update" value={entity.position.lastGpsUpdate} />
          <PublicField label="Speed / Heading" value={`${entity.position.speed} · ${entity.position.heading}`} />
          {!customerMode && <PublicField label="Odometer" value={Number(entity.vehicle.odometer).toLocaleString()} />}
          {!customerMode && <PublicField label="Engine hours" value={entity.position.engineHours.toLocaleString()} />}
          {!customerMode && <PublicField label="Fuel level" value={`${entity.position.fuelLevel}%`} />}
          {!customerMode && <PublicField label="Battery voltage" value={entity.position.batteryVoltage} />}
          {!customerMode && <PublicField label="Device health" value={entity.device?.status ?? "Unknown"} />}
          {!customerMode && <PublicField label="Maintenance" value={entity.vehicle.maintenanceStatus} />}
          {!customerMode && <PublicField label="Compliance" value={entity.vehicle.complianceStatus} />}
        </div>
      </div>

      <div className="panel p-5">
        <p className="section-title">Driver</p>
        <div className="mt-4 grid gap-3 sm:grid-cols-2">
          <PublicField label="Driver" value={customerMode ? publicDriverName : entity.driver?.name} />
          {!customerMode && <PublicField label="Phone" value={entity.driver?.phone} />}
          <PublicField label="Duty status" value={entity.dutyStatus} />
          {!customerMode && <PublicField label="HOS remaining" value={entity.hosRisk ? "42 min risk buffer" : "6h 18m"} />}
          {!customerMode && <PublicField label="Safety score" value={entity.driver?.safetyScore} />}
          {!customerMode && <PublicField label="License expiry" value={entity.driver?.licenseExpiry} />}
          {!customerMode && <PublicField label="Coaching status" value={entity.incident?.coachingRequired === "Yes" ? "Open" : "None"} />}
          {!customerMode && <PublicField label="Recent violations" value={entity.violations.length} />}
        </div>
      </div>

      <Shipment360Drawer entity={entity} customerMode={customerMode} margin={margin} />

      {entity.isColdChain && (
        <div className="panel p-5">
          <p className="section-title">Cold Chain</p>
          <div className="mt-4 grid gap-3 sm:grid-cols-2">
            <PublicField label="Set point" value="2-8°C" />
            <PublicField label="Current temperature" value={entity.severity === "Critical" ? "9.4°C" : "4.8°C"} />
            <PublicField label="Humidity" value="61%" />
            <PublicField label="Door status" value="Closed" />
            <PublicField label="Breach status" value={entity.severity === "Critical" ? "Active breach" : "Clear"} />
            <PublicField label="Breach duration" value={entity.severity === "Critical" ? "6 min" : "0 min"} />
            {!customerMode && <PublicField label="Reefer unit" value="Running" />}
            {!customerMode && <PublicField label="Compliance risk" value={String(entity.shipment?.slaRisk ?? "Low")} />}
          </div>
        </div>
      )}

      {!customerMode && (
        <>
          <div className="panel p-5">
            <p className="section-title">Safety & Telematics</p>
            <div className="mt-4 grid gap-3 sm:grid-cols-2">
              <PublicField label="Last safety event" value={entity.incident?.incidentType ?? "None"} />
              <PublicField label="Harsh braking" value={entity.incident ? 2 : 0} />
              <PublicField label="Speeding" value={entity.severity === "High" ? 1 : 0} />
              <PublicField label="Distracted driving" value="No active event" />
              <PublicField label="AI dashcam clip" value={entity.incident ? "Available" : "No clip"} />
              <PublicField label="Driver-facing camera" value={entity.device?.type === "AI Dashcam" ? entity.device.status : "Linked"} />
              <PublicField label="Road-facing camera" value={entity.device?.type === "AI Dashcam" ? entity.device.status : "Linked"} />
              <PublicField label="ELD sync" value={entity.hosRisk ? "Stale sync" : "Synced"} />
              <PublicField label="Firmware" value={entity.device?.firmware ?? "--"} />
              <PublicField label="Signal" value={entity.device?.signal ?? "--"} />
              <PublicField label="Battery" value={entity.device?.battery ?? "--"} />
              <PublicField label="Last heartbeat" value={entity.device?.lastHeartbeat ?? "--"} />
            </div>
          </div>

          <div className="panel p-5">
            <p className="section-title">Actions</p>
            <div className="mt-4 grid gap-2 sm:grid-cols-2">
              {[
                ["View Vehicle Details", Eye],
                ["View Shipment", ClipboardCheck],
                ["View Driver", User],
                ["View Route", Route],
                ["Call Driver", Phone],
                ["Message Driver", MessageSquare],
                ["Share Tracking Link", Share2],
                ["Create Geofence Here", MapPin],
                ["Create Incident", ShieldAlert],
                ["Create Work Order", Wrench],
                ["Reassign Vehicle", Truck],
                ["Dispatch New Load", Zap],
                ["Open Camera Clip", Camera],
                ["Download Trip Report", Download],
              ].map(([label, Icon]) => {
                const ActionIcon = Icon as typeof Eye;
                return (
                  <button key={String(label)} className="btn-ghost justify-start py-2 text-xs">
                    <ActionIcon className="h-3.5 w-3.5" /> {String(label)}
                  </button>
                );
              })}
            </div>
          </div>
        </>
      )}
    </aside>
  );
}

function Shipment360Drawer({ entity, customerMode, margin }: { entity: FleetEntity; customerMode: boolean; margin: string }) {
  if (!entity.shipment) {
    return (
      <div className="panel p-5">
        <p className="section-title">Current Load / Job</p>
        <EmptyState title="No active load" subtitle="This vehicle is available for dispatch or staging." />
      </div>
    );
  }
  return (
    <div className="panel p-5">
      <p className="section-title">Current Load / Job</p>
      <div className="mt-4 grid gap-3 sm:grid-cols-2">
        <PublicField label="Shipment ID" value={entity.shipment.shipmentId} />
        <PublicField label="Booking ID" value={entity.shipment.bookingId} />
        <PublicField label="Customer" value={entity.shipment.customer} />
        {!customerMode && <PublicField label="Contract" value={entity.contract?.contractId ?? entity.booking?.contract} />}
        <PublicField label="Cargo type" value={entity.shipment.cargoType} />
        <PublicField label="Weight / volume" value={entity.booking?.weight ?? "--"} />
        <PublicField label="Temperature requirement" value={entity.isColdChain ? "Required" : "Not required"} />
        <PublicField label="Pickup" value={entity.shipment.origin} />
        <PublicField label="Drop-off" value={entity.shipment.destination} />
        <PublicField label="Shipment status" value={entity.shipment.currentStatus} />
        <PublicField label="ETA" value={entity.shipment.eta} />
        <PublicField label="ETA confidence" value={`${entity.etaConfidence}% · ${entity.delayProbability}% delay probability`} />
        <PublicField label="SLA risk" value={entity.shipment.slaRisk} />
        {!customerMode && <PublicField label="Revenue" value={formatCurrency(Number(entity.shipment.revenue), String(entity.shipment.currency))} />}
        {!customerMode && <PublicField label="Estimated cost" value={formatCurrency(Number(entity.shipment.cost), String(entity.shipment.currency))} />}
        {!customerMode && <PublicField label="Estimated margin" value={margin} />}
        <PublicField label="Invoice status" value={customerMode ? "Hidden" : entity.shipment.invoiceStatus} />
        <PublicField label="POD status" value={entity.shipment.podStatus} />
      </div>
      <div className="mt-5">
        <p className="section-title">Route Progress</p>
        <div className="mt-3 space-y-3">
          {entity.stops.map((stop) => (
            <div key={`${stop.label}-${stop.location}`} className="flex gap-3 rounded-xl border border-slate-100 bg-white p-3">
              <span className={`mt-1 h-3 w-3 rounded-full ${stop.status === "Completed" ? "bg-emerald-500" : stop.status === "Active" ? "bg-blue-600" : "bg-slate-300"}`} />
              <div className="min-w-0 flex-1">
                <div className="flex items-center justify-between gap-2">
                  <p className="font-semibold text-slate-900">{stop.label}</p>
                  <StatusBadge status={stop.status} />
                </div>
                <p className="mt-1 text-sm text-slate-500">{stop.location} · {stop.time}</p>
              </div>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}

function AIRecommendationPanel({ selected }: { selected: FleetEntity }) {
  const actions = [
    selected.isDelayed ? "Notify customer with updated ETA and reason code." : "Keep ETA confidence active and monitor next stop variance.",
    selected.maintenanceRisk ? "Schedule maintenance immediately after delivery and block new dispatch." : "No maintenance block; eligible for next load after delivery.",
    selected.deviceOffline ? "Create telematics support task for device visibility gap." : "Device data quality supports live tracking and customer share.",
    selected.marginRisk ? "Review lane surcharge, delay cost and detention terms to protect margin." : "Margin is currently protected.",
    selected.isColdChain ? "Watch temperature trend and prepare compliance evidence package." : "No cold-chain action required.",
  ];
  return (
    <div className="panel p-5">
      <p className="section-title">Next Best Action Panel</p>
      <div className="mt-4 space-y-3">
        {actions.map((action, index) => (
          <div key={action} className="rounded-xl border border-violet-100 bg-violet-50/70 p-3">
            <div className="flex items-start gap-2">
              <Sparkles className="mt-0.5 h-4 w-4 flex-shrink-0 text-violet-600" />
              <div>
                <p className="text-sm font-bold text-slate-900">AI suggestion {index + 1}</p>
                <p className="mt-1 text-sm text-slate-600">{action}</p>
              </div>
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

function WhatIfDispatchSimulation() {
  return (
    <div className="panel p-5">
      <p className="section-title">What-If Dispatch Simulation</p>
      <div className="mt-4 rounded-xl border border-slate-100 bg-slate-50 p-3">
        <p className="text-sm font-bold text-slate-900">Unassigned booking: BK-8102 · Al Noor Pharma</p>
        <p className="mt-1 text-xs text-slate-500">Compare top candidate vehicles by ETA, cost, risk and margin.</p>
      </div>
      <div className="mt-3 space-y-2">
        {whatIfCandidates.map((candidate) => (
          <div key={candidate.vehicle} className="rounded-xl border border-slate-100 bg-white p-3">
            <div className="flex items-center justify-between">
              <p className="font-semibold text-slate-900">{candidate.vehicle}</p>
              <RiskBadge risk={candidate.risk} />
            </div>
            <p className="mt-1 text-xs text-slate-500">ETA {candidate.eta} · Cost {candidate.cost} · Margin {candidate.margin}</p>
            <p className="mt-2 text-xs text-slate-600">{candidate.reason}</p>
          </div>
        ))}
      </div>
    </div>
  );
}

function LiveIncidentReplay() {
  return (
    <div className="panel p-5">
      <p className="section-title">Live Incident Replay</p>
      <div className="mt-4 h-2 rounded-full bg-slate-100">
        <div className="h-2 w-[68%] rounded-full bg-gradient-to-r from-blue-600 via-amber-400 to-red-500" />
      </div>
      <div className="mt-4 grid gap-2 text-xs text-slate-600 sm:grid-cols-4">
        <span>Speed timeline</span>
        <span>Stop markers</span>
        <span>Camera events</span>
        <span>Temperature graph</span>
      </div>
    </div>
  );
}

function AlertFeed() {
  return (
    <div className="panel p-4">
      <div className="flex items-center justify-between">
        <p className="section-title">Bottom Live Activity Timeline</p>
        <StatusBadge status="Live" />
      </div>
      <div className="mt-4 grid gap-3 xl:grid-cols-5">
        {liveEvents.map((event) => (
          <div key={`${event.time}-${event.type}`} className="rounded-xl border border-slate-100 bg-white p-3 shadow-sm">
            <div className="flex items-center justify-between gap-2">
              <RiskBadge risk={event.severity} />
              <span className="text-xs font-bold text-slate-400">{event.time}</span>
            </div>
            <p className="mt-2 text-xs font-bold uppercase tracking-[0.12em] text-slate-500">{event.type}</p>
            <p className="mt-1 text-sm text-slate-700">{event.body}</p>
            <button className="btn-ghost mt-3 py-1.5 text-xs">{event.action}</button>
          </div>
        ))}
      </div>
    </div>
  );
}

function passesFilter(entity: FleetEntity, quickFilter: string, toggles: Set<string>, search: string, customerScope: string, mode: ViewMode) {
  if (mode === "Customer View" && String(entity.shipment?.customer) !== customerScope) return false;
  const haystack = [
    entity.id,
    entity.vehicle.plateNumber,
    entity.vehicle.vehicleType,
    entity.vehicle.currentLocation,
    entity.driver?.name,
    entity.shipment?.shipmentId,
    entity.shipment?.customer,
    entity.shipment?.origin,
    entity.shipment?.destination,
    entity.contract?.contractId,
  ].map(String).join(" ").toLowerCase();
  if (search && !haystack.includes(search.toLowerCase())) return false;
  if (quickFilter !== "All") {
    const q = quickFilter.toLowerCase();
    const matches =
      String(entity.vehicle.status).toLowerCase().includes(q) ||
      entity.shipmentStatus.toLowerCase().includes(q) ||
      String(entity.vehicle.vehicleType).toLowerCase().includes(q) ||
      entity.dutyStatus.toLowerCase().includes(q) ||
      entity.severity.toLowerCase() === q ||
      haystack.includes(q) ||
      (q === "active shipments" && Boolean(entity.shipment && entity.shipment.currentStatus !== "Delivered")) ||
      (q === "device risk" && entity.deviceOffline) ||
      (q === "margin risk" && entity.marginRisk) ||
      (q === "critical" && entity.severity === "Critical") ||
      (q === "cold chain" && entity.isColdChain) ||
      (q === "safety" && Boolean(entity.incident));
    if (!matches) return false;
  }
  if (toggles.has("Cold chain only") && !entity.isColdChain) return false;
  if (toggles.has("Delayed only") && !entity.isDelayed) return false;
  if (toggles.has("Safety events only") && !entity.incident) return false;
  if (toggles.has("Devices offline only") && !entity.deviceOffline) return false;
  if (toggles.has("Available for dispatch") && !(String(entity.vehicle.status).match(/Idle|Active/) && !entity.maintenanceRisk && !entity.hosRisk)) return false;
  if (toggles.has("Profitable / margin risk") && !entity.marginRisk) return false;
  return true;
}

export function Fleet360MapView() {
  const [mode, setMode] = useState<ViewMode>("Internal Operations View");
  const [customerScope, setCustomerScope] = useState("Gulf Express Logistics");
  const [search, setSearch] = useState("");
  const [quickFilter, setQuickFilter] = useState("All");
  const [activeTab, setActiveTab] = useState("Vehicle 360");
  const [toggles, setToggles] = useState<Set<string>>(new Set());
  const [layers, setLayers] = useState<Set<string>>(new Set(["Vehicles", "Drivers", "Shipments", "Cold chain sensors", "AI dashcam events", "Incidents", "Geofences", "Routes", "Risk Heatmap", "Customer locations", "Warehouses / hubs"]));
  const entities = useMemo(() => buildFleetEntities(), []);
  const visibleEntities = useMemo(() => entities.filter((entity) => passesFilter(entity, quickFilter, toggles, search, customerScope, mode)), [customerScope, entities, mode, quickFilter, search, toggles]);
  const [selectedId, setSelectedId] = useState(entities[0]?.id ?? "");
  const selected = visibleEntities.find((entity) => entity.id === selectedId) ?? visibleEntities[0] ?? entities[0];

  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow="Control Tower"
        title="Fleet 360 Command View"
        description="Real-time operational visibility across vehicles, drivers, shipments, sensors, alerts, compliance, customers and financial risk."
        actions={<><button className="btn-ghost"><Download className="h-4 w-4" /> Export Command Snapshot</button><button className="btn-primary"><Sparkles className="h-4 w-4" /> Ask Fleet AI</button></>}
      />

      <FleetTopKpiBar entities={entities} setQuickFilter={setQuickFilter} />
      <CustomerMapMode mode={mode} setMode={setMode} customerScope={customerScope} setCustomerScope={setCustomerScope} />

      <div className="panel p-3">
        <div className="mb-3 flex flex-col gap-3 lg:flex-row lg:items-center lg:justify-between">
          <div className="flex flex-wrap items-center gap-2">
            <span className="rounded-full border border-blue-100 bg-blue-50 px-3 py-1 text-xs font-bold text-blue-700">
              Showing {visibleEntities.length} of {entities.length} monitored units
            </span>
            {selected && (
              <>
                <StatusBadge status={selected.shipmentStatus} />
                <RiskBadge risk={selected.severity} />
                <span className="rounded-full border border-slate-200 bg-white px-3 py-1 text-xs font-bold text-slate-600">
                  Selected: {mode === "Customer View" ? `${String(selected.vehicle.vehicleType)} ${String(selected.vehicle.plateNumber).slice(-3)}` : selected.id}
                </span>
              </>
            )}
          </div>
          <div className="relative w-full lg:max-w-md">
            <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
            <input className="field h-10 py-0 pl-10" value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Search vehicle, driver, shipment, customer, location..." />
          </div>
        </div>
        <MapCanvas entities={visibleEntities} selectedId={selected?.id ?? ""} setSelectedId={setSelectedId} layers={layers} mode={mode} />
      </div>

      <div className="panel overflow-hidden">
        <div className="flex gap-1 overflow-x-auto border-b border-slate-100 bg-white px-3 py-2">
          {["Vehicle 360", "Filters & Layers", "AI Actions", "Dispatch Simulation", "Incident Replay", "Live Timeline"].map((tab) => (
            <button
              key={tab}
              className={`whitespace-nowrap rounded-lg px-3 py-2 text-sm font-bold transition ${activeTab === tab ? "bg-blue-600 text-white shadow-sm" : "text-slate-600 hover:bg-slate-100"}`}
              onClick={() => setActiveTab(tab)}
            >
              {tab}
            </button>
          ))}
        </div>

        <div className="p-5">
          {activeTab === "Vehicle 360" && (
            selected ? <Vehicle360Drawer entity={selected} mode={mode} /> : <EmptyState title="No fleet entity selected" subtitle="Click a marker on the map to inspect vehicle, driver, job and device context." />
          )}
          {activeTab === "Filters & Layers" && (
            <MapFilterPanel search={search} setSearch={setSearch} quickFilter={quickFilter} setQuickFilter={setQuickFilter} toggles={toggles} setToggles={setToggles} layers={layers} setLayers={setLayers} />
          )}
          {activeTab === "AI Actions" && (
            selected ? (
              <div className="grid gap-5 xl:grid-cols-[1fr_420px]">
                <AIRecommendationPanel selected={selected} />
                <AiInsightCard insight={{ title: "Fleet AI command brief", body: "Use the selected vehicle context plus map layers to decide whether to reroute, notify the customer, create service work, or protect margin.", score: 91, moduleKey: "map-view" }} />
              </div>
            ) : <EmptyState title="No AI context selected" subtitle="Select a vehicle or shipment marker to generate next-best actions." />
          )}
          {activeTab === "Dispatch Simulation" && <WhatIfDispatchSimulation />}
          {activeTab === "Incident Replay" && <LiveIncidentReplay />}
          {activeTab === "Live Timeline" && (
            mode === "Internal Operations View" ? (
              <AlertFeed />
            ) : (
              <div>
                <p className="section-title">Customer View Guardrails</p>
                <div className="mt-3 grid gap-3 md:grid-cols-3">
                  {["Only scoped customer shipments are visible", "Driver identity and profitability are masked", "Safety evidence and internal compliance notes are hidden"].map((item) => (
                    <div key={item} className="flex items-center gap-2 rounded-xl border border-blue-100 bg-blue-50 p-3 text-sm font-semibold text-blue-800">
                      <CheckCircle2 className="h-4 w-4" />
                      {item}
                    </div>
                  ))}
                </div>
              </div>
            )
          )}
        </div>
      </div>

      {mode === "Customer View" && activeTab !== "Live Timeline" && (
        <div className="panel p-5">
          <p className="section-title">Customer View Guardrails</p>
          <div className="mt-3 grid gap-3 md:grid-cols-3">
            {["Only scoped customer shipments are visible", "Driver identity and profitability are masked", "Safety evidence and internal compliance notes are hidden"].map((item) => (
              <div key={item} className="flex items-center gap-2 rounded-xl border border-blue-100 bg-blue-50 p-3 text-sm font-semibold text-blue-800">
                <CheckCircle2 className="h-4 w-4" />
                {item}
              </div>
            ))}
          </div>
        </div>
      )}

      {mode === "Internal Operations View" && activeTab !== "Live Timeline" && (
        <div className="panel p-4">
          <div className="flex items-center justify-between">
            <p className="section-title">Live Activity Snapshot</p>
            <button className="btn-ghost py-1.5 text-xs" onClick={() => setActiveTab("Live Timeline")}>Open full timeline</button>
          </div>
          <div className="mt-3 grid gap-3 md:grid-cols-3">
            {liveEvents.slice(0, 3).map((event) => (
              <div key={`${event.time}-${event.type}`} className="rounded-xl border border-slate-100 bg-white p-3">
                <div className="flex items-center justify-between">
                  <RiskBadge risk={event.severity} />
                  <span className="text-xs font-bold text-slate-400">{event.time}</span>
                </div>
                <p className="mt-2 text-sm font-semibold text-slate-800">{event.body}</p>
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}
