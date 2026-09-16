import { Check, Copy, ShieldCheck } from "lucide-react";
import type { AnyRecord } from "@/types";

export type AdminTab = "dashboard" | "users" | "roles" | "permissions" | "access" | "settings" | "audit";

export type UserFormState = {
  id?: number;
  fullName: string;
  email: string;
  companyId: number;
  roleId: string;
  roleName: string;
  /** DEF-027: customer-scope binding for portal roles ("" = no binding). */
  customerId: string;
  /** Empty means tenant-wide; otherwise the user's enforced branch ownership scope. */
  branchId: string;
  status: string;
  password: string;
};

export type RoleFormState = {
  name: string;
  permissions: string[];
};

export type ActivationLink = { link: string; expiresAt?: string };

export type UserSortKey = "fullName" | "companyName" | "roleName" | "status" | "lastLoginAt";

export const USER_PAGE_SIZE = 25;

export const USER_COLUMNS: Array<{ label: string; sortKey?: UserSortKey }> = [
  { label: "User", sortKey: "fullName" },
  { label: "Company", sortKey: "companyName" },
  { label: "Role", sortKey: "roleName" },
  { label: "Security" },
  { label: "Status", sortKey: "status" },
  { label: "Last Login", sortKey: "lastLoginAt" },
  { label: "Actions" },
];

export const USER_SORT_ACCESSORS: Record<UserSortKey, (user: AnyRecord) => string | number> = {
  fullName: (user) => String(user.fullName ?? user.full_name ?? "").toLowerCase(),
  companyName: (user) => String(user.companyName ?? user.company_name ?? "").toLowerCase(),
  roleName: (user) => String(user.roleName ?? user.role_name ?? "").toLowerCase(),
  status: (user) => String(user.status ?? "").toLowerCase(),
  lastLoginAt: (user) => {
    const raw = user.lastLoginAt ?? user.last_login_at;
    const time = raw ? new Date(String(raw)).getTime() : 0;
    return Number.isFinite(time) ? time : 0;
  },
};

export const TAB_OPTIONS: Array<{ key: AdminTab; label: string }> = [
  { key: "dashboard", label: "Dashboard" },
  { key: "users", label: "Users" },
  { key: "roles", label: "Roles" },
  { key: "permissions", label: "Permissions" },
  { key: "access", label: "Access Reviews" },
  { key: "settings", label: "Settings" },
  { key: "audit", label: "Audit Logs" },
];

