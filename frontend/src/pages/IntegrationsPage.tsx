import { useEffect, useMemo, useState, type FormEvent, type ReactNode } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  Activity,
  AlertTriangle,
  ArrowRightLeft,
  Building2,
  CheckCircle2,
  Cloud,
  Fuel,
  Layers,
  Link2,
  MapPinned,
  Pencil,
  PlugZap,
  Plug,
  Plus,
  RadioTower,
  RefreshCw,
  Search,
  Settings2,
  ShieldCheck,
  Trash2,
  Warehouse,
  X,
  Zap,
} from "lucide-react";
import {
  exportCsv,
  EmptyState,
  ErrorState,
  KpiCard,
  LoadingState,
  PageHeader,
  StatusBadge,
} from "@/components/ui";
import { useHasPermission } from "@/hooks/usePermission";
import {
  integrationsApi,
  type IntegrationCategory,
  type IntegrationRecord,
  type IntegrationsPayload,
  type IntegrationTestResult,
  type IntegrationWriteInput,
} from "@/services/integrationsApi";

type ConfigField = {
  key: string;
  label: string;
  type: "text" | "url" | "number";
  placeholder?: string;
  note?: string;
};

// Sensitive config values come back from the API redacted (never the real secret).
// An empty submit for a field that is already set must NOT overwrite the stored value.
const REDACTED_MARKER = "••••••••";

// Heuristic: which config keys hold secrets the API redacts (apiKey, token, secret, password...).
function isSecretField(key: string): boolean {
  return /(key|token|secret|password|apikey|auth|credential)/i.test(key);
}

function isRedactedValue(value: string | number | boolean | null | undefined): boolean {
  return typeof value === "string" && value.trim() === REDACTED_MARKER;
}

const CATEGORY_ORDER: IntegrationCategory[] = [
  "ERP & Accounting",
  "Telematics & ELD",
  "Fuel Cards",
  "Maps & Routing",
  "Messaging & Notifications",
  "WMS & Shipment Ops",
  "IoT & Sensors",
  "Compliance",
];

const CATEGORY_META: Record<IntegrationCategory, { icon: ReactNode; accent: string; dot: string }> = {
  "ERP & Accounting": { icon: <Building2 className="h-3.5 w-3.5" />, accent: "bg-blue-50 border-blue-200 text-blue-700", dot: "bg-blue-500" },
  "Telematics & ELD": { icon: <RadioTower className="h-3.5 w-3.5" />, accent: "bg-teal-50 border-teal-200 text-teal-700", dot: "bg-teal-500" },
  "Fuel Cards": { icon: <Fuel className="h-3.5 w-3.5" />, accent: "bg-amber-50 border-amber-200 text-amber-700", dot: "bg-amber-500" },
  "Maps & Routing": { icon: <MapPinned className="h-3.5 w-3.5" />, accent: "bg-green-50 border-green-200 text-green-700", dot: "bg-green-500" },
  "Messaging & Notifications": { icon: <PlugZap className="h-3.5 w-3.5" />, accent: "bg-violet-50 border-violet-200 text-violet-700", dot: "bg-violet-500" },
  "WMS & Shipment Ops": { icon: <Warehouse className="h-3.5 w-3.5" />, accent: "bg-indigo-50 border-indigo-200 text-indigo-700", dot: "bg-indigo-500" },
  "IoT & Sensors": { icon: <Cloud className="h-3.5 w-3.5" />, accent: "bg-sky-50 border-sky-200 text-sky-700", dot: "bg-sky-500" },
  Compliance: { icon: <ShieldCheck className="h-3.5 w-3.5" />, accent: "bg-emerald-50 border-emerald-200 text-emerald-700", dot: "bg-emerald-500" },
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
  return Object.fromEntries(
    fields.map((field) => {
      // Redacted secret → start the input empty (blank = keep the stored secret).
      if (isSecretField(field.key) && isRedactedValue(record.config[field.key])) {
        return [field.key, ""];
      }
      return [field.key, formatConfigValue(record.config[field.key])];
    }),
  ) as Record<string, string>;
}

