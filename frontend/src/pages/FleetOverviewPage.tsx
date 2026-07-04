import { useState, useMemo } from "react";
import {
  Activity, AlertTriangle, ArrowRight, Clock, Gauge, MapPin, Radio,
  ShieldAlert, Truck, Wifi, WifiOff, Zap, Wrench, ChevronRight,
} from "lucide-react";
import { useNavigate } from "react-router-dom";

type VStatus = "Driving" | "Idling" | "Parked" | "Offline" | "OOS";
type MStatus = "Healthy" | "Due Soon" | "Overdue" | "Critical";
type HStatus = "OK" | "Risk" | "--";
type Signal  = "Strong" | "Weak" | "No Upload";

interface FleetVehicle {
  id:          string;
  type:        string;
  driver:      string | null;
  status:      VStatus;
  location:    string;
  speed:       number;
  hosHrs:      number;
  hosStatus:   HStatus;
  maintenance: MStatus;
  signal:      Signal;
  lastSeen:    string;
  flag:        string | null;
}

const FLEET: FleetVehicle[] = [
  { id: "TRK-114",        type: "Semi Truck",   driver: "Mohammed Al-Zahrani", status: "Driving", location: "SABIC Industrial City, Jubail",   speed: 94, hosHrs: 9.2, hosStatus: "Risk",  maintenance: "Healthy",   signal: "Strong",    lastSeen: "< 1 min", flag: "HOS breach risk" },
  { id: "VAN-207",        type: "Van",          driver: null,                  status: "OOS",     location: "Riyadh Main Depot",                speed: 0,  hosHrs: 0,   hosStatus: "--",   maintenance: "Critical",  signal: "Strong",    lastSeen: "11 min",  flag: "Coolant leak — do not dispatch" },
  { id: "TRK-108",        type: "Semi Truck",   driver: "Khaled Al-Rashidi",   status: "Driving", location: "Shell Station, Dhahran",            speed: 0,  hosHrs: 4.1, hosStatus: "OK",   maintenance: "Healthy",   signal: "Strong",    lastSeen: "2 min",   flag: "Fuel anomaly — off-route stop" },
  { id: "RFG-302",        type: "Reefer Truck", driver: "Saeed Al-Ghamdi",    status: "Driving", location: "KAIA Cargo Terminal, Jeddah",       speed: 81, hosHrs: 6.5, hosStatus: "OK",   maintenance: "Healthy",   signal: "Strong",    lastSeen: "< 1 min", flag: "Cold chain excursion +3°C" },
  { id: "TRK-106",        type: "Semi Truck",   driver: "Yusuf Al-Qahtani",   status: "Parked",  location: "Riyadh Industrial City",            speed: 0,  hosHrs: 0,   hosStatus: "OK",   maintenance: "Overdue",   signal: "Strong",    lastSeen: "8 min",   flag: "PM-A 320 km overdue" },
  { id: "KSA-REEFER-214", type: "Reefer Truck", driver: "Salman Qureshi",     status: "Driving", location: "Riyadh East Ring Road",             speed: 76, hosHrs: 3.2, hosStatus: "OK",   maintenance: "Healthy",   signal: "Strong",    lastSeen: "< 1 min", flag: null },
  { id: "KSA-REEFER-119", type: "Reefer Truck", driver: "Bilal Ansari",       status: "Driving", location: "Jeddah Pharma Hub",                 speed: 68, hosHrs: 7.1, hosStatus: "OK",   maintenance: "Due Soon",  signal: "Weak",      lastSeen: "3 min",   flag: null },
  { id: "TRK-109",        type: "Semi Truck",   driver: "Faisal Al-Mutairi",  status: "Driving", location: "Dammam Port — Gate 4",              speed: 62, hosHrs: 5.8, hosStatus: "OK",   maintenance: "Healthy",   signal: "Strong",    lastSeen: "1 min",   flag: null },
  { id: "DXB-VAN-045",    type: "Van",          driver: "Imran Sheikh",        status: "Idling",  location: "Dubai Fulfillment Center",          speed: 0,  hosHrs: 2.4, hosStatus: "OK",   maintenance: "Healthy",   signal: "Strong",    lastSeen: "5 min",   flag: null },
  { id: "VAN-204",        type: "Van",          driver: "Omar Al-Harbi",       status: "Idling",  location: "Riyadh Hypermarket — Bay 3",       speed: 0,  hosHrs: 1.2, hosStatus: "OK",   maintenance: "Healthy",   signal: "Strong",    lastSeen: "7 min",   flag: null },
  { id: "TRK-103",        type: "Semi Truck",   driver: "Ahmed Al-Dosari",     status: "Parked",  location: "Qassim Distribution Centre",        speed: 0,  hosHrs: 8.5, hosStatus: "OK",   maintenance: "Healthy",   signal: "Strong",    lastSeen: "22 min",  flag: "Coaching task pending 4 days" },
  { id: "VAN-211",        type: "Van",          driver: "Nasser Al-Shehri",    status: "Parked",  location: "Madinah DC",                        speed: 0,  hosHrs: 2.1, hosStatus: "OK",   maintenance: "Healthy",   signal: "Strong",    lastSeen: "34 min",  flag: null },
  { id: "TRK-117",        type: "Semi Truck",   driver: null,                  status: "Offline", location: "Last: Tabuk Yard",                  speed: 0,  hosHrs: 0,   hosStatus: "--",   maintenance: "Healthy",   signal: "No Upload", lastSeen: "4 hr",    flag: "No heartbeat 4 hr" },
  { id: "BOX-106",        type: "Box Truck",    driver: "Ana Rivera",          status: "OOS",     location: "Washington DC Depot",               speed: 0,  hosHrs: 0,   hosStatus: "--",   maintenance: "Critical",  signal: "No Upload", lastSeen: "2 hr",    flag: "Camera not recording" },
];

