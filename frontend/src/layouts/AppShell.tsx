import { useEffect, useMemo, useRef, useState } from "react";
import { Link, Outlet, useLocation, useNavigate } from "react-router-dom";
import {
  Bell, ChevronDown, ChevronLeft, ChevronRight, Filter, LogOut,
  Menu, Search, Settings, User, X,
} from "lucide-react";
import { OpsTraxLogo } from "@/components/OpsTraxLogo";
import { WorkspaceExperience } from "@/components/WorkspaceExperience";
import { modules, moduleIcons } from "@/modules/moduleConfig";
import { useAuth } from "@/hooks/useAuth";
import { useHasPermission } from "@/hooks/usePermission";
import { getLandingRouteForSession } from "@/auth/sessionRouting";
import type { AnyRecord, UserSession } from "@/types";

const NAV_SECTIONS = [
  {
    label: "Operations",
    color: "text-teal-600",
    items: ["command-center", "fleet-health", "live-dashboard", "map-view", "alerts"],
  },
  {
    label: "Fleet",
    color: "text-blue-600",
    items: ["vehicles", "drivers", "fleet-utilization", "fleet-workspace", "fleet-cold-chain", "fleet-assets", "fleet-saudi-readiness", "fleet-compliance", "assignments"],
  },
  {
    label: "Dispatch",
    color: "text-cyan-700",
    items: ["dispatch-board", "jobs", "trips", "route-plans", "last-mile-delivery", "operations-proof-center", "logistics-workspace"],
  },
  {
    label: "Shipments",
    color: "text-teal-700",
    items: ["active-shipments", "shipments", "proof-of-delivery"],
  },
  {
    label: "Safety",
    color: "text-red-600",
    items: ["incidents", "coaching", "driver-scorecards", "dvir-inspections", "hos-eld"],
  },
  {
    label: "Maintenance",
    color: "text-amber-700",
    items: ["work-orders", "preventive-maintenance", "service-history", "downtime"],
  },
  {
    label: "Telematics",
    color: "text-violet-700",
    items: ["iot-devices", "gps-tracking", "obd-j1939"],
  },
  {
    label: "Customers",
    color: "text-indigo-600",
    items: ["customers"],
  },
  {
    label: "Reports",
    color: "text-purple-600",
    items: ["reports-analytics", "predictive-analytics", "sla-kpi", "carbon-tracking"],
  },
  {
    label: "Admin",
    color: "text-slate-500",
    items: ["user-management", "audit-logs", "integrations", "settings", "about"],
  },
] as const;

type Group = typeof NAV_SECTIONS[number]["label"];
type BreadcrumbItem = {
  label: string;
  to?: string;
  current?: boolean;
};
type SessionLike = Pick<UserSession, "role" | "user" | "company" | "permissions"> | null | undefined;
type NavState = Partial<Record<Group, boolean>>;

const NAV_STORAGE_PREFIX = "opstrax.nav.sidebar";

function resolveSearchRoute(query: string): string {
  const q = query.trim().toLowerCase();
  if (!q) return "/command-center";

  const exactModule = modules.find((module) => {
    const haystack = [module.key, module.title, module.route, module.description].join(" ").toLowerCase();
    return haystack.includes(q);
  });

  if (exactModule) return exactModule.route;

  const keywordRoutes: Array<{ test: RegExp; route: string }> = [
    { test: /\bmap\b|\blive map\b|\bgps\b|\btelemetry\b/, route: "/map-view" },
    { test: /\bhealth\b|\bfleet health\b/, route: "/fleet-health" },
    { test: /\bvehicle\b|\bvehicles\b|\btruck\b|\bvan\b/, route: "/vehicles" },
    { test: /\bdriver\b|\bdrivers\b/, route: "/drivers" },
    { test: /\bwork order\b|\bwork orders\b|\bmaintenance\b|\brepair\b/, route: "/work-orders" },
    { test: /\bjob\b|\bjobs\b|\bdispatch\b|\btrip\b|\btrips\b/, route: "/dispatch-board" },
    { test: /\balert\b|\balerts\b/, route: "/alerts" },
    { test: /\bshipment\b|\bshipments\b|\bpod\b/, route: "/shipments" },
  ];

  for (const item of keywordRoutes) {
    if (item.test.test(q)) return item.route;
  }

  return "/command-center";
}