function buildConfigPayload(record: IntegrationRecord, form: Record<string, string>) {
  const payload: Record<string, string | number | boolean | null> = {};
  for (const field of categoryFields(record.category)) {
    const raw = String(form[field.key] ?? "").trim();
    const secret = isSecretField(field.key);
    const storedRedacted = isRedactedValue(record.config[field.key]);

    // Secret already set (API returns "••••••••"): the user left the field blank or
    // untouched (still the redacted marker) → omit the key so the stored secret is kept.
    if (secret && storedRedacted && (raw === "" || raw === REDACTED_MARKER)) {
      continue;
    }
    // Never send the placeholder marker itself as a real value.
    if (raw === REDACTED_MARKER) {
      continue;
    }

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

/* ============================================================
   CONFIG DRAWER — connect / configure a connector's live fields
   ============================================================ */
function ConfigDrawer({
  integration,
  canManage,
  onClose,
}: {
  integration: IntegrationRecord;
  canManage: boolean;
  onClose: () => void;
}) {
  const qc = useQueryClient();
  const [saved, setSaved] = useState(false);
  const [form, setForm] = useState<Record<string, string>>(() => buildFormState(integration));
  // Local result of the real provider handshake, surfaced inside the drawer.
  const [testResult, setTestResult] = useState<{ success: boolean; message: string } | null>(null);

  useEffect(() => {
    setForm(buildFormState(integration));
    setSaved(false);
    setTestResult(null);
  }, [integration]);

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if (event.key === "Escape") onClose();
    };
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [onClose]);

  const saveMut = useMutation({
    mutationFn: (payload: Record<string, string | number | boolean | null>) =>
      integrationsApi.configure(integration.id, payload),
    onSuccess: async () => {
      setSaved(true);
      await qc.invalidateQueries({ queryKey: ["integrations"] });
      setTimeout(() => setSaved(false), 2200);
    },
  });

  const testMut = useMutation({
    mutationFn: () => integrationsApi.testConnection(integration.id),
    onSuccess: async (result: IntegrationTestResult) => {
      setTestResult({ success: result.success, message: result.message });
      await qc.invalidateQueries({ queryKey: ["integrations"] });
    },
    onError: (error) => {
      setTestResult({
        success: false,
        message: error instanceof Error ? error.message : "Connection test failed. Please try again.",
      });
      void qc.invalidateQueries({ queryKey: ["integrations"] });
    },
  });

  const fields = categoryFields(integration.category);
  const meta = CATEGORY_META[integration.category];

  return (
    <div className="fixed inset-0 z-50 flex justify-end bg-black/50 backdrop-blur-sm anim-fade-in">
      <div aria-hidden className="absolute inset-0" onClick={onClose} />
      <aside
        role="dialog"
        aria-modal="true"
        aria-label={`Configure ${integration.name}`}
        className="anim-slide-right relative flex h-full w-full max-w-xl flex-col gap-5 overflow-y-auto border-l border-slate-200 bg-linear-to-b from-white to-slate-50 p-6 shadow-2xl"
      >
        <div className="flex items-start justify-between gap-4">
          <div className="flex min-w-0 items-start gap-3">
            <div className="flex h-12 w-12 shrink-0 items-center justify-center rounded-2xl border border-slate-200 bg-white shadow-sm">
              <span className="text-xs font-black tracking-tight text-slate-700">{integration.logo.slice(0, 3).toUpperCase()}</span>
            </div>
            <div className="min-w-0">
              <p className="text-[11px] font-bold uppercase tracking-[0.22em] text-teal-600">Integration configuration</p>
              <h2 className="mt-1.5 truncate text-xl font-black tracking-tight text-slate-950">{integration.name}</h2>
              <p className="mt-1 text-sm text-slate-500">{categoryText(integration.category)}</p>
            </div>
          </div>
          <button type="button" aria-label="Close" className="icon-btn" onClick={onClose}>
            <X className="h-5 w-5" />
          </button>
        </div>

        <div className="clay-card p-4">
          <div className="flex items-center justify-between gap-3">
            <div className="flex items-center gap-2">
              <StatusBadge status={integration.status} />
              <CategoryBadge category={integration.category} />
            </div>
            <span className="text-xs text-slate-500">Last sync: {integration.sync}</span>
          </div>
          <p className="mt-3 text-sm leading-6 text-slate-600">{integration.description}</p>
          {integration.relatedSystems.length > 0 && (
            <div className="mt-4">
              <p className="text-[10px] font-bold uppercase tracking-[0.18em] text-slate-400">Related systems</p>
              <div className="mt-2 flex flex-wrap gap-2">
                {integration.relatedSystems.map((item) => (
                  <ConnectorPill key={item} value={item} />
                ))}
              </div>
            </div>
          )}
        </div>

        <form
          className="flex flex-col gap-4"
          onSubmit={(event) => {
            event.preventDefault();
            if (!canManage) return;
            void saveMut.mutateAsync(buildConfigPayload(integration, form));
          }}
        >
          <div className="grid gap-4 md:grid-cols-2">
            <div>
              <label className="field-label text-[12px] font-bold text-slate-700">Managed by</label>
              <input className="field mt-1 w-full bg-slate-50" value={integration.managedBy} readOnly />
            </div>
            <div>
              <label className="field-label text-[12px] font-bold text-slate-700">Tenant scope</label>
              <input
                className="field mt-1 w-full bg-slate-50"
                value={integration.scope === "platform" ? "Platform-wide" : `Tenant ${integration.tenantId}`}
                readOnly
              />
            </div>
          </div>

          <div className="space-y-4 rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
            <div className="flex items-center justify-between gap-3">
              <div className="flex items-center gap-2">
                <span className={`flex h-7 w-7 items-center justify-center rounded-lg border ${meta.accent}`}>{meta.icon}</span>
                <div>
                  <p className="text-sm font-semibold text-slate-800">Connection settings</p>
                  <p className="text-xs text-slate-500">Persisted in live backend state for this tenant.</p>
                </div>
              </div>
              <Settings2 className="h-4 w-4 text-slate-400" />
            </div>

            {fields.map((field) => {
              const secretSet = isSecretField(field.key) && isRedactedValue(integration.config[field.key]);
              return (
                <div key={field.key}>
                  <label className="field-label text-[12px] font-bold text-slate-700">{field.label}</label>
                  <input
                    type={field.type}
                    className="field mt-1 w-full"
                    value={form[field.key] ?? ""}
                    onChange={(event) => setForm((current) => ({ ...current, [field.key]: event.target.value }))}
                    placeholder={secretSet ? `${REDACTED_MARKER} (set — leave blank to keep)` : field.placeholder}
                    disabled={!canManage}
                  />
                  {secretSet ? (
                    <p className="mt-1 text-xs text-slate-400">Stored secret is set. Leave blank to keep it, or type a new value to replace it.</p>
                  ) : (
                    field.note && <p className="mt-1 text-xs text-slate-400">{field.note}</p>
                  )}
                </div>
              );
            })}
          </div>

          {integration.category === "Messaging & Notifications" && (
            <div className="rounded-2xl border border-violet-200 bg-violet-50/60 p-4">
              <p className="text-xs font-bold uppercase tracking-[0.18em] text-violet-600">Notification routing</p>
              <div className="mt-3 flex items-center gap-2 text-sm text-slate-600">
                <ArrowRightLeft className="h-4 w-4 text-violet-500" />
                Operational alerts and customer notifications are routed through this connector live.
              </div>
            </div>
          )}

          {testResult && (
            <div
              role="status"
              aria-live="polite"
              className={`flex items-start gap-2 rounded-xl border px-4 py-3 text-sm ${
                testResult.success
                  ? "border-emerald-200 bg-emerald-50 text-emerald-800"
                  : "border-red-200 bg-red-50 text-red-700"
              }`}
            >
              {testResult.success ? (
                <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0 text-emerald-600" />
              ) : (
                <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0 text-red-500" />
              )}
              <span className="font-medium">{testResult.message}</span>
            </div>
          )}

          {canManage ? (
            <div className="flex flex-wrap items-center gap-3 border-t border-slate-100 pt-3">
              <button type="submit" className="btn-primary flex-1" disabled={saveMut.isPending}>
                {saveMut.isPending ? "Saving..." : "Save configuration"}
              </button>
              <button
                type="button"
                className="btn-ghost"
                disabled={testMut.isPending}
                onClick={() => {
                  setTestResult(null);
                  testMut.mutate();
                }}
              >
                {testMut.isPending ? (
                  <RefreshCw className="h-3.5 w-3.5 animate-spin" />
                ) : (
                  <Zap className="h-3.5 w-3.5" />
                )}
                {testMut.isPending ? "Testing..." : "Test connection"}
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
          ) : (
            <div className="flex items-center gap-2 rounded-xl border border-slate-200 bg-slate-50 px-4 py-3 text-xs text-slate-500">
              <ShieldCheck className="h-4 w-4 shrink-0 text-slate-400" />
              You have read-only access. Ask an administrator to edit connector settings.
            </div>
          )}
        </form>
      </aside>
    </div>
  );
}

