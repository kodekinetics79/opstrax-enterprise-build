import { useEffect, useMemo, useState, type ReactNode } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  Activity,
  AlertTriangle,
  ArrowRightLeft,
  Building2,
  CheckCircle2,
  Cloud,
  Fuel,
  Link2,
  MapPinned,
  PlugZap,
  RadioTower,
  RefreshCw,
  Search,
  Settings2,
  ShieldCheck,
  Warehouse,
  X,
  Zap,
} from "lucide-react";
import { exportCsv, EmptyState, ErrorState, LoadingState, PageHeader, StatusBadge } from "@/components/ui";
import { useHasPermission } from "@/hooks/usePermission";
import {
  integrationsApi,
  type IntegrationCategory,
  type IntegrationRecord,
  type IntegrationsPayload,
} from "@/services/integrationsApi";

type Tab = "Connectors" | "Activity Log";

type ConfigField = {
  key: string;
  label: string;
  type: "text" | "url" | "number";
  placeholder?: string;
  note?: string;
};

const CATEGORY_META: Record<IntegrationCategory, { icon: ReactNode; accent: string }> = {
  "ERP & Accounting": { icon: <Building2 className="h-3.5 w-3.5" />, accent: "bg-blue-50 border-blue-200 text-blue-700" },
  "Telematics & ELD": { icon: <RadioTower className="h-3.5 w-3.5" />, accent: "bg-teal-50 border-teal-200 text-teal-700" },
  "Fuel Cards": { icon: <Fuel className="h-3.5 w-3.5" />, accent: "bg-amber-50 border-amber-200 text-amber-700" },
  "Maps & Routing": { icon: <MapPinned className="h-3.5 w-3.5" />, accent: "bg-green-50 border-green-200 text-green-700" },
  "Messaging & Notifications": { icon: <PlugZap className="h-3.5 w-3.5" />, accent: "bg-violet-50 border-violet-200 text-violet-700" },
  "WMS & Shipment Ops": { icon: <Warehouse className="h-3.5 w-3.5" />, accent: "bg-indigo-50 border-indigo-200 text-indigo-700" },
  "IoT & Sensors": { icon: <Cloud className="h-3.5 w-3.5" />, accent: "bg-sky-50 border-sky-200 text-sky-700" },
  Compliance: { icon: <ShieldCheck className="h-3.5 w-3.5" />, accent: "bg-emerald-50 border-emerald-200 text-emerald-700" },
};

function categoryFields(category: IntegrationCategory): ConfigField[] {
  switch (category) {
    case "ERP & Accounting":
      return [
        { key: "baseUrl", label: "ERP base URL", type: "url", placeholder: "https://erp.example.com", note: "Used for invoices, orders, and finance sync." },
        { key: "companyCode", label: "Company code", type: "text", placeholder: "NSFL" },
        { key: "syncIntervalMinutes", label: "Sync interval (minutes)", type: "number", placeholder: "15" },
      ];
    case "Telematics & ELD":
      return [
        { key: "providerAccountId", label: "Provider account ID", type: "text", placeholder: "sam-1001" },
        { key: "apiKey", label: "API key", type: "text", placeholder: "sk-..." },
        { key: "webhookUrl", label: "Webhook URL", type: "url", placeholder: "https://ops.opstrax.com/webhooks/telematics" },
      ];
    case "Fuel Cards":
      return [
        { key: "accountId", label: "Account ID", type: "text", placeholder: "wex-88231" },
        { key: "apiKey", label: "API key", type: "text", placeholder: "fc-..." },
        { key: "syncIntervalMinutes", label: "Sync interval (minutes)", type: "number", placeholder: "5" },
      ];
    case "Maps & Routing":
      return [
        { key: "apiKey", label: "Maps API key", type: "text", placeholder: "maps-..." },
        { key: "routingProfile", label: "Routing profile", type: "text", placeholder: "truck-default" },
        { key: "region", label: "Region", type: "text", placeholder: "global" },
      ];
    case "Messaging & Notifications":
      return [
        { key: "sender", label: "Sender / channel", type: "text", placeholder: "OpsTrax" },
        { key: "webhookUrl", label: "Webhook URL", type: "url", placeholder: "https://hooks.opstrax.com/notify" },
        { key: "channel", label: "Target channel", type: "text", placeholder: "#ops" },
      ];
    case "WMS & Shipment Ops":
      return [
        { key: "baseUrl", label: "WMS endpoint", type: "url", placeholder: "https://wms.example.com" },
        { key: "warehouseCode", label: "Warehouse code", type: "text", placeholder: "DC-01" },
        { key: "syncIntervalMinutes", label: "Sync interval (minutes)", type: "number", placeholder: "10" },
      ];
    case "IoT & Sensors":
      return [
        { key: "brokerUrl", label: "Broker URL", type: "url", placeholder: "mqtts://broker.example.com" },
        { key: "topicPrefix", label: "Topic prefix", type: "text", placeholder: "opstrax/fleet" },
        { key: "syncIntervalMinutes", label: "Sync interval (minutes)", type: "number", placeholder: "1" },
      ];
    case "Compliance":
      return [
        { key: "profile", label: "Compliance profile", type: "text", placeholder: "US FMCSA" },
        { key: "exportWindow", label: "Export window", type: "text", placeholder: "daily" },
        { key: "syncIntervalMinutes", label: "Sync interval (minutes)", type: "number", placeholder: "1440" },
      ];
  }
}