function normalizeRoute(route: string) {
  if (route === "/") return route;
  return route.replace(/\/+$/, "");
}

function isRouteActive(route: string, pathname: string) {
  const normalizedRoute = normalizeRoute(route);
  const normalizedPath = normalizeRoute(pathname);
  return normalizedPath === normalizedRoute || normalizedPath.startsWith(`${normalizedRoute}/`);
}

function humanizeSegment(segment: string) {
  return segment
    .replace(/[-_]+/g, " ")
    .trim()
    .replace(/\b\w/g, (char) => char.toUpperCase());
}

function getSessionIdentityKey(session: SessionLike) {
  const companyId = String(session?.company?.id ?? session?.company?.companyId ?? "tenant");
  const role = String(session?.role ?? "role").toLowerCase().replace(/\s+/g, "-");
  const userKey = String(session?.user?.email ?? session?.user?.id ?? session?.user?.name ?? "user").toLowerCase().replace(/\s+/g, "-");
  return `${NAV_STORAGE_PREFIX}.${companyId}.${role}.${userKey}`;
}

function getSessionDisplayName(session: SessionLike) {
  return String(session?.user?.name ?? session?.user?.fullName ?? session?.user?.email ?? session?.role ?? "User");
}

function getSessionRoleLabel(session: SessionLike) {
  return String(session?.role ?? "Tenant user")
    .replace(/[_-]+/g, " ")
    .replace(/\b\w/g, (char) => char.toUpperCase());
}

function getSessionCompanyLabel(session: SessionLike) {
  return String(session?.company?.name ?? "OpsTrax Tenant");
}

function getSessionPlanLabel(session: SessionLike) {
  const company = session?.company as AnyRecord | undefined;
  const candidates = [
    company?.planName,
    company?.plan_name,
    company?.subscriptionPlan,
    company?.subscription_plan,
    company?.packageName,
    company?.package_name,
    company?.package,
    company?.package_code,
    company?.tier,
  ];
  const found = candidates.find((value) => typeof value === "string" && value.trim().length > 0);
  return found ? String(found) : "Enterprise";
}

function findActiveModule(pathname: string) {
  return [...modules]
    .sort((left, right) => right.route.length - left.route.length)
    .find((module) => isRouteActive(module.route, pathname));
}

function buildBreadcrumbs(pathname: string, session: SessionLike): BreadcrumbItem[] {
  const landingRoute = getLandingRouteForSession(session as UserSession | null);
  const crumbs: BreadcrumbItem[] = [{ label: "Home", to: landingRoute }];
  const activeModule = findActiveModule(pathname);
  const activeSection = NAV_SECTIONS.find((section) => activeModule && (section.items as readonly string[]).includes(activeModule.key));

  if (!activeModule) {
    const segments = pathname.split("/").filter(Boolean);
    segments.forEach((segment, index) => {
      const to = `/${segments.slice(0, index + 1).join("/")}`;
      crumbs.push({
        label: humanizeSegment(segment),
        to,
        current: index === segments.length - 1,
      });
    });
    return crumbs;
  }

  if (activeSection) crumbs.push({ label: activeSection.label });
  crumbs.push({ label: activeModule.title, to: activeModule.route });

  const base = normalizeRoute(activeModule.route);
  const tail = pathname.startsWith(`${base}/`) ? pathname.slice(base.length + 1) : "";
  if (tail) {
    const segments = tail.split("/").filter(Boolean);
    segments.forEach((segment, index) => {
      const to = `${base}/${segments.slice(0, index + 1).join("/")}`;
      crumbs.push({
        label: humanizeSegment(segment),
        to,
        current: index === segments.length - 1,
      });
    });
  } else {
    const last = crumbs[crumbs.length - 1];
    if (last) last.current = true;
  }

  return crumbs;
}

function matchesFilter(module: (typeof modules)[number], sectionLabel: string, query: string) {
  if (!query) return true;
  const haystack = [module.key, module.title, module.route, module.description, module.group, sectionLabel].join(" ").toLowerCase();
  return haystack.includes(query);
}

type ExperienceProfile = {
  clientOutcome: string;
  maintenanceOutcome: string;
  shortcuts: Array<{ label: string; route: string }>;
};