/* ============================================================
   CUSTOM CONNECTOR DIALOG — create / edit a tenant-owned connector
   ============================================================ */
type ConfigRow = { id: number; key: string; value: string };

let configRowSeq = 0;
function nextConfigRowId() {
  configRowSeq += 1;
  return configRowSeq;
}

function configToRows(config: IntegrationRecord["config"] | undefined): ConfigRow[] {
  if (!config) return [];
  return Object.entries(config)
    .filter(([key]) => key.trim().length > 0)
    .map(([key, value]) => {
      // Redacted secret → start the value blank so an untouched save keeps the stored secret.
      const redactedSecret = isSecretField(key) && isRedactedValue(value);
      return { id: nextConfigRowId(), key, value: redactedSecret ? "" : formatConfigValue(value) };
    });
}

function coerceConfigValue(raw: string): string | number | boolean | null {
  const trimmed = raw.trim();
  if (trimmed === "") return null;
  if (trimmed === "true") return true;
  if (trimmed === "false") return false;
  // Only treat as a number when it round-trips cleanly (avoids clobbering things
  // like account codes with leading zeros or ids like "wex-88231").
  if (/^-?\d+(\.\d+)?$/.test(trimmed)) {
    const parsed = Number(trimmed);
    if (Number.isFinite(parsed) && String(parsed) === trimmed) return parsed;
  }
  return trimmed;
}

function splitList(value: string): string[] {
  return value
    .split(",")
    .map((item) => item.trim())
    .filter((item) => item.length > 0);
}