function categoryText(category: IntegrationCategory) {
  switch (category) {
    case "ERP & Accounting":
      return "Finance and ERP systems";
    case "Telematics & ELD":
      return "Live fleet and driver telemetry";
    case "Fuel Cards":
      return "Fuel and spend controls";
    case "Maps & Routing":
      return "Maps, geocoding, and route optimization";
    case "Messaging & Notifications":
      return "Driver, customer, and ops notifications";
    case "WMS & Shipment Ops":
      return "Warehouse and shipment execution";
    case "IoT & Sensors":
      return "Device and sensor telemetry";
    case "Compliance":
      return "Authority and reporting workflows";
  }
}

function CategoryBadge({ category }: { category: IntegrationCategory }) {
  const meta = CATEGORY_META[category];
  return (
    <span className={`inline-flex items-center gap-1.5 rounded-full border px-2.5 py-[3px] text-[10px] font-bold uppercase tracking-[0.14em] ${meta.accent}`}>
      {meta.icon}
      {category}
    </span>
  );
}

function ConnectorPill({ value }: { value: string }) {
  return (
    <span className="inline-flex items-center rounded-full border border-slate-200 bg-slate-50 px-2.5 py-[3px] text-[10px] font-semibold text-slate-600">
      {value}
    </span>
  );
}

function formatConfigValue(value: string | number | boolean | null | undefined) {
  if (value === null || value === undefined || value === "") return "";
  return String(value);
}

function buildFormState(record: IntegrationRecord) {
  const fields = categoryFields(record.category);
  return Object.fromEntries(fields.map((field) => [field.key, formatConfigValue(record.config[field.key])])) as Record<string, string>;
}

function buildConfigPayload(record: IntegrationRecord, form: Record<string, string>) {
  const payload: Record<string, string | number | boolean | null> = {};
  for (const field of categoryFields(record.category)) {
    const raw = String(form[field.key] ?? "").trim();
    if (!raw) {
      payload[field.key] = null;
    } else if (field.type === "number") {
      const parsed = Number(raw);
      payload[field.key] = Number.isFinite(parsed) ? parsed : raw;
    } else {
      payload[field.key] = raw;
    }
  }
  return payload;
}