function getExperienceProfile(pathname: string, title: string): ExperienceProfile {
  const base = {
    clientOutcome: `Work through ${title.toLowerCase()} with clear next actions and one-click access to the surfaces that resolve each issue.`,
    maintenanceOutcome: "",
    shortcuts: [
      { label: "Dashboard", route: "/command-center" },
      { label: "Proof Center", route: "/operations/proof-center" },
      { label: "Control Tower", route: "/control-tower" },
    ],
  };

  if (/^\/(command-center|control-tower|live-dashboard|map-view|alerts)/.test(pathname)) {
    return {
      clientOutcome: "See live fleet status, risk, and exceptions immediately, then jump to the exact operational surface that resolves the issue.",
      maintenanceOutcome: "This cluster uses the shared control-room pattern, so future ops modules can be added by configuration instead of one-off page work.",
      shortcuts: [
        { label: "Alerts", route: "/alerts" },
        { label: "Fleet Health", route: "/fleet-health" },
        { label: "Proof Center", route: "/operations/proof-center" },
      ],
    };
  }

  if (/^\/(vehicles|drivers|fleet-health|fleet-utilization)/.test(pathname)) {
    return {
      clientOutcome: "Give transport teams a fast path to readiness, service state, and the next operational decision.",
      maintenanceOutcome: "These pages now share the same shell and action model, so new roster or readiness views can be added without inventing a new UI language.",
      shortcuts: [
        { label: "Vehicles", route: "/vehicles" },
        { label: "Drivers", route: "/drivers" },
        { label: "Fleet Health", route: "/fleet-health" },
      ],
    };
  }

  if (/^\/(dispatch|jobs|trips|last-mile-delivery|operations\/proof-center|proof-of-delivery)/.test(pathname)) {
    return {
      clientOutcome: "Keep dispatch, proof, and recovery work in one flow so operators always know what happens next.",
      maintenanceOutcome: "Shared route shortcuts and one set of proof/action patterns make it easier to extend the dispatch spine without redoing every screen.",
      shortcuts: [
        { label: "Dispatch", route: "/dispatch" },
        { label: "Trips", route: "/trips" },
        { label: "Proof Center", route: "/operations/proof-center" },
      ],
    };
  }

  if (/^\/(fleet-workspace|fleet-cold-chain|fleet-assets|fleet-saudi-readiness|fleet-compliance)/.test(pathname)) {
    return {
      clientOutcome: "Keep fleet control surfaces professional, credible, and easy to trust during client walkthroughs.",
      maintenanceOutcome: "The same surface language now spans fleet submodules, which reduces styling drift and makes future module delivery cheaper.",
      shortcuts: [
        { label: "Fleet Workspace", route: "/fleet-workspace" },
        { label: "Cold Chain", route: "/fleet-cold-chain" },
        { label: "Saudi Readiness", route: "/fleet-saudi-readiness" },
      ],
    };
  }

  return base;
}

const NOTIFS: Array<{ text: string; time: string; type: "danger" | "warning" | "info" }> = [];