function CustomConnectorDialog({
  target,
  onClose,
  onSaved,
}: {
  // null = create mode; an IntegrationRecord = edit mode.
  target: IntegrationRecord | null;
  onClose: () => void;
  onSaved: () => void;
}) {
  const qc = useQueryClient();
  const isEdit = target !== null;

  const [name, setName] = useState(target?.name ?? "");
  const [category, setCategory] = useState<IntegrationCategory>(target?.category ?? CATEGORY_ORDER[0]);
  const [description, setDescription] = useState(target?.description ?? "");
  const [logo, setLogo] = useState(target?.logo ?? "");
  const [managedBy, setManagedBy] = useState(target?.managedBy ?? "");
  const [relatedSystems, setRelatedSystems] = useState((target?.relatedSystems ?? []).join(", "));
  const [connectedTo, setConnectedTo] = useState((target?.connectedTo ?? []).join(", "));
  const [configRows, setConfigRows] = useState<ConfigRow[]>(() => configToRows(target?.config));

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if (event.key === "Escape") onClose();
    };
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [onClose]);

  const saveMut = useMutation({
    mutationFn: (payload: IntegrationWriteInput) =>
      isEdit ? integrationsApi.update(target!.id, payload) : integrationsApi.create(payload),
    onSuccess: async () => {
      await qc.invalidateQueries({ queryKey: ["integrations"] });
      onSaved();
    },
  });

  const trimmedName = name.trim();
  const canSubmit = trimmedName.length > 0 && !saveMut.isPending;

  function handleSubmit(event: FormEvent) {
    event.preventDefault();
    if (!canSubmit) return;

    const config: Record<string, string | number | boolean | null> = {};
    for (const row of configRows) {
      const key = row.key.trim();
      if (!key) continue;
      // A redacted secret left untouched must not be written back as the literal
      // "••••••••" placeholder — omit it so the stored secret is preserved.
      if (isSecretField(key) && (row.value.trim() === REDACTED_MARKER || row.value.trim() === "")) {
        continue;
      }
      config[key] = coerceConfigValue(row.value);
    }

    const payload: IntegrationWriteInput = {
      name: trimmedName,
      category,
      description: description.trim(),
      logo: logo.trim() || trimmedName.slice(0, 3).toUpperCase(),
      managedBy: managedBy.trim(),
      relatedSystems: splitList(relatedSystems),
      connectedTo: splitList(connectedTo),
      config,
    };

    void saveMut.mutate(payload);
  }

  const logoPreview = (logo.trim() || trimmedName.slice(0, 3)).slice(0, 3).toUpperCase();

  return (
    <div className="fixed inset-0 z-50 flex justify-end bg-black/50 backdrop-blur-sm anim-fade-in">
      <div aria-hidden className="absolute inset-0" onClick={onClose} />
      <aside
        role="dialog"
        aria-modal="true"
        aria-label={isEdit ? `Edit ${target?.name}` : "Add custom connector"}
        className="anim-slide-right relative flex h-full w-full max-w-xl flex-col gap-5 overflow-y-auto border-l border-slate-200 bg-linear-to-b from-white to-slate-50 p-6 shadow-2xl"
      >
        <div className="flex items-start justify-between gap-4">
          <div className="flex min-w-0 items-start gap-3">
            <div className="flex h-12 w-12 shrink-0 items-center justify-center rounded-2xl border border-slate-200 bg-white shadow-sm">
              <span className="text-xs font-black tracking-tight text-slate-700">{logoPreview || "NEW"}</span>
            </div>
            <div className="min-w-0">
              <p className="text-[11px] font-bold uppercase tracking-[0.22em] text-teal-600">
                {isEdit ? "Edit custom connector" : "Add custom connector"}
              </p>
              <h2 className="mt-1.5 truncate text-xl font-black tracking-tight text-slate-950">
                {isEdit ? target?.name : "New connector"}
              </h2>
              <p className="mt-1 text-sm text-slate-500">Tenant-owned integration, persisted live in the primary API.</p>
            </div>
          </div>
          <button type="button" aria-label="Close" className="icon-btn" onClick={onClose}>
            <X className="h-5 w-5" />
          </button>
        </div>

        <form className="flex flex-col gap-4" onSubmit={handleSubmit}>
          <div>
            <label className="field-label text-[12px] font-bold text-slate-700">
              Name <span className="text-red-500">*</span>
            </label>
            <input
              className="field mt-1 w-full"
              value={name}
              onChange={(event) => setName(event.target.value)}
              placeholder="e.g. Acme Freight API"
              autoFocus
              required
            />
          </div>

          <div className="grid gap-4 md:grid-cols-2">
            <div>
              <label className="field-label text-[12px] font-bold text-slate-700">
                Category <span className="text-red-500">*</span>
              </label>
              <select
                aria-label="Category"
                className="field mt-1 w-full"
                value={category}
                onChange={(event) => setCategory(event.target.value as IntegrationCategory)}
              >
                {CATEGORY_ORDER.map((cat) => (
                  <option key={cat} value={cat}>
                    {cat}
                  </option>
                ))}
              </select>
            </div>
            <div>
              <label className="field-label text-[12px] font-bold text-slate-700">Logo badge</label>
              <input
                className="field mt-1 w-full uppercase"
                value={logo}
                onChange={(event) => setLogo(event.target.value.slice(0, 4))}
                maxLength={4}
                placeholder={trimmedName ? trimmedName.slice(0, 3).toUpperCase() : "Defaults to first 3 letters"}
              />
            </div>
          </div>

          <div>
            <label className="field-label text-[12px] font-bold text-slate-700">Description</label>
            <textarea
              className="field mt-1 w-full"
              rows={3}
              value={description}
              onChange={(event) => setDescription(event.target.value)}
              placeholder="What does this connector do and what does it sync?"
            />
          </div>

          <div>
            <label className="field-label text-[12px] font-bold text-slate-700">Managed by</label>
            <input
              className="field mt-1 w-full"
              value={managedBy}
              onChange={(event) => setManagedBy(event.target.value)}
              placeholder="e.g. Platform Ops"
            />
          </div>

          <div className="grid gap-4 md:grid-cols-2">
            <div>
              <label className="field-label text-[12px] font-bold text-slate-700">Related systems</label>
              <input
                className="field mt-1 w-full"
                value={relatedSystems}
                onChange={(event) => setRelatedSystems(event.target.value)}
                placeholder="Comma-separated, e.g. NetSuite, SAP"
              />
              <p className="mt-1 text-xs text-slate-400">Separate multiple values with commas.</p>
            </div>
            <div>
              <label className="field-label text-[12px] font-bold text-slate-700">Connected to</label>
              <input
                className="field mt-1 w-full"
                value={connectedTo}
                onChange={(event) => setConnectedTo(event.target.value)}
                placeholder="Comma-separated, e.g. Dispatch, Billing"
              />
              <p className="mt-1 text-xs text-slate-400">Separate multiple values with commas.</p>
            </div>
          </div>

          <div className="space-y-3 rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
            <div className="flex items-center justify-between gap-3">
              <div className="flex items-center gap-2">
                <span className="flex h-7 w-7 items-center justify-center rounded-lg border border-slate-200 bg-slate-50 text-slate-500">
                  <Settings2 className="h-3.5 w-3.5" />
                </span>
                <div>
                  <p className="text-sm font-semibold text-slate-800">Connection settings</p>
                  <p className="text-xs text-slate-500">Free-form key/value config persisted live for this connector.</p>
                </div>
              </div>
              <button
                type="button"
                className="flex items-center gap-1 rounded-lg border border-slate-200 bg-slate-50 px-2 py-1 text-[11px] font-semibold text-slate-500 transition hover:bg-slate-100"
                onClick={() => setConfigRows((rows) => [...rows, { id: nextConfigRowId(), key: "", value: "" }])}
              >
                <Plus className="h-3 w-3" />
                Add row
              </button>
            </div>

            {configRows.length === 0 ? (
              <p className="rounded-lg border border-dashed border-slate-200 bg-slate-50/60 px-3 py-3 text-center text-xs text-slate-400">
                No config keys yet. Add a row to store credentials, URLs, or intervals.
              </p>
            ) : (
              <div className="space-y-2">
                {configRows.map((row) => (
                  <div key={row.id} className="flex items-center gap-2">
                    <input
                      className="field w-2/5 text-sm"
                      value={row.key}
                      onChange={(event) =>
                        setConfigRows((rows) => rows.map((r) => (r.id === row.id ? { ...r, key: event.target.value } : r)))
                      }
                      placeholder="key"
                      aria-label="Config key"
                    />
                    <input
                      className="field flex-1 text-sm"
                      value={row.value}
                      onChange={(event) =>
                        setConfigRows((rows) => rows.map((r) => (r.id === row.id ? { ...r, value: event.target.value } : r)))
                      }
                      placeholder={
                        isEdit && isSecretField(row.key) ? `${REDACTED_MARKER} (set — leave blank to keep)` : "value"
                      }
                      aria-label="Config value"
                    />
                    <button
                      type="button"
                      aria-label="Remove config row"
                      className="rounded-lg border border-red-200 bg-red-50 p-1.5 text-red-600 transition hover:bg-red-100"
                      onClick={() => setConfigRows((rows) => rows.filter((r) => r.id !== row.id))}
                    >
                      <Trash2 className="h-3.5 w-3.5" />
                    </button>
                  </div>
                ))}
              </div>
            )}
          </div>

          {saveMut.isError && (
            <div className="flex items-center gap-2 rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-xs font-medium text-red-700">
              <AlertTriangle className="h-4 w-4 shrink-0 text-red-500" />
              {saveMut.error instanceof Error
                ? saveMut.error.message
                : `Unable to ${isEdit ? "save changes to" : "create"} this connector. Please try again.`}
            </div>
          )}

          <div className="flex items-center gap-3 border-t border-slate-100 pt-3">
            <button type="submit" className="btn-primary flex-1" disabled={!canSubmit}>
              {saveMut.isPending ? "Saving..." : isEdit ? "Save changes" : "Create connector"}
            </button>
            <button type="button" className="btn-ghost" onClick={onClose}>
              Cancel
            </button>
          </div>
        </form>
      </aside>
    </div>
  );
}

