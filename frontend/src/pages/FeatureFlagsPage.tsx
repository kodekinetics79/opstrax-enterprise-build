import { useState, useCallback } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiClient, unwrap } from "@/services/apiClient";
import { ErrorState, LoadingState } from "@/components/ui";
import type { AnyRecord } from "@/types";

const flagsApi = () =>
  unwrap<AnyRecord[]>(apiClient.get("/api/feature-flags")).then((rows) =>
    rows.map((r) => ({
      ...r,
      flagKey: r.flagKey ?? r.flag_key ?? r.record_code ?? String(r.id),
      name: r.name ?? r.title ?? "",
      category: r.category ?? "General",
      enabled: Boolean(r.enabled ?? r.status === "Active"),
      rolloutPct: Number(r.rolloutPct ?? r.rollout_pct ?? r.numericValue ?? r.numeric_value ?? 0),
      env: r.env ?? r.environment ?? "Production",
      lastModified: r.lastModified ?? r.last_modified ?? r.updatedAt ?? "",
      description: r.description ?? r.notes ?? "",
    }))
  );

// ── Helpers ──────────────────────────────────────────────────────────────────

function EnvBadge({ env }: { env: string }) {
  const cls =
    env === "Production" ? "bg-teal-50 border-teal-200 text-teal-700" :
    env === "Staging"    ? "bg-blue-50 border-blue-200 text-blue-700" :
    env === "Beta"       ? "bg-violet-50 border-violet-200 text-violet-700" :
    "bg-slate-50 border-slate-200 text-slate-500";
  return <span className={`inline-flex text-xs px-2 py-0.5 rounded-full border font-medium ${cls}`}>{env}</span>;
}

function CategoryBadge({ cat }: { cat: string }) {
  const cls: Record<string, string> = {
    Fleet:       "bg-orange-50 border-orange-200 text-orange-700",
    Safety:      "bg-red-50 border-red-200 text-red-700",
    CRM:         "bg-blue-50 border-blue-200 text-blue-700",
    Finance:     "bg-amber-50 border-amber-200 text-amber-700",
    Telematics:  "bg-teal-50 border-teal-200 text-teal-700",
    Beta:        "bg-violet-50 border-violet-200 text-violet-700",
  };
  const c = cls[cat] ?? "bg-slate-50 border-slate-200 text-slate-600";
  return <span className={`inline-flex text-xs px-2 py-0.5 rounded-full border font-medium ${c}`}>{cat}</span>;
}

function RolloutBar({ pct }: { pct: number }) {
  const color = pct === 100 ? "bg-teal-500" : pct > 0 ? "bg-amber-500" : "bg-slate-200";
  return (
    <div className="flex items-center gap-2">
      <div className="w-16 h-1.5 bg-slate-100 rounded-full overflow-hidden">
        <div className={`h-full ${color} rounded-full`} style={{ width: `${pct}%` }} />
      </div>
      <span className="text-xs text-slate-500">{pct}%</span>
    </div>
  );
}

function ToggleSwitch({ enabled, onChange, disabled }: { enabled: boolean; onChange: () => void; disabled?: boolean }) {
  return (
    <button type="button" disabled={disabled} onClick={onChange}
      className={`relative inline-flex h-5 w-9 items-center rounded-full transition-colors focus:outline-none focus:ring-2 focus:ring-teal-400 focus:ring-offset-1 ${
        enabled ? "bg-teal-500" : "bg-slate-300"
      } ${disabled ? "opacity-50 cursor-not-allowed" : "cursor-pointer"}`}>
      <span className={`inline-block h-3.5 w-3.5 transform rounded-full bg-white shadow transition-transform ${
        enabled ? "translate-x-4" : "translate-x-0.5"
      }`} />
    </button>
  );
}

// ── Main page ─────────────────────────────────────────────────────────────────

const ALL_CATEGORIES = ["All", "Fleet", "Safety", "CRM", "Finance", "Telematics", "Beta"] as const;
type CategoryFilter = typeof ALL_CATEGORIES[number];