function csvValue(value: unknown) {
  const text = String(value ?? "");
  if (/[",\n]/.test(text)) return `"${text.replace(/"/g, '""')}"`;
  return text;
}

export function downloadCsv(filename: string, rows: AnyRecord[], headers: string[]) {
  const lines = [
    headers.join(","),
    ...rows.map((row) => headers.map((key) => csvValue(row[key])).join(",")),
  ];
  const blob = new Blob([lines.join("\n")], { type: "text/csv;charset=utf-8;" });
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = filename;
  anchor.click();
  URL.revokeObjectURL(url);
}

export function permissionsByGroup(permissions: string[]) {
  const groups: Array<{ title: string; prefix: string }> = [
    { title: "Dashboard", prefix: "dashboard:" },
    { title: "Fleet", prefix: "vehicles:" },
    { title: "Drivers", prefix: "drivers:" },
    { title: "Shipments / Jobs", prefix: "shipments:" },
    { title: "Dispatch", prefix: "dispatch:" },
    { title: "Customers", prefix: "customers:" },
    { title: "Safety", prefix: "safety:" },
    { title: "Maintenance", prefix: "maintenance:" },
    { title: "Compliance", prefix: "compliance:" },
    { title: "Alerts", prefix: "alerts:" },
    { title: "Reports", prefix: "reports:" },
    { title: "Admin", prefix: "users:" },
    { title: "Roles", prefix: "roles:" },
    { title: "Settings", prefix: "settings:" },
    { title: "Audit", prefix: "audit:" },
  ];
  const grouped = groups
    .map((group) => ({
      ...group,
      permissions: permissions.filter((permission) => permission.startsWith(group.prefix)),
    }))
    .filter((group) => group.permissions.length > 0);
  // Catch-all so permissions with unknown prefixes (access_review:, telematics:, …)
  // stay visible and grantable instead of silently disappearing from the editor.
  const known = new Set(grouped.flatMap((g) => g.permissions));
  const other = permissions.filter((p) => !known.has(p));
  if (other.length > 0) grouped.push({ title: "Other", prefix: "", permissions: other });
  return grouped;
}

export function permissionList(value: unknown): string[] {
  if (Array.isArray(value)) return value.map(String);
  if (typeof value === "string") {
    try {
      const parsed = JSON.parse(value);
      return Array.isArray(parsed) ? parsed.map(String) : [value];
    } catch {
      return value.split(",").map((item) => item.trim()).filter(Boolean);
    }
  }
  return [];
}

function initials(name: string): string {
  return name
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0]?.toUpperCase() ?? "")
    .join("") || "?";
}

/** Deterministic avatar hue per name so the roster is scannable at a glance. */
function avatarHue(name: string): number {
  let h = 0;
  for (let i = 0; i < name.length; i++) h = (h * 31 + name.charCodeAt(i)) % 360;
  return h;
}

export function Avatar({ name }: { name: string }) {
  const hue = avatarHue(name);
  return (
    <span
      aria-hidden="true"
      className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full text-[11px] font-bold text-white shadow-[inset_0_1px_0_rgba(255,255,255,.4),0_2px_5px_rgba(15,23,42,.25)]"
      style={{ background: `linear-gradient(145deg, hsl(${hue} 55% 52%), hsl(${hue} 60% 40%))` }}
    >
      {initials(name)}
    </span>
  );
}

/** Pulls the human-readable message out of an Axios/ApiResponse error. */
export function extractApiError(err: unknown, fallback: string): string {
  const data = (err as { response?: { data?: { message?: string; errors?: string[] } } })?.response?.data;
  return data?.errors?.[0] ?? data?.message ?? fallback;
}

export function MfaBadge({ status }: { status: unknown }) {
  const enabled = String(status ?? "").toLowerCase() === "enabled";
  return (
    <span className={`iam-chip ${enabled ? "!text-emerald-700" : "!text-slate-400"}`} title={enabled ? "Multi-factor authentication enrolled" : "MFA not enrolled"}>
      <ShieldCheck className={`h-3 w-3 shrink-0 ${enabled ? "text-emerald-600" : "text-slate-300"}`} />
      <span>{enabled ? "MFA" : "No MFA"}</span>
    </span>
  );
}

/** One-time activation link panel (same pattern as the API-key panel on SettingsPage). */
export function ActivationLinkPanel({
  result, copied, onCopy, onDismiss,
}: {
  result: ActivationLink; copied: boolean; onCopy: () => void; onDismiss: () => void;
}) {
  return (
    <div className="rounded-lg border border-amber-200 bg-amber-50 p-3 space-y-2">
      <p className="text-xs font-semibold text-amber-800">One-time activation link — copy it now and share it securely; it will not be shown again.</p>
      <div className="flex items-center gap-2 min-w-0">
        <code className="min-w-0 flex-1 whitespace-nowrap rounded-lg border border-amber-200 bg-white px-3 py-2 text-xs font-mono text-slate-700 overflow-x-auto select-all">
          {result.link}
        </code>
        <button type="button" className="btn-secondary text-xs shrink-0" onClick={onCopy}>
          {copied ? <><Check className="h-3.5 w-3.5" /> Copied</> : <><Copy className="h-3.5 w-3.5" /> Copy</>}
        </button>
      </div>
      {result.expiresAt && (
        <p className="text-xs text-amber-700">Link expires {new Date(result.expiresAt).toLocaleDateString()}</p>
      )}
      <button type="button" className="text-xs font-semibold text-amber-800 underline" onClick={onDismiss}>
        Dismiss
      </button>
    </div>
  );
}