const STATUS_TABS = ["All", "Driving", "Idling", "Parked", "OOS", "Offline"] as const;
type Tab = typeof STATUS_TABS[number];

const STATUS_CFG: Record<VStatus, { badge: string; dot: string; label: string }> = {
  Driving: { badge: "badge badge-success",                                               dot: "bg-emerald-500 animate-pulse", label: "Driving" },
  Idling:  { badge: "badge badge-warning",                                               dot: "bg-amber-400",                 label: "Idling" },
  Parked:  { badge: "inline-flex items-center rounded-md px-2 py-0.5 text-[10px] font-semibold bg-slate-100 text-slate-600 border border-slate-200", dot: "bg-slate-400", label: "Parked" },
  Offline: { badge: "badge badge-danger",                                                dot: "bg-red-500",                   label: "Offline" },
  OOS:     { badge: "inline-flex items-center rounded-md px-2 py-0.5 text-[10px] font-semibold bg-red-100 text-red-700 border border-red-200",      dot: "bg-red-500 animate-pulse", label: "OOS" },
};

const MAINT_CFG: Record<MStatus, { cls: string }> = {
  Healthy:   { cls: "text-emerald-600 text-xs font-medium" },
  "Due Soon":{ cls: "text-amber-600 text-xs font-medium" },
  Overdue:   { cls: "text-red-600 text-xs font-semibold" },
  Critical:  { cls: "text-red-700 text-xs font-bold" },
};

const HOS_CFG: Record<HStatus, { cls: string }> = {
  OK:    { cls: "text-emerald-600 text-xs" },
  Risk:  { cls: "text-red-600 text-xs font-bold" },
  "--":  { cls: "text-slate-400 text-xs" },
};

const SIGNAL_ICON: Record<Signal, React.ReactNode> = {
  Strong:      <Wifi className="h-3.5 w-3.5 text-emerald-500" />,
  Weak:        <Wifi className="h-3.5 w-3.5 text-amber-500" />,
  "No Upload": <WifiOff className="h-3.5 w-3.5 text-red-400" />,
};