function ConfigDrawer({
  integration,
  onClose,
}: {
  integration: IntegrationRecord;
  onClose: () => void;
}) {
  const qc = useQueryClient();
  const [saved, setSaved] = useState(false);
  const [form, setForm] = useState<Record<string, string>>(() => buildFormState(integration));

  useEffect(() => {
    setForm(buildFormState(integration));
    setSaved(false);
  }, [integration]);

  const saveMut = useMutation({
    mutationFn: (payload: Record<string, string | number | boolean | null>) =>
      integrationsApi.configure(integration.id, payload),
    onSuccess: async () => {
      setSaved(true);
      await qc.invalidateQueries({ queryKey: ["integrations"] });
      setTimeout(() => setSaved(false), 2200);
    },
  });

  const fields = categoryFields(integration.category);

  return (
    <div className="fixed inset-0 z-50 flex justify-end bg-black/50 backdrop-blur-sm">
      <aside className="flex h-full w-full max-w-xl flex-col gap-5 overflow-y-auto border-l border-slate-200 bg-white p-6">
        <div className="flex items-start justify-between gap-4">
          <div className="min-w-0">
            <p className="text-[11px] font-bold uppercase tracking-[0.22em] text-teal-600">Integration configuration</p>
            <h2 className="mt-2 truncate text-xl font-bold text-slate-900">{integration.name}</h2>
            <p className="mt-1 text-sm text-slate-500">{categoryText(integration.category)}</p>
          </div>
          <button type="button" aria-label="Close" className="icon-btn" onClick={onClose}>
            <X className="h-5 w-5" />
          </button>
        </div>

        <div className="rounded-2xl border border-slate-200 bg-slate-50 p-4">
          <div className="flex items-center justify-between gap-3">
            <StatusBadge status={integration.status} />
            <span className="text-xs text-slate-500">Last sync: {integration.sync}</span>
          </div>
          <p className="mt-3 text-sm leading-6 text-slate-600">{integration.description}</p>
          <div className="mt-4 flex flex-wrap gap-2">
            {integration.relatedSystems.map((item) => (
              <ConnectorPill key={item} value={item} />
            ))}
          </div>
        </div>

        <form
          className="flex flex-col gap-4"
          onSubmit={(event) => {
            event.preventDefault();
            void saveMut.mutateAsync(buildConfigPayload(integration, form));
          }}
        >
          <div className="grid gap-4 md:grid-cols-2">
            <div>
              <label className="field-label">Managed by</label>
              <input className="field mt-1 w-full bg-slate-50" value={integration.managedBy} readOnly />
            </div>
            <div>
              <label className="field-label">Tenant scope</label>
              <input className="field mt-1 w-full bg-slate-50" value={`Tenant ${integration.tenantId}`} readOnly />
            </div>
          </div>

          <div className="space-y-4 rounded-2xl border border-slate-200 bg-white p-4">
            <div className="flex items-center justify-between gap-3">
              <div>
                <p className="text-sm font-semibold text-slate-800">Connection settings</p>
                <p className="text-xs text-slate-500">These settings are persisted in the live backend state for this tenant.</p>
              </div>
              <Settings2 className="h-4 w-4 text-slate-400" />
            </div>

            {fields.map((field) => (
              <div key={field.key}>
                <label className="field-label">{field.label}</label>
                <input
                  type={field.type}
                  className="field mt-1 w-full"
                  value={form[field.key] ?? ""}
                  onChange={(event) => setForm((current) => ({ ...current, [field.key]: event.target.value }))}
                  placeholder={field.placeholder}
                />
                {field.note && <p className="mt-1 text-xs text-slate-400">{field.note}</p>}
              </div>
            ))}
          </div>

          {integration.category === "Messaging & Notifications" && (
            <div className="rounded-2xl border border-slate-200 bg-slate-50 p-4">
              <p className="text-xs font-bold uppercase tracking-[0.18em] text-slate-500">Notification routing</p>
              <div className="mt-3 flex items-center gap-2 text-sm text-slate-600">
                <ArrowRightLeft className="h-4 w-4 text-teal-500" />
                Operational alerts and customer notifications are routed through this connector live.
              </div>
            </div>
          )}

          <div className="flex items-center gap-3 border-t border-slate-100 pt-2">
            <button type="submit" className="btn-primary flex-1" disabled={saveMut.isPending}>
              {saveMut.isPending ? "Saving..." : "Save configuration"}
            </button>
            <button type="button" className="btn-ghost" onClick={onClose}>
              Cancel
            </button>
            {saved && (
              <span className="flex items-center gap-1 text-xs font-semibold text-teal-600">
                <CheckCircle2 className="h-3.5 w-3.5" />
                Saved
              </span>
            )}
          </div>
        </form>
      </aside>
    </div>
  );
}

