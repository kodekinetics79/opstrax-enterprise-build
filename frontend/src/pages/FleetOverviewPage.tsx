import { useState, useMemo } from "react";
import {
  Activity, AlertTriangle, Clock, MapPin, Radio, ShieldAlert,
  Truck, Wifi, WifiOff, Zap, Wrench, ChevronRight,
} from "lucide-react";
import { useNavigate } from "react-router-dom";
import { PageHeader } from "@/components/ui";

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
      <PageHeader
        eyebrow="Fleet Overview"
        title="Live Vehicle Status"
        description={`${FLEET.length} vehicles tracked · ${counts.Driving} on road · ${flagged} flagged for review`}
        actions={
          <button type="button" className="btn-primary" onClick={() => navigate("/vehicles")}>
            Full Fleet Registry
            <ChevronRight className="h-4 w-4" />
          </button>
        }
      />

      {/* Status KPI strip */}
      <div className="grid grid-cols-2 gap-3 md:grid-cols-5">
        <KpiStrip
          label="Driving"
          count={counts.Driving}
          Icon={Truck}
          accent="text-emerald-600"
          bg="bg-emerald-50"
          border="border-emerald-200"
          dot="bg-emerald-500 animate-pulse"
          onClick={() => setTab("Driving")}
          active={tab === "Driving"}
        />
        <KpiStrip
          label="Idling"
          count={counts.Idling}
          Icon={Zap}
          accent="text-amber-600"
          bg="bg-amber-50"
          border="border-amber-200"
          dot="bg-amber-400"
          onClick={() => setTab("Idling")}
          active={tab === "Idling"}
        />
        <KpiStrip
          label="Parked"
          count={counts.Parked}
          Icon={Clock}
          accent="text-slate-600"
          bg="bg-slate-50"
          border="border-slate-200"
          dot="bg-slate-400"
          onClick={() => setTab("Parked")}
          active={tab === "Parked"}
        />
        <KpiStrip
          label="Out of Service"
          count={counts.OOS}
          Icon={ShieldAlert}
          accent="text-red-600"
          bg="bg-red-50"
          border="border-red-200"
          dot="bg-red-500 animate-pulse"
          onClick={() => setTab("OOS")}
          active={tab === "OOS"}
        />
        <KpiStrip
          label="Offline"
          count={counts.Offline}
          Icon={WifiOff}
          accent="text-slate-500"
          bg="bg-slate-50"
          border="border-slate-200"
          dot="bg-slate-300"
          onClick={() => setTab("Offline")}
          active={tab === "Offline"}
        />
      </div>

      {/* Filter tabs + roster table */}
      <div className="panel overflow-hidden">
        {/* Tab bar */}
        <div className="flex items-center gap-1 border-b border-slate-100 px-4 pt-3 pb-0">
          {STATUS_TABS.map((t) => (
            <button
              key={t}
              type="button"
              onClick={() => setTab(t)}
              className={`relative px-3 py-2 text-sm font-medium transition-colors ${
                tab === t
                  ? "text-slate-950 after:absolute after:bottom-0 after:left-0 after:right-0 after:h-0.5 after:bg-teal-500"
                  : "text-slate-400 hover:text-slate-700"
              }`}
            >
              {t}
              {t !== "All" && (
                <span className={`ml-1.5 rounded-full px-1.5 py-px text-[10px] font-semibold ${
                  tab === t ? "bg-teal-100 text-teal-700" : "bg-slate-100 text-slate-500"
                }`}>
                  {counts[t as Exclude<Tab, "All">] ?? 0}
                </span>
              )}
            </button>
          ))}
          <span className="ml-auto text-xs text-slate-400 pb-2">
            <Activity className="inline h-3 w-3 mr-1 text-teal-500" />
            Live · updates every 15 s
          </span>
        </div>

        {/* Table */}
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-slate-100">
                <th className="px-5 py-3 text-left text-[10px] font-semibold uppercase tracking-widest text-slate-400">Vehicle</th>
                <th className="px-3 py-3 text-left text-[10px] font-semibold uppercase tracking-widest text-slate-400">Driver</th>
                <th className="px-3 py-3 text-left text-[10px] font-semibold uppercase tracking-widest text-slate-400">Status</th>
                <th className="px-3 py-3 text-left text-[10px] font-semibold uppercase tracking-widest text-slate-400">Location</th>
                <th className="px-3 py-3 text-left text-[10px] font-semibold uppercase tracking-widest text-slate-400">Speed</th>
                <th className="px-3 py-3 text-left text-[10px] font-semibold uppercase tracking-widest text-slate-400">HOS</th>
                <th className="px-3 py-3 text-left text-[10px] font-semibold uppercase tracking-widest text-slate-400">Maint.</th>
                <th className="px-3 py-3 text-left text-[10px] font-semibold uppercase tracking-widest text-slate-400">Signal</th>
                <th className="px-3 py-3 text-left text-[10px] font-semibold uppercase tracking-widest text-slate-400">Last Update</th>
                <th className="px-3 py-3" />
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-50">
              {filtered.map((v) => {
                const sc = STATUS_CFG[v.status];
                const mc = MAINT_CFG[v.maintenance];
                const hc = HOS_CFG[v.hosStatus];
                return (
                  <tr
                    key={v.id}
                    className={`group transition-colors hover:bg-slate-50/60 ${v.flag ? "bg-red-50/30" : ""}`}
                  >
                    {/* Vehicle */}
                    <td className="px-5 py-3">
                      <div className="flex items-center gap-2.5">
                        <span className={`mt-0.5 h-2 w-2 shrink-0 rounded-full ${sc.dot}`} />
                        <div>
                          <p className="font-semibold text-slate-900">{v.id}</p>
                          <p className="text-[11px] text-slate-400">{v.type}</p>
                        </div>
                      </div>
                    </td>
                    {/* Driver */}
                    <td className="px-3 py-3">
                      {v.driver ? (
                        <p className="text-slate-700">{v.driver}</p>
                      ) : (
                        <p className="text-slate-400 italic">Unassigned</p>
                      )}
                    </td>
                    {/* Status */}
                    <td className="px-3 py-3">
                      <span className={sc.badge}>{sc.label}</span>
                    </td>
                    {/* Location */}
                    <td className="px-3 py-3">
                      <div className="flex items-center gap-1.5">
                        <MapPin className="h-3.5 w-3.5 shrink-0 text-slate-300" />
                        <span className="text-slate-600">{v.location}</span>
                      </div>
                      {v.flag && (
                        <div className="mt-0.5 flex items-center gap-1">
                          <AlertTriangle className="h-3 w-3 shrink-0 text-amber-500" />
                          <span className="text-[11px] text-amber-700">{v.flag}</span>
                        </div>
                      )}
                    </td>
                    {/* Speed */}
                    <td className="px-3 py-3 text-slate-700">
                      {v.status === "Driving" && v.speed > 0 ? (
                        <span className="font-medium">{v.speed} <span className="text-xs font-normal text-slate-400">km/h</span></span>
                      ) : (
                        <span className="text-slate-400">—</span>
                      )}
                    </td>
                    {/* HOS */}
                    <td className="px-3 py-3">
                      {v.hosStatus === "--" ? (
                        <span className={hc.cls}>—</span>
                      ) : (
                        <div>
                          <span className={hc.cls}>{v.hosStatus}</span>
                          <p className="text-[11px] text-slate-400">{v.hosHrs.toFixed(1)} h driven</p>
                        </div>
                      )}
                    </td>
                    {/* Maintenance */}
                    <td className="px-3 py-3">
                      <div className="flex items-center gap-1">
                        <Wrench className="h-3.5 w-3.5 shrink-0 text-slate-300" />
                        <span className={mc.cls}>{v.maintenance}</span>
                      </div>
                    </td>
                    {/* Signal */}
                    <td className="px-3 py-3">
                      <div className="flex items-center gap-1.5">
                        {SIGNAL_ICON[v.signal]}
                        <span className="text-[11px] text-slate-500">{v.signal}</span>
                      </div>
                    </td>
                    {/* Last update */}
                    <td className="px-3 py-3">
                      <span className="text-xs text-slate-500">{v.lastSeen}</span>
                    </td>
                    {/* Action */}
                    <td className="px-3 py-3">
                      <button
                        type="button"
                        className="btn-ghost invisible h-7 gap-1 px-3 text-xs group-hover:visible"
                        onClick={() => navigate("/vehicles")}
                      >
                        Detail
                        <ChevronRight className="h-3 w-3" />
                      </button>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>

          {filtered.length === 0 && (
            <div className="py-12 text-center">
              <Radio className="mx-auto h-8 w-8 text-slate-300" />
              <p className="mt-2 text-sm font-semibold text-slate-500">No vehicles match this filter</p>
            </div>
          )}
        </div>

        {/* Footer strip */}
        <div className="flex items-center justify-between border-t border-slate-100 px-5 py-3 text-xs text-slate-400">
          <span>{filtered.length} of {FLEET.length} vehicles shown</span>
          <div className="flex items-center gap-4">
            <span>
              <Wifi className="mr-1 inline h-3 w-3 text-emerald-500" />
              {FLEET.filter((v) => v.signal === "Strong").length} strong signal
            </span>
            <span>
              <ShieldAlert className="mr-1 inline h-3 w-3 text-amber-500" />
              {flagged} flagged
            </span>
            <button
              type="button"
              className="text-teal-600 hover:underline"
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
  label, count, Icon, accent, bg, border, dot, onClick, active,
}: {
  label: string;
  count: number;
  Icon: React.ElementType;
  accent: string;
  bg: string;
  border: string;
  dot: string;
  onClick: () => void;
  active: boolean;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={`flex items-center gap-3 rounded-xl border p-4 text-left transition-all ${
        active ? `${bg} ${border} ring-2 ring-offset-1 ring-teal-400` : "bg-white border-slate-200 hover:border-slate-300"
      }`}
    >
      <div className={`flex h-9 w-9 shrink-0 items-center justify-center rounded-xl ${active ? bg : "bg-slate-50"}`}>
        <Icon className={`h-4.5 w-4.5 ${active ? accent : "text-slate-400"}`} />
      </div>
      <div className="min-w-0 flex-1">
        <div className="flex items-center gap-1.5">
          <span className={`h-2 w-2 shrink-0 rounded-full ${dot}`} />
          <p className="text-2xl font-bold text-slate-900">{count}</p>
        </div>
        <p className={`truncate text-xs font-semibold ${active ? accent : "text-slate-500"}`}>{label}</p>
      </div>
    </button>
  );
}