/* ============================================================
   CONNECTOR CARD — claymorphic marketplace tile
   ============================================================ */
function ConnectorCard({
  integration,
  canManage,
  busy,
  testing,
  onConnect,
  onDisconnect,
  onSync,
  onTest,
  onConfigure,
  onEdit,
  onDelete,
}: {
  integration: IntegrationRecord;
  canManage: boolean;
  busy: boolean;
  testing: boolean;
  onConnect: () => void;
  onDisconnect: () => void;
  onSync: () => void;
  onTest: () => void;
  onConfigure: () => void;
  onEdit: () => void;
  onDelete: () => void;
}) {
  const isConnected = integration.status === "Connected";
  const isError = integration.status === "Error";
  const meta = CATEGORY_META[integration.category];
  const primaryLabel =
    integration.status === "Pending" ? "Authorize" : isError ? "Reconnect" : "Connect";

  return (
    <div className="clay-card card-hover flex flex-col gap-3 p-4">
      <span
        className={`pointer-events-none absolute inset-x-0 top-0 h-1 rounded-t-(--r-clay) ${
          isConnected ? "bg-emerald-400/70" : isError ? "bg-red-400/70" : integration.status === "Pending" ? "bg-amber-400/70" : "bg-slate-300/70"
        }`}
      />
      <div className="flex items-start gap-3">
        <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-2xl border border-slate-200 bg-white shadow-[inset_0_1px_2px_rgba(255,255,255,.9),0_1px_3px_rgba(15,23,42,.08)]">
          <span className="text-[11px] font-black tracking-tight text-slate-700">{integration.logo.slice(0, 3).toUpperCase()}</span>
        </div>
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-1.5">
            <p className="truncate text-sm font-bold leading-tight text-slate-900">{integration.name}</p>
            {integration.isCustom && (
              <span className="shrink-0 rounded-full border border-teal-200 bg-teal-50 px-1.5 py-px text-[9px] font-bold uppercase tracking-[0.12em] text-teal-600">
                Custom
              </span>
            )}
          </div>
          <div className="mt-1.5">
            <CategoryBadge category={integration.category} />
          </div>
        </div>
        <StatusBadge status={integration.status} />
      </div>

      <p className="line-clamp-2 flex-1 text-xs leading-relaxed text-slate-500">{integration.description}</p>

      {integration.connectedTo.length > 0 && (
        <div className="flex flex-wrap gap-1.5">
          {integration.connectedTo.slice(0, 4).map((item) => (
            <ConnectorPill key={item} value={item} />
          ))}
        </div>
      )}

      <div className="flex items-center justify-between gap-2 rounded-xl border border-slate-200/70 bg-slate-50 px-3 py-2 shadow-[inset_0_1px_3px_rgba(148,163,184,.18)]">
        <span className="text-[10px] font-semibold uppercase tracking-[0.12em] text-slate-400">Last sync</span>
        <span className="text-[11px] font-semibold text-slate-600">{integration.sync}</span>
      </div>

      {canManage ? (
        <div className="flex gap-1.5">
          {isConnected ? (
            <>
              <button
                type="button"
                disabled={busy}
                onClick={onSync}
                className="flex flex-1 items-center justify-center gap-1.5 rounded-lg border border-teal-300 bg-teal-50 px-2.5 py-1.5 text-xs font-semibold text-teal-700 transition hover:bg-teal-100 disabled:opacity-50"
              >
                <RefreshCw className="h-3.5 w-3.5" />
                Sync now
              </button>
              <button
                type="button"
                disabled={busy}
                onClick={onDisconnect}
                className="rounded-lg border border-red-200 bg-red-50 px-2.5 py-1.5 text-xs font-semibold text-red-600 transition hover:bg-red-100 disabled:opacity-50"
              >
                Disconnect
              </button>
            </>
          ) : (
            <button
              type="button"
              disabled={busy}
              onClick={onConnect}
              className="flex flex-1 items-center justify-center gap-1.5 rounded-lg border border-teal-300 bg-teal-50 px-2.5 py-1.5 text-xs font-semibold text-teal-700 transition hover:bg-teal-100 disabled:opacity-50"
            >
              <Plug className="h-3.5 w-3.5" />
              {primaryLabel}
            </button>
          )}

          <button
            type="button"
            title="Test connection"
            aria-label={`Test connection for ${integration.name}`}
            disabled={testing}
            onClick={onTest}
            className="rounded-lg border border-slate-200 bg-slate-50 p-1.5 text-slate-500 transition hover:bg-slate-100 disabled:opacity-50"
          >
            {testing ? <RefreshCw className="h-3.5 w-3.5 animate-spin" /> : <Zap className="h-3.5 w-3.5" />}
          </button>

          <button
            type="button"
            title="Configure"
            aria-label={`Configure ${integration.name}`}
            onClick={onConfigure}
            className="rounded-lg border border-slate-200 bg-slate-50 p-1.5 text-slate-500 transition hover:bg-slate-100"
          >
            <Settings2 className="h-3.5 w-3.5" />
          </button>

          {integration.isCustom && (
            <>
              <button
                type="button"
                title="Edit connector"
                aria-label={`Edit ${integration.name}`}
                onClick={onEdit}
                className="rounded-lg border border-slate-200 bg-slate-50 p-1.5 text-slate-500 transition hover:bg-slate-100"
              >
                <Pencil className="h-3.5 w-3.5" />
              </button>
              <button
                type="button"
                title="Delete connector"
                aria-label={`Delete ${integration.name}`}
                disabled={busy}
                onClick={onDelete}
                className="rounded-lg border border-red-200 bg-red-50 p-1.5 text-red-600 transition hover:bg-red-100 disabled:opacity-50"
              >
                <Trash2 className="h-3.5 w-3.5" />
              </button>
            </>
          )}
        </div>
      ) : (
        <button
          type="button"
          onClick={onConfigure}
          className="flex items-center justify-center gap-1.5 rounded-lg border border-slate-200 bg-slate-50 px-2.5 py-1.5 text-xs font-semibold text-slate-500 transition hover:bg-slate-100"
        >
          <Settings2 className="h-3.5 w-3.5" />
          View details
        </button>
      )}
    </div>
  );
}