export function IntegrationsPage() {
  const hasPermission = useHasPermission();
  const canManage = hasPermission("telematics:providers:manage");
  const qc = useQueryClient();

  const [tab, setTab] = useState<Tab>("Connectors");
  const [categoryFilter, setCategoryFilter] = useState<string>("All");
  const [statusFilter, setStatusFilter] = useState<string>("All");
  const [search, setSearch] = useState("");
  const [configTarget, setConfigTarget] = useState<IntegrationRecord | null>(null);

  const q = useQuery<IntegrationsPayload>({
    queryKey: ["integrations"],
    queryFn: integrationsApi.list,
  });

  const payload = q.data;
  const integrations = payload?.records ?? [];
  const activity = payload?.activity ?? [];
  const summary = payload?.summary ?? {
    total: integrations.length,
    connected: integrations.filter((item) => item.status === "Connected").length,
    pending: integrations.filter((item) => item.status === "Pending").length,
    errors: integrations.filter((item) => item.status === "Error").length,
    categories: new Set(integrations.map((item) => item.category)).size,
    lastUpdated: new Date().toISOString(),
  };

  const categories = useMemo(
    () => ["All", ...Array.from(new Set(integrations.map((item) => item.category)))],
    [integrations],
  );

  const filtered = integrations.filter((integration) => {
    if (categoryFilter !== "All" && integration.category !== categoryFilter) return false;
    if (statusFilter !== "All" && integration.status !== statusFilter) return false;
    if (search) {
      const haystack = [
        integration.name,
        integration.category,
        integration.description,
        integration.managedBy,
        ...integration.relatedSystems,
        ...integration.connectedTo,
      ].join(" ").toLowerCase();
      if (!haystack.includes(search.toLowerCase())) return false;
    }
    return true;
  });

  const connectMut = useMutation({
    mutationFn: (id: number) => integrationsApi.connect(id),
    onSuccess: async () => {
      await qc.invalidateQueries({ queryKey: ["integrations"] });
    },
  });

  const disconnectMut = useMutation({
    mutationFn: (id: number) => integrationsApi.disconnect(id),
    onSuccess: async () => {
      await qc.invalidateQueries({ queryKey: ["integrations"] });
    },
  });

  const syncMut = useMutation({
    mutationFn: (id: number) => integrationsApi.sync(id),
    onSuccess: async () => {
      await qc.invalidateQueries({ queryKey: ["integrations"] });
    },
  });

  if (q.isLoading) return <LoadingState />;
  if (q.isError) {
    return (
      <div className="py-6">
        <ErrorState message="Unable to load live integrations from the backend." />
      </div>
    );
  }

  return (
    <div className="flex h-full flex-col gap-6 overflow-y-auto py-6">
      <PageHeader
        eyebrow="Connector hub"
        title="Integrations"
        description="Live connector hub for ERP, accounting, telematics, fuel cards, routing, messaging, WMS, IoT, and compliance. Unrelated categories have been removed."
        actions={
          <>
            <button
              type="button"
              className="btn-ghost text-sm"
              onClick={() => exportCsv("integrations", integrations)}
            >
              Export CSV
            </button>
            <button
              type="button"
              className="btn-ghost text-sm"
              onClick={() => void qc.invalidateQueries({ queryKey: ["integrations"] })}
            >
              Refresh
            </button>
          </>
        }
      />

      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-5">
        {[
          { label: "Connectors", val: summary.total, accent: "text-slate-900" },
          { label: "Connected", val: summary.connected, accent: "text-teal-600" },
          { label: "Pending", val: summary.pending, accent: summary.pending > 0 ? "text-amber-600" : "text-slate-400" },
          { label: "Errors", val: summary.errors, accent: summary.errors > 0 ? "text-red-600" : "text-slate-400" },
          { label: "Categories", val: summary.categories, accent: "text-slate-600" },
        ].map((item) => (
          <div key={item.label} className="panel flex flex-col gap-1">
            <span className={`text-xl font-bold ${item.accent}`}>{item.val}</span>
            <span className="text-xs font-medium text-slate-500">{item.label}</span>
          </div>
        ))}
      </div>

      <div className="flex gap-1 self-start rounded-xl border border-slate-200 bg-slate-50 p-1">
        {(["Connectors", "Activity Log"] as Tab[]).map((item) => (
          <button
            key={item}
            type="button"
            onClick={() => setTab(item)}
            className={`flex items-center gap-1.5 rounded-lg px-4 py-1.5 text-sm font-medium transition ${
              tab === item ? "bg-white text-slate-900 shadow-sm" : "text-slate-500 hover:text-slate-700"
            }`}
          >
            {item === "Connectors" ? <Link2 className="h-3.5 w-3.5" /> : <Activity className="h-3.5 w-3.5" />}
            {item}
          </button>
        ))}
      </div>

      {tab === "Connectors" && (
        <>
          <div className="panel flex flex-wrap items-center gap-3">
            <div className="relative">
              <Search className="absolute left-2.5 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-slate-400" />
              <input
                className="field w-56 pl-8 text-sm"
                value={search}
                onChange={(event) => setSearch(event.target.value)}
                placeholder="Search connectors..."
              />
            </div>

            <div className="flex flex-wrap gap-1.5">
              {categories.map((item) => (
                <button
                  key={item}
                  type="button"
                  onClick={() => setCategoryFilter(item)}
                  className={`rounded-lg border px-2.5 py-1 text-xs font-medium transition-colors ${
                    categoryFilter === item
                      ? "border-teal-300 bg-teal-50 text-teal-700"
                      : "border-slate-200 bg-slate-50 text-slate-600 hover:bg-slate-100"
                  }`}
                >
                  {item}
                </button>
              ))}
            </div>

            <select
              aria-label="Status filter"
              className="field ml-auto w-40 text-sm"
              value={statusFilter}
              onChange={(event) => setStatusFilter(event.target.value)}
            >
              <option value="All">All statuses</option>
              <option value="Connected">Connected</option>
              <option value="Pending">Pending</option>
              <option value="Error">Error</option>
              <option value="Disconnected">Disconnected</option>
            </select>
          </div>

          {summary.errors > 0 && (
            <div className="flex items-center gap-3 rounded-xl border border-red-200 bg-red-50 px-4 py-3">
              <AlertTriangle className="h-4 w-4 shrink-0 text-red-500" />
              <p className="text-sm font-medium text-red-700">
                {summary.errors} connector{summary.errors > 1 ? "s" : ""} need attention. The backend is now showing this live instead of hiding it in a fake catalogue.
              </p>
            </div>
          )}

          {filtered.length === 0 ? (
            <EmptyState
              title="No connectors match your filters"
              subtitle="Try clearing the category or status filter to see the live connector inventory."
            />
          ) : (
            <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-3">
              {filtered.map((integration) => {
                const isConnected = integration.status === "Connected";
                const isDisabled = connectMut.isPending || disconnectMut.isPending || syncMut.isPending;
                return (
                  <div key={integration.id} className="panel flex flex-col gap-3 transition hover:border-slate-300">
                    <div className="flex items-start gap-3">
                      <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg border border-slate-200 bg-slate-100">
                        <span className="text-[10px] font-bold tracking-tight text-slate-600">{integration.logo.slice(0, 3)}</span>
                      </div>
                      <div className="min-w-0 flex-1">
                        <p className="truncate text-sm font-semibold leading-tight text-slate-900">{integration.name}</p>
                        <div className="mt-1 flex flex-wrap items-center gap-1.5">
                          <CategoryBadge category={integration.category} />
                        </div>
                      </div>
                    </div>

                    <p className="flex-1 text-xs leading-relaxed text-slate-500">{integration.description}</p>

                    <div className="flex flex-wrap gap-2">
                      {integration.connectedTo.map((item) => (
                        <ConnectorPill key={item} value={item} />
                      ))}
                    </div>

                    <div className="flex items-center justify-between gap-2 border-t border-slate-100 pt-2">
                      <StatusBadge status={integration.status} />
                      <span className="text-[10px] text-slate-400">Last sync: {integration.sync}</span>
                    </div>

                    <div className="flex gap-1.5">
                      {isConnected ? (
                        <>
                          <button
                            type="button"
                            disabled={isDisabled}
                            onClick={() => syncMut.mutate(integration.id)}
                            className="flex-1 rounded-lg border border-teal-300 bg-teal-50 px-2.5 py-1.5 text-xs font-medium text-teal-700 transition hover:bg-teal-100 disabled:opacity-50"
                          >
                            Sync now
                          </button>
                          <button
                            type="button"
                            disabled={isDisabled}
                            onClick={() => disconnectMut.mutate(integration.id)}
                            className="rounded-lg border border-red-200 bg-red-50 px-2.5 py-1.5 text-xs font-medium text-red-600 transition hover:bg-red-100 disabled:opacity-50"
                          >
                            Disconnect
                          </button>
                        </>
                      ) : (
                        <button
                          type="button"
                          disabled={isDisabled}
                          onClick={() => connectMut.mutate(integration.id)}
                          className="flex-1 rounded-lg border border-teal-300 bg-teal-50 px-2.5 py-1.5 text-xs font-medium text-teal-700 transition hover:bg-teal-100 disabled:opacity-50"
                        >
                          {integration.status === "Pending" ? "Authorize" : integration.status === "Error" ? "Reconnect" : "Connect"}
                        </button>
                      )}

                      <button
                        type="button"
                        title="Configure"
                        onClick={() => setConfigTarget(integration)}
                        className="rounded-lg border border-slate-200 bg-slate-50 p-1.5 text-slate-500 transition hover:bg-slate-100"
                      >
                        <Settings2 className="h-3.5 w-3.5" />
                      </button>
                    </div>
                  </div>
                );
              })}
            </div>
          )}
        </>
      )}

      {tab === "Activity Log" && (
        <div className="panel overflow-hidden">
          <div className="mb-4 flex items-center justify-between gap-3">
            <div>
              <p className="text-sm font-semibold text-slate-800">Recent live connector activity</p>
              <p className="text-xs text-slate-500">Connect, configure, disconnect, and sync operations are coming from the backend module.</p>
            </div>
            <button
              type="button"
              className="flex items-center gap-1.5 text-xs text-slate-500 transition hover:text-slate-700"
              onClick={() => void qc.invalidateQueries({ queryKey: ["integrations"] })}
            >
              <RefreshCw className="h-3.5 w-3.5" />
              Refresh
            </button>
          </div>

          {activity.length === 0 ? (
            <EmptyState title="No activity yet" subtitle="Connect or sync a connector to populate the live event feed." />
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full text-sm">
                <thead>
                  <tr className="border-b border-slate-200">
                    {["Connector", "Event", "Records", "Timestamp", "Status"].map((header) => (
                      <th key={header} className="pb-2 pr-4 text-left text-xs font-semibold uppercase tracking-wide text-slate-500">
                        {header}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {activity.map((row) => (
                    <tr key={row.id} className="transition hover:bg-slate-50">
                      <td className="whitespace-nowrap py-2.5 pr-4 text-xs font-medium text-slate-800">{row.integration}</td>
                      <td className="py-2.5 pr-4 text-xs text-slate-600">{row.event}</td>
                      <td className="py-2.5 pr-4 text-xs font-mono text-slate-500">{row.records > 0 ? row.records.toLocaleString() : "—"}</td>
                      <td className="whitespace-nowrap py-2.5 pr-4 text-xs text-slate-400">{row.ts}</td>
                      <td className="py-2.5">
                        <StatusBadge status={row.status} />
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      )}

      {configTarget && (
        <ConfigDrawer
          integration={configTarget}
          onClose={() => setConfigTarget(null)}
        />
      )}
    </div>
  );
}