export function FleetOverviewPage() {
  const navigate = useNavigate();
  const [tab, setTab] = useState<Tab>("All");

  const counts = useMemo(() => ({
    Driving: FLEET.filter((v) => v.status === "Driving").length,
    Idling:  FLEET.filter((v) => v.status === "Idling").length,
    Parked:  FLEET.filter((v) => v.status === "Parked").length,
    Offline: FLEET.filter((v) => v.status === "Offline").length,
    OOS:     FLEET.filter((v) => v.status === "OOS").length,
  }), []);

  const filtered = useMemo(
    () => tab === "All" ? FLEET : FLEET.filter((v) => v.status === tab),
    [tab],
  );

  const flagged = FLEET.filter((v) => v.flag).length;

  return (
    <div className="space-y-6">
      {/* ── Hero banner ─────────────────────────────────────────── */}
      <header className="fh-hero relative">
        <span className="fh-hero-bar" />
        <span className="fh-hero-glow-1" />
        <span className="fh-hero-glow-2" />

        <div className="relative px-7 py-6">
          <div className="flex flex-wrap items-start justify-between gap-6">
            <div className="min-w-0 flex-1">
              <div className="flex items-center gap-3 mb-3">
                <span className="inline-flex items-center gap-1.5 rounded-lg bg-white/90 px-3 py-1 text-[10px] font-bold uppercase tracking-[0.2em] text-teal-700 ring-1 ring-teal-200/50 shadow-sm">
                  <Gauge className="h-3 w-3" /> Fleet Overview
                </span>
                <span className="relative flex h-2.5 w-2.5">
                  <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-emerald-400 opacity-60" />
                  <span className="relative inline-flex h-2.5 w-2.5 rounded-full bg-emerald-500" />
                </span>
                <span className="text-[11px] font-semibold text-slate-500">Live · 15s refresh</span>
              </div>

              <h1 className="text-[32px] font-black tracking-tight leading-none cc-gradient-text sm:text-[36px]">
                Live Vehicle Status
              </h1>
              <p className="mt-1 text-[13px] font-medium text-slate-400 tracking-wide">
                {FLEET.length} vehicles tracked · {counts.Driving} on road · {flagged} flagged for review
              </p>
            </div>

            <div className="flex items-center gap-2">
              <button
                type="button"
                className="fh-btn-primary"
                onClick={() => navigate("/vehicles")}
              >
                Full Fleet Registry
                <ArrowRight className="h-3.5 w-3.5" />
              </button>
            </div>
          </div>
        </div>
      </header>

      {/* ── Status KPI strip ────────────────────────────────────── */}
      <div className="grid grid-cols-2 gap-3 md:grid-cols-5">
        <KpiStrip
          label="Driving"
          count={counts.Driving}
          Icon={Truck}
          accent="text-teal-600"
          dot="bg-emerald-500 animate-pulse"
          onClick={() => setTab("Driving")}
          active={tab === "Driving"}
        />
        <KpiStrip
          label="Idling"
          count={counts.Idling}
          Icon={Zap}
          accent="text-teal-600"
          dot="bg-amber-400"
          onClick={() => setTab("Idling")}
          active={tab === "Idling"}
        />
        <KpiStrip
          label="Parked"
          count={counts.Parked}
          Icon={Clock}
          accent="text-teal-600"
          dot="bg-slate-400"
          onClick={() => setTab("Parked")}
          active={tab === "Parked"}
        />
        <KpiStrip
          label="Out of Service"
          count={counts.OOS}
          Icon={ShieldAlert}
          accent="text-teal-600"
          dot="bg-red-500 animate-pulse"
          onClick={() => setTab("OOS")}
          active={tab === "OOS"}
        />
        <KpiStrip
          label="Offline"
          count={counts.Offline}
          Icon={WifiOff}
          accent="text-teal-600"
          dot="bg-slate-300"
          onClick={() => setTab("Offline")}
          active={tab === "Offline"}
        />
      </div>

      {/* ── Fleet roster table ──────────────────────────────────── */}
      <div className="fo-table-panel">
        {/* Tab bar */}
        <div className="fo-tab-bar">
          {STATUS_TABS.map((t) => (
            <button
              key={t}
              type="button"
              onClick={() => setTab(t)}
              className={`fo-tab ${tab === t ? "fo-tab-active" : ""}`}
            >
              {t}
              {t !== "All" && (
                <span className={`fo-tab-count ${tab === t ? "fo-tab-count-active" : "fo-tab-count-inactive"}`}>
                  {counts[t as Exclude<Tab, "All">] ?? 0}
                </span>
              )}
            </button>
          ))}
          <span className="fo-tab-live">
            <span className="fo-tab-live-dot" />
            Live · updates every 15 s
          </span>
        </div>

        {/* Table */}
        <div className="overflow-x-auto">
          <table className="fo-table">
            <thead className="fo-table-head">
              <tr>
                <th>Vehicle</th>
                <th>Driver</th>
                <th>Status</th>
                <th>Location</th>
                <th>Speed</th>
                <th>HOS</th>
                <th>Maint.</th>
                <th>Signal</th>
                <th>Last Update</th>
                <th style={{ width: 80 }} />
              </tr>
            </thead>
            <tbody>
              {filtered.map((v) => {
                const sc = STATUS_CFG[v.status];
                const mc = MAINT_CFG[v.maintenance];
                const hc = HOS_CFG[v.hosStatus];
                const statusClass = {
                  Driving: "fo-status-driving",
                  Idling:  "fo-status-idling",
                  Parked:  "fo-status-parked",
                  Offline: "fo-status-offline",
                  OOS:     "fo-status-oos",
                }[v.status] ?? "fo-status-parked";

                return (
                  <tr
                    key={v.id}
                    className={`fo-table-row ${v.flag ? "fo-table-row-flagged" : ""}`}
                  >
                    {/* Vehicle */}
                    <td className="fo-table-cell">
                      <div className="fo-vehicle-id">
                        <span className={`fo-vehicle-dot ${sc.dot}`} />
                        <div>
                          <p className="fo-vehicle-name">{v.id}</p>
                          <p className="fo-vehicle-type">{v.type}</p>
                        </div>
                      </div>
                    </td>
                    {/* Driver */}
                    <td className="fo-table-cell">
                      {v.driver ? (
                        <span className="text-[13px] text-slate-700 font-medium">{v.driver}</span>
                      ) : (
                        <span className="text-[13px] text-slate-400 italic">Unassigned</span>
                      )}
                    </td>
                    {/* Status */}
                    <td className="fo-table-cell">
                      <span className={`fo-status-badge ${statusClass}`}>
                        <span className={`h-1.5 w-1.5 rounded-full ${sc.dot}`} />
                        {sc.label}
                      </span>
                    </td>
                    {/* Location */}
                    <td className="fo-table-cell">
                      <div className="fo-location">
                        <MapPin className="fo-location-icon" />
                        <div>
                          <span className="fo-location-text">{v.location}</span>
                          {v.flag && (
                            <div className="fo-flag">
                              <AlertTriangle className="fo-flag-icon" />
                              {v.flag}
                            </div>
                          )}
                        </div>
                      </div>
                    </td>
                    {/* Speed */}
                    <td className="fo-table-cell">
                      {v.status === "Driving" && v.speed > 0 ? (
                        <span className="fo-speed">{v.speed}<span className="fo-speed-unit">km/h</span></span>
                      ) : (
                        <span className="text-slate-400">—</span>
                      )}
                    </td>
                    {/* HOS */}
                    <td className="fo-table-cell">
                      {v.hosStatus === "--" ? (
                        <span className="text-slate-400">—</span>
                      ) : (
                        <div>
                          <span className={`fo-hos-status ${hc.cls}`}>{v.hosStatus}</span>
                          <p className="fo-hos-hours">{v.hosHrs.toFixed(1)} h driven</p>
                        </div>
                      )}
                    </td>
                    {/* Maintenance */}
                    <td className="fo-table-cell">
                      <div className="fo-maint">
                        <Wrench className="fo-maint-icon" />
                        <span className={mc.cls}>{v.maintenance}</span>
                      </div>
                    </td>
                    {/* Signal */}
                    <td className="fo-table-cell">
                      <div className="fo-signal">
                        {SIGNAL_ICON[v.signal]}
                        <span>{v.signal}</span>
                      </div>
                    </td>
                    {/* Last update */}
                    <td className="fo-table-cell">
                      <span className="fo-last-seen">{v.lastSeen}</span>
                    </td>
                    {/* Action */}
                    <td className="fo-table-cell">
                      <button
                        type="button"
                        className="fo-action-btn"
                        onClick={() => navigate("/vehicles")}
                      >
                        Detail
                        <ChevronRight className="fo-action-btn-icon" />
                      </button>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>

          {filtered.length === 0 && (
            <div className="fo-empty">
              <Radio className="fo-empty-icon" />
              <p className="fo-empty-text">No vehicles match this filter</p>
            </div>
          )}
        </div>

        {/* Footer strip */}
        <div className="fo-footer-strip">
          <span>{filtered.length} of {FLEET.length} vehicles shown</span>
          <div className="fo-footer-stats">
            <span className="fo-footer-stat">
              <Wifi className="fo-footer-stat-icon text-emerald-500" />
              {FLEET.filter((v) => v.signal === "Strong").length} strong signal
            </span>
            <span className="fo-footer-stat">
              <ShieldAlert className="fo-footer-stat-icon text-amber-500" />
              {flagged} flagged
            </span>
            <button
              type="button"
              className="fo-footer-link"
              onClick={() => navigate("/iot-devices")}
            >
              Device Health →
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}

/* ── KPI strip card ─────────────────────────────────── */
function KpiStrip({
  label, count, Icon, accent, dot, onClick, active,
}: {
  label: string;
  count: number;
  Icon: React.ElementType;
  accent: string;
  dot: string;
  onClick: () => void;
  active: boolean;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={`fo-kpi-card ${active ? "fo-kpi-active" : ""}`}
    >
      <div className={`fo-kpi-icon ${active ? "fo-kpi-icon-active" : "fo-kpi-icon-inactive"}`}>
        <Icon className="h-5 w-5 text-teal-600" />
      </div>
      <div className="min-w-0 flex-1">
        <div className="fo-kpi-count">
          <span className={`fo-kpi-dot ${dot}`} />
          <span className={active ? accent : "text-slate-900"}>{count}</span>
        </div>
        <p className={`fo-kpi-label ${active ? accent : "text-slate-500"}`}>{label}</p>
      </div>
    </button>
  );
}