/* ============================================================
   ACTIVITY FEED — recent live connector events
   ============================================================ */
function activityTone(status: string) {
  if (/error|fail/i.test(status)) return { dot: "bg-red-500", ring: "ring-red-100" };
  if (/pending|progress/i.test(status)) return { dot: "bg-amber-500", ring: "ring-amber-100" };
  return { dot: "bg-emerald-500", ring: "ring-emerald-100" };
}

function ActivityFeed({ activity, onRefresh }: { activity: IntegrationsPayload["activity"]; onRefresh: () => void }) {
  return (
    <div className="clay-card flex h-full flex-col overflow-hidden p-4">
      <div className="mb-3 flex items-center justify-between gap-3">
        <div className="flex items-center gap-2">
          <Activity className="h-4 w-4 text-teal-500" />
          <div>
            <p className="text-sm font-bold text-slate-800">Activity feed</p>
            <p className="text-[11px] text-slate-500">Live connect · configure · sync · disconnect</p>
          </div>
        </div>
        <button
          type="button"
          className="flex items-center gap-1.5 rounded-lg border border-slate-200 bg-white px-2 py-1 text-[11px] font-semibold text-slate-500 transition hover:bg-slate-50"
          onClick={onRefresh}
        >
          <RefreshCw className="h-3 w-3" />
          Refresh
        </button>
      </div>

      {activity.length === 0 ? (
        <div className="flex flex-1 flex-col items-center justify-center rounded-xl border border-dashed border-slate-200 bg-slate-50/60 px-4 py-10 text-center">
          <Zap className="mb-2 h-5 w-5 text-slate-300" />
          <p className="text-sm font-semibold text-slate-600">No activity yet</p>
          <p className="mt-1 max-w-56 text-xs text-slate-400">Connect or sync a connector to populate the live event feed.</p>
        </div>
      ) : (
        <div className="-mr-1 flex-1 space-y-0 overflow-y-auto pr-1">
          {activity.map((row, index) => {
            const tone = activityTone(row.status);
            const isLast = index === activity.length - 1;
            return (
              <div key={row.id} className="flex gap-3">
                <div className="flex flex-col items-center">
                  <span className={`mt-1 h-2.5 w-2.5 shrink-0 rounded-full ${tone.dot} ring-4 ${tone.ring}`} />
                  {!isLast && <span className="mt-1 w-px flex-1 bg-slate-200" />}
                </div>
                <div className="min-w-0 flex-1 pb-4">
                  <div className="flex items-start justify-between gap-2">
                    <p className="truncate text-sm font-semibold text-slate-800">{row.integration}</p>
                    <span className="shrink-0 whitespace-nowrap text-[10px] text-slate-400">{row.ts}</span>
                  </div>
                  <p className="mt-0.5 text-xs text-slate-500">{row.event}</p>
                  <div className="mt-1.5 flex items-center gap-2">
                    <StatusBadge status={row.status} />
                    {row.records > 0 && (
                      <span className="text-[10px] font-mono text-slate-400">{row.records.toLocaleString()} records</span>
                    )}
                  </div>
                </div>
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
}

/* ============================================================
   PAGE
   ============================================================ */
export function IntegrationsPage() {
  const hasPermission = useHasPermission();
  const canManage = hasPermission("telematics:providers:manage");
  const qc = useQueryClient();

  const [categoryFilter, setCategoryFilter] = useState<string>("All");
  const [statusFilter, setStatusFilter] = useState<string>("All");
  const [search, setSearch] = useState("");
  const [configTarget, setConfigTarget] = useState<IntegrationRecord | null>(null);
  // Live "Test Connection" result banner — surfaces the REAL provider handshake result.
  const [testResult, setTestResult] = useState<{ id: number; success: boolean; message: string } | null>(null);
  // Custom-connector create/edit dialog. { mode: "create" } opens an empty form;
  // { mode: "edit", record } prefills from an existing custom connector.
  const [connectorDialog, setConnectorDialog] = useState<
    { mode: "create" } | { mode: "edit"; record: IntegrationRecord } | null
  >(null);

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

  // Category chips ordered canonically, then any unexpected categories.
  const categories = useMemo(() => {
    const present = new Set(integrations.map((item) => item.category));
    const ordered = CATEGORY_ORDER.filter((cat) => present.has(cat));
    const extra = Array.from(present).filter((cat) => !CATEGORY_ORDER.includes(cat as IntegrationCategory));
    return ["All", ...ordered, ...extra];
  }, [integrations]);

  const categoryCounts = useMemo(() => {
    const map = new Map<string, number>();
    for (const item of integrations) map.set(item.category, (map.get(item.category) ?? 0) + 1);
    return map;
  }, [integrations]);

  const filtered = useMemo(
    () =>
      integrations.filter((integration) => {
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
          ]
            .join(" ")
            .toLowerCase();
          if (!haystack.includes(search.toLowerCase())) return false;
        }
        return true;
      }),
    [integrations, categoryFilter, statusFilter, search],
  );

  // Group the filtered set by category in canonical order for section headers.
  const grouped = useMemo(() => {
    const map = new Map<IntegrationCategory, IntegrationRecord[]>();
    for (const item of filtered) {
      const list = map.get(item.category) ?? [];
      list.push(item);
      map.set(item.category, list);
    }
    const order = [
      ...CATEGORY_ORDER.filter((cat) => map.has(cat)),
      ...Array.from(map.keys()).filter((cat) => !CATEGORY_ORDER.includes(cat)),
    ];
    return order.map((cat) => [cat, map.get(cat)!] as const);
  }, [filtered]);

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

  // Real provider handshake. Surface the TRUE result (never fabricate success) and
  // refresh the list so the card's status reflects the server-side Connected/Error update.
  const testMut = useMutation({
    mutationFn: (id: number) => integrationsApi.testConnection(id),
    onSuccess: async (result: IntegrationTestResult, id: number) => {
      setTestResult({ id, success: result.success, message: result.message });
      await qc.invalidateQueries({ queryKey: ["integrations"] });
    },
    onError: (error, id: number) => {
      setTestResult({
        id,
        success: false,
        message: error instanceof Error ? error.message : "Connection test failed. Please try again.",
      });
      void qc.invalidateQueries({ queryKey: ["integrations"] });
    },
  });

  function handleTest(id: number) {
    if (!canManage) return;
    setTestResult(null);
    testMut.mutate(id);
  }

  const deleteMut = useMutation({
    mutationFn: (id: number) => integrationsApi.remove(id),
    onSuccess: async () => {
      await qc.invalidateQueries({ queryKey: ["integrations"] });
    },
    onError: (error) => {
      window.alert(
        error instanceof Error ? error.message : "Unable to delete this connector. Please try again.",
      );
    },
  });

  function handleDelete(integration: IntegrationRecord) {
    if (!canManage) return;
    const ok = window.confirm(
      `Delete the custom connector "${integration.name}"? This removes it from your tenant's marketplace and cannot be undone.`,
    );
    if (ok) deleteMut.mutate(integration.id);
  }

  const busy =
    connectMut.isPending || disconnectMut.isPending || syncMut.isPending || deleteMut.isPending;

  if (q.isLoading) return <LoadingState />;
  if (q.isError) {
    return (
      <div className="py-6">
        <ErrorState
          message="Unable to load live integrations from the backend."
          onRetry={() => void q.refetch()}
        />
      </div>
    );
  }

  return (
    <div className="flex h-full flex-col gap-6 overflow-y-auto py-6">
      <PageHeader
        eyebrow="Connector marketplace"
        title="Integrations"
        description="Samsara-grade connector marketplace for ERP, accounting, telematics, fuel cards, routing, messaging, WMS, IoT, and compliance — every card, status, and event streamed live from the primary API and scoped to your tenant."
        actions={
          <>
            <button type="button" className="btn-ghost text-sm" onClick={() => exportCsv("integrations", integrations)}>
              Export CSV
            </button>
            <button
              type="button"
              className="btn-ghost text-sm"
              onClick={() => void qc.invalidateQueries({ queryKey: ["integrations"] })}
            >
              <RefreshCw className="h-3.5 w-3.5" />
              Refresh
            </button>
            {canManage && (
              <button type="button" className="btn-primary text-sm" onClick={() => setConnectorDialog({ mode: "create" })}>
                <Plus className="h-3.5 w-3.5" />
                Add Custom Connector
              </button>
            )}
          </>
        }
      />

      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-5">
        <KpiCard label="Connectors" value={summary.total} icon={<Layers className="h-5 w-5" />} delta={`${summary.categories} categories`} />
        <KpiCard label="Connected" value={summary.connected} status="Live" icon={<Link2 className="h-5 w-5" />} />
        <KpiCard label="Pending" value={summary.pending} status={summary.pending > 0 ? "Pending" : undefined} icon={<PlugZap className="h-5 w-5" />} />
        <KpiCard label="Errors" value={summary.errors} status={summary.errors > 0 ? "Critical" : undefined} icon={<AlertTriangle className="h-5 w-5" />} />
        <KpiCard label="Categories" value={summary.categories} icon={<Warehouse className="h-5 w-5" />} />
      </div>

      {summary.errors > 0 && (
        <div className="flex items-center gap-3 rounded-xl border border-red-200 bg-red-50 px-4 py-3">
          <AlertTriangle className="h-4 w-4 shrink-0 text-red-500" />
          <p className="text-sm font-medium text-red-700">
            {summary.errors} connector{summary.errors > 1 ? "s" : ""} need attention. Filter by <b>Error</b> status to triage and reconnect.
          </p>
        </div>
      )}

      {testResult && (
        <div
          role="status"
          aria-live="polite"
          className={`flex items-start gap-3 rounded-xl border px-4 py-3 ${
            testResult.success
              ? "border-emerald-200 bg-emerald-50 text-emerald-800"
              : "border-red-200 bg-red-50 text-red-700"
          }`}
        >
          {testResult.success ? (
            <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0 text-emerald-600" />
          ) : (
            <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0 text-red-500" />
          )}
          <div className="min-w-0 flex-1">
            <p className="text-[11px] font-bold uppercase tracking-[0.14em]">
              {(() => {
                const name = integrations.find((item) => item.id === testResult.id)?.name;
                const label = testResult.success ? "Connection successful" : "Connection failed";
                return name ? `${label} · ${name}` : label;
              })()}
            </p>
            <p className="mt-0.5 text-sm font-medium">{testResult.message}</p>
          </div>
          <button
            type="button"
            aria-label="Dismiss connection test result"
            className={`shrink-0 rounded-lg p-1 transition ${
              testResult.success ? "hover:bg-emerald-100" : "hover:bg-red-100"
            }`}
            onClick={() => setTestResult(null)}
          >
            <X className="h-4 w-4" />
          </button>
        </div>
      )}

      <div className="panel flex flex-col gap-3 p-4">
        <div className="flex flex-wrap items-center gap-3">
          <div className="relative">
            <Search className="absolute left-2.5 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-slate-400" />
            <input
              className="field w-64 pl-8 text-sm"
              value={search}
              onChange={(event) => setSearch(event.target.value)}
              placeholder="Search connectors, systems, owners..."
              aria-label="Search connectors"
            />
          </div>
          <select
            aria-label="Status filter"
            className="field w-40 text-sm"
            value={statusFilter}
            onChange={(event) => setStatusFilter(event.target.value)}
          >
            <option value="All">All statuses</option>
            <option value="Connected">Connected</option>
            <option value="Pending">Pending</option>
            <option value="Error">Error</option>
            <option value="Disconnected">Disconnected</option>
          </select>
          <span className="ml-auto rounded-full border border-slate-200 bg-slate-50 px-3 py-1 text-[11px] font-bold text-slate-500">
            {filtered.length === integrations.length ? `${integrations.length} connectors` : `${filtered.length} of ${integrations.length}`}
          </span>
        </div>

        <div className="flex flex-wrap gap-1.5 border-t border-slate-100 pt-3">
          {categories.map((item) => {
            const active = categoryFilter === item;
            const count = item === "All" ? integrations.length : categoryCounts.get(item) ?? 0;
            const meta = item === "All" ? null : CATEGORY_META[item as IntegrationCategory];
            return (
              <button
                key={item}
                type="button"
                aria-pressed={active}
                onClick={() => setCategoryFilter(item)}
                className={active ? "filter-chip filter-chip-active" : "filter-chip"}
              >
                {meta ? <span className={`h-1.5 w-1.5 rounded-full ${meta.dot}`} /> : null}
                {item}
                <span className="ml-1 rounded-full bg-black/5 px-1.5 text-[10px] font-bold tabular-nums">{count}</span>
              </button>
            );
          })}
        </div>
      </div>

      <div className="grid grid-cols-1 gap-6 xl:grid-cols-[minmax(0,1fr)_340px]">
        <div className="flex min-w-0 flex-col gap-6">
          {filtered.length === 0 ? (
            <EmptyState
              title="No connectors match your filters"
              subtitle="Clear the category, status, or search filter to see the live connector inventory."
              action={
                <button
                  type="button"
                  className="btn-ghost text-sm"
                  onClick={() => {
                    setCategoryFilter("All");
                    setStatusFilter("All");
                    setSearch("");
                  }}
                >
                  Reset filters
                </button>
              }
            />
          ) : (
            grouped.map(([category, records]) => {
              const meta = CATEGORY_META[category];
              return (
                <section key={category} className="flex flex-col gap-3">
                  <div className="flex items-center gap-2.5">
                    <span className={`flex h-8 w-8 items-center justify-center rounded-xl border ${meta.accent}`}>{meta.icon}</span>
                    <div className="min-w-0">
                      <h2 className="text-sm font-black tracking-tight text-slate-900">{category}</h2>
                      <p className="text-[11px] text-slate-500">{categoryText(category)}</p>
                    </div>
                    <span className="ml-auto rounded-full border border-slate-200 bg-slate-50 px-2.5 py-0.5 text-[11px] font-bold text-slate-500">
                      {records.length}
                    </span>
                  </div>
                  <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 2xl:grid-cols-3">
                    {records.map((integration) => (
                      <ConnectorCard
                        key={integration.id}
                        integration={integration}
                        canManage={canManage}
                        busy={busy}
                        testing={testMut.isPending && testMut.variables === integration.id}
                        onConnect={() => connectMut.mutate(integration.id)}
                        onDisconnect={() => disconnectMut.mutate(integration.id)}
                        onSync={() => syncMut.mutate(integration.id)}
                        onTest={() => handleTest(integration.id)}
                        onConfigure={() => setConfigTarget(integration)}
                        onEdit={() => setConnectorDialog({ mode: "edit", record: integration })}
                        onDelete={() => handleDelete(integration)}
                      />
                    ))}
                  </div>
                </section>
              );
            })
          )}
        </div>

        <aside className="xl:sticky xl:top-6 xl:self-start">
          <div className="xl:max-h-[calc(100vh-6rem)] xl:overflow-hidden">
            <ActivityFeed activity={activity} onRefresh={() => void qc.invalidateQueries({ queryKey: ["integrations"] })} />
          </div>
        </aside>
      </div>

      {configTarget && (
        <ConfigDrawer integration={configTarget} canManage={canManage} onClose={() => setConfigTarget(null)} />
      )}

      {connectorDialog && (
        <CustomConnectorDialog
          key={connectorDialog.mode === "edit" ? `edit-${connectorDialog.record.id}` : "create"}
          target={connectorDialog.mode === "edit" ? connectorDialog.record : null}
          onClose={() => setConnectorDialog(null)}
          onSaved={() => setConnectorDialog(null)}
        />
      )}
    </div>
  );
}