export function FeatureFlagsPage() {
  const [catFilter, setCatFilter] = useState<CategoryFilter>("All");
  const [statusFilter, setStatusFilter] = useState<"All" | "Enabled" | "Disabled">("All");
  const [search, setSearch] = useState("");
  const [localStates, setLocalStates] = useState<Record<string, boolean>>({});
  const qc = useQueryClient();

  const q = useQuery({ queryKey: ["feature-flags"], queryFn: flagsApi });
  const toggleMut = useMutation({
    mutationFn: ({ key, enabled }: { key: string; enabled: boolean }) =>
      apiClient.put(`/api/feature-flags/${encodeURIComponent(key)}/toggle`, { enabled }),
    onSuccess: () => { void qc.invalidateQueries({ queryKey: ["feature-flags"] }); },
    onError: (_error, variables) => {
      setLocalStates((prev) => {
        const next = { ...prev };
        delete next[variables.key];
        return next;
      });
    },
  });

  const flags = (q.data ?? []) as AnyRecord[];

  const handleToggle = useCallback((flagKey: string, currentEnabled: boolean) => {
    setLocalStates((prev) => ({ ...prev, [flagKey]: !currentEnabled }));
    toggleMut.mutate({ key: flagKey, enabled: !currentEnabled });
  }, [toggleMut]);

  const filtered = flags.filter((f) => {
    const isEnabled = localStates[String(f.flagKey)] !== undefined ? localStates[String(f.flagKey)] : Boolean(f.enabled);
    if (catFilter !== "All" && f.category !== catFilter) return false;
    if (statusFilter === "Enabled" && !isEnabled) return false;
    if (statusFilter === "Disabled" && isEnabled) return false;
    if (search) {
      const q = search.toLowerCase();
      return (
        String(f.name ?? "").toLowerCase().includes(q) ||
        String(f.flagKey ?? "").toLowerCase().includes(q) ||
        String(f.description ?? "").toLowerCase().includes(q)
      );
    }
    return true;
  });

  const enabledCount = flags.filter((f) => {
    const key = String(f.flagKey);
    return localStates[key] !== undefined ? localStates[key] : Boolean(f.enabled);
  }).length;

  if (q.isLoading) return <LoadingState />;
  if (q.isError) return <ErrorState message={(q.error as Error)?.message ?? "Unable to load feature flags."} />;

  return (
    <div className="flex flex-col gap-6 py-6">
      <div>
        <h1 className="text-xl font-bold text-slate-900">Feature Flags</h1>
        <p className="text-sm text-slate-500 mt-0.5">Tenant feature rollout, beta controls and operational toggles — enable or disable platform capabilities per environment</p>
      </div>

      {/* KPI strip */}
      <div className="flex flex-wrap gap-3">
        {[
          { label: "Total Flags",   val: flags.length },
          { label: "Enabled",       val: enabledCount,              accent: "text-teal-600" },
          { label: "Disabled",      val: flags.length - enabledCount, accent: enabledCount < flags.length ? "text-amber-600" : "text-slate-400" },
          { label: "Beta / Staging", val: flags.filter((f) => ["Beta", "Staging"].includes(String(f.env ?? ""))).length, accent: "text-violet-600" },
        ].map(({ label, val, accent }) => (
          <div key={label} className="panel flex flex-col gap-1 min-w-28">
            <span className={`text-xl font-bold ${accent ?? "text-slate-900"}`}>{val}</span>
            <span className="text-xs text-slate-500 font-medium">{label}</span>
          </div>
        ))}
      </div>

      {/* Filters */}
      <div className="panel flex flex-wrap gap-3 items-center">
        <div className="flex gap-1.5 flex-wrap">
          {ALL_CATEGORIES.map((cat) => (
            <button key={cat} type="button" onClick={() => setCatFilter(cat)}
              className={`px-3 py-1.5 rounded-lg text-sm font-medium border transition-colors ${
                catFilter === cat ? "bg-teal-50 border-teal-300 text-teal-700" : "bg-slate-50 border-slate-200 text-slate-600 hover:bg-slate-100"
              }`}>{cat}</button>
          ))}
        </div>
        <select title="Status" value={statusFilter} onChange={(e) => setStatusFilter(e.target.value as "All" | "Enabled" | "Disabled")}
          className="border border-slate-200 rounded-lg px-3 py-1.5 text-sm text-slate-700 focus:outline-none focus:ring-2 focus:ring-teal-400">
          <option value="All">All Statuses</option>
          <option value="Enabled">Enabled</option>
          <option value="Disabled">Disabled</option>
        </select>
        <input type="search" placeholder="Search flag name or key…" value={search} onChange={(e) => setSearch(e.target.value)}
          className="ml-auto border border-slate-200 rounded-lg px-3 py-1.5 text-sm text-slate-900 placeholder-slate-400 focus:outline-none focus:ring-2 focus:ring-teal-400 w-56" />
      </div>

      {/* Flag cards grouped by category */}
      {(catFilter === "All" ? (["Fleet", "Safety", "CRM", "Finance", "Telematics", "Beta"] as const) : [catFilter]).map((cat) => {
        const catFlags = filtered.filter((f) => f.category === cat);
        if (catFlags.length === 0) return null;
        return (
          <div key={cat}>
            <div className="flex items-center gap-2 mb-3">
              <CategoryBadge cat={cat} />
              <span className="text-xs text-slate-400">{catFlags.length} flag{catFlags.length !== 1 ? "s" : ""}</span>
            </div>
            <div className="flex flex-col gap-0 panel overflow-hidden p-0">
              {catFlags.map((flag, i) => {
                const key = String(flag.flagKey);
                const isEnabled = localStates[key] !== undefined ? localStates[key] : Boolean(flag.enabled);
                const isLast = i === catFlags.length - 1;
                return (
                  <div key={key} className={`flex items-center gap-4 px-5 py-4 hover:bg-slate-50 ${!isLast ? "border-b border-slate-100" : ""}`}>
                    <ToggleSwitch enabled={isEnabled} onChange={() => handleToggle(key, isEnabled)} disabled={toggleMut.isPending} />
                    <div className="flex-1 min-w-0">
                      <div className="flex items-center gap-2 flex-wrap">
                        <p className={`text-sm font-semibold ${isEnabled ? "text-slate-900" : "text-slate-400"}`}>{String(flag.name ?? key)}</p>
                        <EnvBadge env={String(flag.env ?? "Production")} />
                      </div>
                      <p className="text-xs text-slate-500 mt-0.5 truncate">{String(flag.description ?? "")}</p>
                      <p className="text-xs text-slate-400 mt-0.5 font-mono">{key}</p>
                    </div>
                    <div className="shrink-0 hidden sm:block">
                      <RolloutBar pct={Math.min(100, Math.max(0, Number(flag.rolloutPct ?? 0)))} />
                    </div>
                    <p className="text-xs text-slate-400 shrink-0 hidden md:block w-24 text-right">{String(flag.lastModified ?? "")}</p>
                  </div>
                );
              })}
            </div>
          </div>
        );
      })}
    </div>
  );
}