export function AppShell() {
  const { session, logout } = useAuth();
  const location = useLocation();
  const navigate = useNavigate();
  const hasPermission = useHasPermission();

  const navStateKey = useMemo(() => getSessionIdentityKey(session), [session?.company?.id, session?.company?.companyId, session?.role, session?.user?.email, session?.user?.id, session?.user?.name]);
  const [sectionOpen, setSectionOpen] = useState<NavState | null>(null);
  const [mobileOpen, setMobileOpen] = useState(false);
  const [notifOpen, setNotifOpen] = useState(false);
  const [profileOpen, setProfileOpen] = useState(false);
  const [sidebarQuery, setSidebarQuery] = useState("");
  const [now, setNow] = useState(() => new Date());
  const notifRef = useRef<HTMLDivElement>(null);
  const profileRef = useRef<HTMLDivElement>(null);

  // live header clock
  useEffect(() => {
    const t = setInterval(() => setNow(new Date()), 1000);
    return () => clearInterval(t);
  }, []);

  useEffect(() => {
    try {
      const raw = localStorage.getItem(navStateKey);
      setSectionOpen(raw ? JSON.parse(raw) as NavState : {});
    } catch {
      setSectionOpen({});
    }
  }, [navStateKey]);

  useEffect(() => {
    if (sectionOpen === null) return;
    try {
      localStorage.setItem(navStateKey, JSON.stringify(sectionOpen));
    } catch {
      // Ignore storage failures. Navigation should continue to work without persistence.
    }
  }, [navStateKey, sectionOpen]);

  const activeModule = useMemo(() => findActiveModule(location.pathname), [location.pathname]);
  const pageBreadcrumbs = useMemo(() => buildBreadcrumbs(location.pathname, session), [location.pathname, session]);
  const currentPageTitle = pageBreadcrumbs.at(-1)?.label ?? activeModule?.title ?? "Dashboard";
  const visibleSections = useMemo(
    () => NAV_SECTIONS.map((section) => {
      const accessibleItems = section.items
        .map((key) => modules.find((module) => module.key === key || module.route === key || module.route === `/${key}`))
        .filter((module): module is (typeof modules)[number] => Boolean(module && (!module.requiredPermission || hasPermission(module.requiredPermission))));

      return {
        ...section,
        items: accessibleItems,
      };
    }).filter((section) => section.items.length > 0),
    [hasPermission, session?.permissions],
  );

  const normalizedSidebarQuery = sidebarQuery.trim().toLowerCase();
  const filteredSections = useMemo(() => {
    if (!normalizedSidebarQuery) return visibleSections;

    return visibleSections
      .map((section) => {
        const items = section.items.filter((module) => matchesFilter(module, section.label, normalizedSidebarQuery) || isRouteActive(module.route, location.pathname));
        return { ...section, items };
      })
      .filter((section) => section.items.length > 0);
  }, [location.pathname, normalizedSidebarQuery, visibleSections]);

  const experience = useMemo(() => getExperienceProfile(location.pathname, currentPageTitle), [location.pathname, currentPageTitle]);

  // close mobile sidebar & notif panel on route change
  useEffect(() => {
    setMobileOpen(false);
    setNotifOpen(false);
  }, [location.pathname]);

  // close notif + lang panels on outside click
  useEffect(() => {
    const handler = (e: MouseEvent) => {
      if (notifRef.current && !notifRef.current.contains(e.target as Node)) setNotifOpen(false);
      if (profileRef.current && !profileRef.current.contains(e.target as Node)) setProfileOpen(false);
    };
    document.addEventListener("mousedown", handler);
    return () => document.removeEventListener("mousedown", handler);
  }, []);

  const toggleGroup = (g: Group) =>
    setSectionOpen((prev) => {
      const next: NavState = { ...(prev ?? {}) };
      next[g] = !(prev?.[g] ?? true);
      return next;
    });

  const displayName = getSessionDisplayName(session);
  const initials = displayName
    .split(/\s+/)
    .filter(Boolean)
    .map((part) => part[0])
    .join("")
    .slice(0, 2)
    .toUpperCase() || "U";
  const roleLabel = getSessionRoleLabel(session);
  const companyLabel = getSessionCompanyLabel(session);
  const planLabel = getSessionPlanLabel(session);
  const accessibleModuleCount = visibleSections.reduce((total, section) => total + section.items.length, 0);
  const firstFilteredRoute = filteredSections.flatMap((section) => section.items).find((module) => matchesFilter(module, module.group, normalizedSidebarQuery));
  const backTarget = pageBreadcrumbs.length > 2 ? pageBreadcrumbs[pageBreadcrumbs.length - 2]?.to : undefined;

  /* ── Sidebar nav content (shared between desktop + mobile) ── */
  const navContent = (
    <div className="flex h-full flex-col gap-3.5">

      {/* Brand + search */}
      <div className="rounded-[26px] border border-slate-200/80 bg-white/82 px-3.5 py-3 shadow-[0_14px_34px_rgba(15,23,42,.05)] backdrop-blur-xl">
        <div className="flex items-center gap-3">
          <div className="shrink-0">
            <OpsTraxLogo size={34} />
          </div>
          <div className="min-w-0 flex-1">
            <div className="flex items-center gap-2">
              <p className="text-[15px] font-black tracking-tight text-slate-950">OpsTrax</p>
              <span className="inline-flex items-center gap-1 rounded-full bg-emerald-50 px-1.5 py-0.5 text-[9px] font-bold uppercase tracking-wide text-emerald-600 ring-1 ring-emerald-200">
                <span className="live-dot h-1.5 w-1.5" /> Live
              </span>
            </div>
            <p className="truncate text-[10px] font-semibold uppercase tracking-widest text-slate-400">{companyLabel}</p>
          </div>
        </div>

        <div className="mt-3 border-t border-slate-200/80 pt-3">
          <div className="flex items-center justify-between gap-2">
            <p className="text-[10px] font-black uppercase tracking-[0.24em] text-slate-400">Quick filter</p>
            <span className="rounded-full border border-slate-200 bg-slate-50 px-2 py-0.5 text-[10px] font-semibold text-slate-500">
              {accessibleModuleCount} visible
            </span>
          </div>
          <div className="relative mt-2">
            <Search className="pointer-events-none absolute left-3 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-slate-400" />
            <input
              className="field h-10 w-full rounded-xl py-0 pl-9 pr-3 text-[13px]"
              placeholder="Filter modules or press Enter…"
              value={sidebarQuery}
              onChange={(event) => setSidebarQuery(event.target.value)}
              onKeyDown={(event) => {
                if (event.key !== "Enter") return;
                event.preventDefault();
                if (firstFilteredRoute) {
                  navigate(firstFilteredRoute.route);
                  setSidebarQuery("");
                  return;
                }
                navigate(resolveSearchRoute(sidebarQuery));
                setSidebarQuery("");
              }}
            />
          </div>
        </div>
      </div>

      <div className="rounded-[22px] border border-slate-200/80 bg-white/74 px-3.5 py-3 shadow-[0_12px_30px_rgba(15,23,42,.04)] backdrop-blur-md">
        <div className="flex items-center justify-between gap-3">
          <div className="min-w-0">
            <p className="text-[10px] font-black uppercase tracking-[0.24em] text-slate-400">Signed in as</p>
            <p className="mt-1 truncate text-[14px] font-bold text-slate-950">{displayName}</p>
            <p className="truncate text-xs text-slate-500">{roleLabel} · {companyLabel}</p>
          </div>
          <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-2xl border border-teal-200 bg-gradient-to-br from-teal-50 to-blue-50 text-sm font-extrabold text-teal-700">
            {initials}
          </div>
        </div>
        <div className="mt-3 flex flex-wrap gap-1.5">
          <span className="badge badge-muted text-[9px]">{roleLabel}</span>
          <span className="badge badge-info text-[9px]">{planLabel}</span>
        </div>
      </div>

      {/* Navigation */}
      <nav className="flex-1 space-y-1 overflow-y-auto pb-2 pt-1">
        {filteredSections.length === 0 ? (
          <div className="rounded-[20px] border border-dashed border-slate-200 bg-white/70 px-4 py-5 text-center">
            <p className="text-sm font-semibold text-slate-800">No modules match this filter</p>
            <p className="mt-1 text-xs text-slate-500">Try a different search term or clear the filter to see all accessible sections.</p>
          </div>
        ) : filteredSections.map((section) => {
          const isPinnedOpen = section.items.some((module) => isRouteActive(module.route, location.pathname));
          const isOpen = normalizedSidebarQuery.length > 0 || isPinnedOpen || sectionOpen?.[section.label] !== false;
          return (
            <div key={section.label}>
              {/* Group header */}
              <button
                type="button"
                className="flex w-full items-center justify-between rounded-2xl px-3 py-2 transition-colors hover:bg-slate-100/80"
                onClick={() => toggleGroup(section.label)}
                aria-expanded={isOpen}
              >
                <span className="flex items-center gap-2">
                  <span className={`text-[10px] font-bold uppercase tracking-[0.22em] ${section.color} opacity-80`}>
                    {section.label}
                  </span>
                </span>
                <span className="flex items-center gap-1.5">
                  <ChevronDown
                    className="h-3.5 w-3.5 text-slate-400 transition-transform duration-200"
                    style={{ transform: isOpen ? "rotate(0deg)" : "rotate(-90deg)" }}
                  />
                </span>
              </button>

              {/* Group items */}
              {isOpen && (
                <div className="mt-1 space-y-1 pb-2 pl-1">
                  {section.items.map((module) => {
                    const Icon = moduleIcons[module.key];
                    const active = isRouteActive(module.route, location.pathname);
                    return (
                      <Link
                        key={module.key}
                        to={module.route}
                        title={module.description}
                        aria-current={active ? "page" : undefined}
                        className={`group relative flex items-center gap-2.5 rounded-xl py-2 pl-3.5 pr-3 text-sm transition-all duration-150 ${
                          active
                            ? "bg-gradient-to-r from-teal-50 via-blue-50 to-transparent font-semibold text-slate-950 shadow-sm ring-1 ring-blue-200/70"
                            : "text-slate-600 hover:bg-slate-100 hover:text-slate-950"
                        }`}
                      >
                        <>
                          {active && <span className="nav-active-bar" />}
                          <span className={`flex h-7 w-7 shrink-0 items-center justify-center rounded-lg transition-colors ${
                            active ? "bg-white shadow-sm ring-1 ring-slate-200/80" : "bg-transparent group-hover:bg-white/70"
                          }`}>
                            <Icon className={`h-4 w-4 shrink-0 transition-colors ${active ? section.color : "text-slate-400 group-hover:text-slate-600"}`} />
                          </span>
                          <span className="min-w-0 flex-1">
                            <span className="block truncate">{module.title}</span>
                            <span className="block truncate text-[10px] font-medium text-slate-400">{module.group}</span>
                          </span>
                        </>
                      </Link>
                    );
                  })}
                </div>
              )}
            </div>
          );
        })}
      </nav>

      {/* User footer */}
      <div className="border-t border-slate-200/80 pt-3">
        <div className="flex items-center gap-3 rounded-[18px] px-3 py-2.5 transition hover:bg-slate-100/80">
          <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded-xl bg-gradient-to-br from-teal-50 to-blue-50 border border-blue-200 text-sm font-extrabold text-blue-700">
            {initials}
          </div>
          <div className="min-w-0 flex-1">
            <p className="truncate text-sm font-semibold text-slate-900">{displayName}</p>
            <p className="truncate text-xs text-slate-500">{roleLabel} · {companyLabel}</p>
          </div>
          <button
            type="button"
            className="icon-btn shrink-0"
            title="Sign out"
            onClick={() => { void logout(); }}
          >
            <LogOut className="h-3.5 w-3.5" />
          </button>
        </div>
        <p className="px-3 pb-1 pt-2 text-[10px] text-slate-400">OpsTrax · Kode Kinetics</p>
      </div>
    </div>
  );

  return (
    <div className="control-shell h-screen overflow-hidden text-slate-800">

      {/* ── Desktop Sidebar ── */}
      <aside className="fixed left-4 top-4 bottom-4 z-30 hidden w-[300px] overflow-hidden rounded-[30px] border border-slate-200/80 bg-[linear-gradient(180deg,#ffffff_0%,#f7fbff_100%)] p-2.5 shadow-[0_24px_60px_rgba(15,23,42,.08)] xl:flex xl:flex-col">
        <div className="flex h-full flex-col overflow-hidden rounded-[26px] bg-white/42 p-2 backdrop-blur-xl">
          {navContent}
        </div>
      </aside>

      {/* ── Mobile Sidebar ── */}
      {mobileOpen && (
        <div className="fixed inset-0 z-50 xl:hidden anim-fade-in">
          <div
            className="absolute inset-0 bg-slate-900/30 backdrop-blur-sm"
            onClick={() => setMobileOpen(false)}
          />
          <aside className="anim-slide-left absolute inset-y-0 left-0 w-[272px] overflow-y-auto border-r border-slate-200 bg-[linear-gradient(180deg,#ffffff_0%,#f8fbff_100%)] p-3 shadow-2xl">
            <button
              type="button"
              aria-label="Close navigation"
              className="absolute right-3 top-3 icon-btn"
              onClick={() => setMobileOpen(false)}
            >
              <X className="h-4 w-4" />
            </button>
            {navContent}
          </aside>
        </div>
      )}

      {/* ── Main ── */}
      {/* Fixed to viewport height: header stays pinned, the page body below owns
          its own scroll so KPIs/metrics never leave the visible screen. */}
      <div className="flex h-screen flex-col overflow-hidden xl:pl-[316px]">

        {/* ── Header ── */}
        <header className="shell-header z-20 shrink-0 border-b border-slate-200 bg-white/92 backdrop-blur-xl">
          <div className="mx-auto max-w-[1800px] px-4 md:px-6">
            <div className="flex h-[54px] items-center gap-3">

              {/* Mobile menu button */}
              <button type="button" aria-label="Open navigation" className="icon-btn xl:hidden shrink-0" onClick={() => setMobileOpen(true)}>
                <Menu className="h-4 w-4" />
              </button>

              {/* Page context breadcrumb (fills the centre gap) */}
              <div className="ml-1 hidden min-w-0 items-center gap-3 border-l border-slate-200 pl-4 lg:flex">
                {backTarget ? (
                  <button
                    type="button"
                    className="inline-flex items-center gap-1.5 rounded-full border border-slate-200 bg-white px-3 py-1.5 text-[11px] font-semibold text-slate-600 shadow-sm transition hover:border-slate-300 hover:text-slate-900"
                    onClick={() => navigate(backTarget)}
                  >
                    <ChevronLeft className="h-3.5 w-3.5" />
                    Back
                  </button>
                ) : null}
                <nav aria-label="Breadcrumb" className="min-w-0">
                  <ol className="flex min-w-0 items-center gap-1.5">
                    {pageBreadcrumbs.map((crumb, index) => {
                      const isLast = index === pageBreadcrumbs.length - 1;
                      return (
                        <li key={`${crumb.label}-${index}`} className="flex min-w-0 items-center gap-1.5">
                          {index > 0 ? <ChevronRight className="h-3.5 w-3.5 shrink-0 text-slate-300" /> : null}
                          {crumb.to && !isLast ? (
                            <Link
                              to={crumb.to}
                              className={`truncate text-[12px] font-semibold transition hover:text-slate-950 ${
                                index === 0 ? "uppercase tracking-[0.18em] text-slate-400" : "text-slate-600"
                              }`}
                            >
                              {crumb.label}
                            </Link>
                          ) : (
                            <span
                              className={`truncate text-[12px] font-semibold ${
                                isLast ? "text-slate-900" : index === 0 ? "uppercase tracking-[0.18em] text-slate-400" : "text-slate-500"
                              }`}
                            >
                              {crumb.label}
                            </span>
                          )}
                        </li>
                      );
                    })}
                  </ol>
                </nav>
              </div>

              {/* Live clock */}
              <div className="ml-auto hidden flex-col items-end leading-none md:flex">
                <span className="font-mono text-[15px] font-bold tabular-nums text-slate-800">
                  {now.toLocaleTimeString("en-GB", { hour12: false })}
                </span>
                <span className="text-[10px] font-semibold uppercase tracking-wider text-slate-400">
                  {now.toLocaleDateString("en-GB", { weekday: "short", day: "2-digit", month: "short" })}
                </span>
              </div>

              {/* Right section */}
              <div className="ml-auto flex items-center gap-2 md:ml-3 md:border-l md:border-slate-200 md:pl-3">

                {/* Live status */}
                <div className="hidden sm:flex items-center gap-1.5 rounded-full border border-emerald-500/30 bg-emerald-50 px-2.5 py-1 text-[11px] font-semibold text-emerald-700">
                  <span className="live-dot h-1.5 w-1.5" />
                  Live
                </div>

                {/* Notifications */}
                <div className="relative" ref={notifRef}>
                  <button
                    type="button"
                    aria-label="Notifications"
                    className="icon-btn relative h-8 w-8"
                    onClick={() => setNotifOpen((v) => !v)}
                  >
                    <Bell className="h-3.5 w-3.5" />
                    {NOTIFS.length > 0 ? <span className="notif-badge">{NOTIFS.length}</span> : null}
                  </button>

                  {notifOpen && (
                    <div className="panel anim-slide-right absolute right-0 top-full z-50 mt-2 w-[300px] overflow-hidden p-0">
                      <div className="flex items-center justify-between border-b border-slate-200 px-4 py-3">
                        <p className="section-title">Notifications</p>
                        <span className="rounded-full border border-slate-200 bg-slate-50 px-2 py-0.5 text-[10px] font-bold text-slate-500">
                          {NOTIFS.length} new
                        </span>
                      </div>
                      <div className="max-h-[320px] overflow-y-auto divide-y divide-slate-100">
                        {NOTIFS.length === 0 ? (
                          <div className="px-4 py-8 text-center">
                            <p className="text-sm font-semibold text-slate-700">No notifications yet</p>
                            <p className="mt-1 text-xs text-slate-500">Live alerts will appear here when the backend emits them.</p>
                          </div>
                        ) : (
                          NOTIFS.map((n, i) => (
                            <div key={i} className="flex items-start gap-3 px-4 py-3 transition hover:bg-slate-50 cursor-pointer">
                              <span
                                className={`mt-0.5 h-2 w-2 shrink-0 rounded-full ${
                                  n.type === "danger" ? "bg-red-400" : n.type === "warning" ? "bg-amber-400" : "bg-sky-400"
                                }`}
                              />
                              <div className="min-w-0 flex-1">
                                <p className="text-[13px] text-slate-700 leading-snug">{n.text}</p>
                                <p className="mt-0.5 text-xs text-slate-500">{n.time}</p>
                              </div>
                            </div>
                          ))
                        )}
                      </div>
                      <div className="border-t border-slate-200 px-4 py-2.5">
                        <button
                          type="button"
                          className="text-xs font-semibold text-teal-700 hover:text-teal-600 transition"
                          onClick={() => navigate("/audit-logs")}
                        >
                          View all notifications
                        </button>
                      </div>
                    </div>
                  )}
                </div>

                {/* Avatar / profile dropdown */}
                <div className="relative" ref={profileRef}>
                  <button
                    type="button"
                    className="flex h-8 w-8 shrink-0 items-center justify-center rounded-lg bg-linear-to-br from-teal-500/15 to-blue-500/10 border border-teal-500/25 text-[13px] font-extrabold text-teal-700 transition hover:border-teal-500/40 hover:from-teal-500/25"
                    title="My profile"
                    onClick={() => setProfileOpen((v) => !v)}
                  >
                    {initials}
                  </button>
                  {profileOpen && (
                    <div className="panel absolute right-0 top-full z-50 mt-2 w-56 overflow-hidden p-0 shadow-lg">
                      {/* User info */}
                      <div className="flex items-center gap-3 px-4 py-3 border-b border-slate-100 bg-slate-50">
                        <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg bg-linear-to-br from-teal-100 to-blue-100 text-[14px] font-extrabold text-teal-700">
                          {initials}
                        </div>
                        <div className="min-w-0">
                          <p className="text-sm font-bold text-slate-900 truncate">{displayName}</p>
                          <p className="text-[11px] text-slate-500 truncate">{roleLabel} · {companyLabel}</p>
                        </div>
                      </div>
                      {/* Actions */}
                      <div className="py-1">
                        <button
                          type="button"
                          className="flex w-full items-center gap-3 px-4 py-2.5 text-[13px] text-slate-700 hover:bg-slate-50 transition"
                          onClick={() => { navigate("/settings"); setProfileOpen(false); }}
                        >
                          <Settings className="h-4 w-4 text-slate-400" />
                          Settings
                        </button>
                        <button
                          type="button"
                          className="flex w-full items-center gap-3 px-4 py-2.5 text-[13px] text-slate-700 hover:bg-slate-50 transition"
                          onClick={() => { navigate("/user-management"); setProfileOpen(false); }}
                        >
                          <User className="h-4 w-4 text-slate-400" />
                          User Management
                        </button>
                      </div>
                      <div className="border-t border-slate-100 py-1">
                        <button
                          type="button"
                          className="flex w-full items-center gap-3 px-4 py-2.5 text-[13px] text-red-600 hover:bg-red-50 transition"
                          onClick={() => { void logout().finally(() => setProfileOpen(false)); }}
                        >
                          <LogOut className="h-4 w-4" />
                          Sign out
                        </button>
                      </div>
                    </div>
                  )}
                </div>
              </div>
            </div>
          </div>
        </header>

        {/* ── Body: fills the space under the fixed header. Pages that want a
            fixed-viewport layout render a `flex h-full flex-col` root and let their
            own data region scroll; simpler pages just scroll this container. ── */}
        <div className="flex min-h-0 flex-1 flex-col overflow-y-auto">
          <div className="mx-auto w-full max-w-[1800px] shrink-0 px-4 pt-4 md:px-6">
            <WorkspaceExperience
              pageTitle={currentPageTitle}
              clientOutcome={experience.clientOutcome}
              maintenanceOutcome={experience.maintenanceOutcome}
              shortcuts={experience.shortcuts}
            />
          </div>

          {/* ── Content ── */}
          <main className="mx-auto flex w-full min-h-0 max-w-[1800px] flex-1 flex-col px-4 py-6 md:px-6">
            <Outlet />
          </main>
        </div>
      </div>
    </div>
  );
}
