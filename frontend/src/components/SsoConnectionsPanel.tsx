import { useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { KeyRound, Plus, ShieldCheck, Trash2, X } from "lucide-react";
import { settingsApi } from "@/services/settingsApi";
import type { AnyRecord } from "@/types";

type Draft = {
  id?: number;
  providerType: "oidc" | "saml";
  displayName: string;
  issuerOrEntityId: string;
  clientId: string;
  clientSecretRef: string;
  metadataUrl: string;
  domainHints: string; // comma-separated in the form
  enabled: boolean;
};

const EMPTY: Draft = {
  providerType: "oidc",
  displayName: "",
  issuerOrEntityId: "",
  clientId: "",
  clientSecretRef: "",
  metadataUrl: "",
  domainHints: "",
  enabled: true,
};

function parseDomains(csv: string): string[] {
  return csv.split(",").map((d) => d.trim().replace(/^@/, "").toLowerCase()).filter(Boolean);
}

function domainsToText(hints: unknown): string {
  if (Array.isArray(hints)) return hints.join(", ");
  if (typeof hints === "string") {
    try { const a = JSON.parse(hints); return Array.isArray(a) ? a.join(", ") : hints; } catch { return hints; }
  }
  return "";
}

/**
 * Tenant self-serve management of enterprise SSO connections, backed by the real
 * /api/security/sso-connections endpoints. The client secret is never entered or
 * shown here — the connection stores only a reference (env:VAR_NAME) that the
 * login flow resolves server-side.
 */
export function SsoConnectionsPanel() {
  const qc = useQueryClient();
  const listQ = useQuery({ queryKey: ["sso-connections"], queryFn: settingsApi.ssoConnectionsList });
  const [draft, setDraft] = useState<Draft | null>(null);
  const [formError, setFormError] = useState("");

  const invalidate = () => qc.invalidateQueries({ queryKey: ["sso-connections"] });

  const saveMut = useMutation({
    mutationFn: async (d: Draft) => {
      const body: Record<string, unknown> = {
        providerType: d.providerType,
        displayName: d.displayName.trim(),
        issuerOrEntityId: d.issuerOrEntityId.trim(),
        clientId: d.clientId.trim(),
        metadataUrl: d.metadataUrl.trim() || null,
        domainHintsJson: JSON.stringify(parseDomains(d.domainHints)),
        enabled: d.enabled,
      };
      // Only send the secret reference when the admin provided/changed one.
      if (d.clientSecretRef.trim()) body.clientSecretRef = d.clientSecretRef.trim();
      return d.id ? settingsApi.ssoConnectionUpdate(d.id, body) : settingsApi.ssoConnectionCreate(body);
    },
    onSuccess: () => { setDraft(null); setFormError(""); invalidate(); },
    onError: () => setFormError("Could not save the connection. Check the fields and try again."),
  });

  const disableMut = useMutation({
    mutationFn: (id: number) => settingsApi.ssoConnectionDisable(id),
    onSuccess: invalidate,
  });

  const rows = (listQ.data ?? []) as AnyRecord[];

  const canSave = useMemo(() => {
    if (!draft) return false;
    return Boolean(draft.displayName.trim() && draft.issuerOrEntityId.trim() && draft.clientId.trim() && parseDomains(draft.domainHints).length);
  }, [draft]);

  function startEdit(r: AnyRecord) {
    setFormError("");
    setDraft({
      id: Number(r.id),
      providerType: (r.providerType as "oidc" | "saml") ?? "oidc",
      displayName: String(r.displayName ?? ""),
      issuerOrEntityId: String(r.issuerOrEntityId ?? ""),
      clientId: String(r.clientId ?? ""),
      clientSecretRef: "", // never pre-filled; blank keeps the existing reference
      metadataUrl: String(r.metadataUrl ?? ""),
      domainHints: domainsToText(r.domainHints),
      enabled: Boolean(r.enabled),
    });
  }

  return (
    <div className="iam iam-card p-5">
      <div className="flex items-start justify-between gap-4">
        <div className="flex items-center gap-2.5">
          <ShieldCheck className="h-5 w-5 text-teal-600" />
          <div>
            <h3 className="text-sm font-bold text-slate-900">Single sign-on (SSO / SAML)</h3>
            <p className="text-xs text-slate-500">Map your identity provider so staff sign in with their work email domain.</p>
          </div>
        </div>
        {!draft && (
          <button onClick={() => { setFormError(""); setDraft({ ...EMPTY }); }}
            className="inline-flex items-center gap-1.5 rounded-lg bg-teal-600 px-3 py-2 text-xs font-semibold text-white hover:bg-teal-500">
            <Plus className="h-3.5 w-3.5" /> Add connection
          </button>
        )}
      </div>

      {/* List */}
      <div className="mt-4 space-y-2">
        {listQ.isLoading && <p className="text-xs text-slate-400">Loading connections…</p>}
        {listQ.isError && (
          <p role="alert" className="rounded-lg border border-rose-200 bg-rose-50 px-4 py-3 text-xs text-rose-700">
            Could not load SSO connections. Refresh to retry — existing connections are unaffected.
          </p>
        )}
        {!listQ.isLoading && !listQ.isError && rows.length === 0 && !draft && (
          <p className="rounded-lg border border-dashed border-slate-200 px-4 py-6 text-center text-xs text-slate-400">
            No SSO connections yet. Add one to enable identity-provider sign-in for your organization.
          </p>
        )}
        {rows.map((r) => (
          <div key={String(r.id)} className="flex items-center justify-between gap-3 rounded-xl border border-slate-200 px-4 py-3">
            <div className="min-w-0">
              <div className="flex items-center gap-2">
                <span className="truncate text-sm font-semibold text-slate-900">{String(r.displayName)}</span>
                <span className="rounded-full bg-slate-100 px-2 py-0.5 text-[10px] font-bold uppercase tracking-wide text-slate-500">{String(r.providerType)}</span>
                {String(r.providerType) === "saml" && (
                  <span className="rounded-full bg-amber-50 px-2 py-0.5 text-[10px] font-bold text-amber-700" title="SAML sign-in is not yet active; users on these domains fall back to password login.">sign-in inactive</span>
                )}
                <span className={`rounded-full px-2 py-0.5 text-[10px] font-bold ${r.enabled ? "bg-emerald-50 text-emerald-700" : "bg-slate-100 text-slate-400"}`}>
                  {r.enabled ? "Enabled" : "Disabled"}
                </span>
                {r.hasSecretRef ? (
                  <span className="inline-flex items-center gap-1 text-[10px] text-slate-400"><KeyRound className="h-3 w-3" /> secret set</span>
                ) : (
                  <span className="text-[10px] font-semibold text-amber-600">secret missing</span>
                )}
              </div>
              <p className="mt-0.5 truncate text-[11px] text-slate-500">{domainsToText(r.domainHints) || "no domains"} · {String(r.issuerOrEntityId)}</p>
            </div>
            <div className="flex shrink-0 items-center gap-2">
              <button onClick={() => startEdit(r)} className="rounded-lg border border-slate-200 px-2.5 py-1.5 text-xs font-semibold text-slate-600 hover:bg-slate-50">Edit</button>
              {Boolean(r.enabled) && (
                <button onClick={() => disableMut.mutate(Number(r.id))} disabled={disableMut.isPending}
                  className="inline-flex items-center gap-1 rounded-lg border border-red-200 px-2.5 py-1.5 text-xs font-semibold text-red-600 hover:bg-red-50 disabled:opacity-50">
                  <Trash2 className="h-3.5 w-3.5" /> Disable
                </button>
              )}
            </div>
          </div>
        ))}
      </div>

      {/* Add / edit form */}
      {draft && (
        <div className="mt-4 rounded-xl border border-teal-200 bg-teal-50/30 p-4">
          <div className="mb-3 flex items-center justify-between">
            <h4 className="text-sm font-bold text-slate-900">{draft.id ? "Edit connection" : "New connection"}</h4>
            <button onClick={() => setDraft(null)} aria-label="Cancel" className="text-slate-400 hover:text-slate-700"><X className="h-4 w-4" /></button>
          </div>
          <div className="grid gap-3 sm:grid-cols-2">
            <Field label="Display name">
              <input value={draft.displayName} onChange={(e) => setDraft({ ...draft, displayName: e.target.value })}
                placeholder="Acme Corp SSO" className={inputCls} />
            </Field>
            <Field label="Protocol">
              <select value={draft.providerType} onChange={(e) => setDraft({ ...draft, providerType: e.target.value as "oidc" | "saml" })} className={inputCls}>
                <option value="oidc">OIDC (OpenID Connect)</option>
                <option value="saml">SAML 2.0 (configuration only — sign-in not yet active)</option>
              </select>
              {draft.providerType === "saml" && (
                <p className="mt-1 text-[11px] font-medium text-amber-700">
                  SAML sign-in is not live yet: users on these domains will keep using password login until SAML support ships. Use OIDC for active SSO today.
                </p>
              )}
            </Field>
            <Field label="Email domains (comma-separated)">
              <input value={draft.domainHints} onChange={(e) => setDraft({ ...draft, domainHints: e.target.value })}
                placeholder="acme.com, acme.io" className={inputCls} />
            </Field>
            <Field label={draft.providerType === "oidc" ? "Issuer URL" : "Entity ID"}>
              <input value={draft.issuerOrEntityId} onChange={(e) => setDraft({ ...draft, issuerOrEntityId: e.target.value })}
                placeholder={draft.providerType === "oidc" ? "https://tenant.us.auth0.com/" : "urn:acme:sso"} className={inputCls} />
            </Field>
            <Field label="Client ID">
              <input value={draft.clientId} onChange={(e) => setDraft({ ...draft, clientId: e.target.value })}
                placeholder="Application client id" className={inputCls} />
            </Field>
            <Field label="Client secret reference">
              <input value={draft.clientSecretRef} onChange={(e) => setDraft({ ...draft, clientSecretRef: e.target.value })}
                placeholder="env:SSO_OIDC_CLIENT_SECRET" className={inputCls} />
            </Field>
            <Field label="Metadata / discovery URL (optional)">
              <input value={draft.metadataUrl} onChange={(e) => setDraft({ ...draft, metadataUrl: e.target.value })}
                placeholder="https://tenant.us.auth0.com/.well-known/openid-configuration" className={inputCls} />
            </Field>
            <Field label="Status">
              <label className="flex items-center gap-2 pt-2 text-sm text-slate-700">
                <input type="checkbox" checked={draft.enabled} onChange={(e) => setDraft({ ...draft, enabled: e.target.checked })} className="h-4 w-4 accent-teal-600" />
                Enabled (participates in login routing)
              </label>
            </Field>
          </div>
          <p className="mt-2 text-[11px] text-slate-500">
            The secret itself is never stored here — set it in your server environment and reference it as <code className="rounded bg-slate-100 px-1">env:VAR_NAME</code>. Leave blank to keep the existing reference.
          </p>
          {formError && <p role="alert" className="mt-2 text-xs font-medium text-red-600">{formError}</p>}
          <div className="mt-3 flex items-center gap-2">
            <button onClick={() => saveMut.mutate(draft)} disabled={!canSave || saveMut.isPending}
              className="rounded-lg bg-teal-600 px-4 py-2 text-xs font-semibold text-white hover:bg-teal-500 disabled:opacity-50">
              {saveMut.isPending ? "Saving…" : draft.id ? "Save changes" : "Create connection"}
            </button>
            <button onClick={() => setDraft(null)} className="rounded-lg border border-slate-200 px-4 py-2 text-xs font-semibold text-slate-600 hover:bg-slate-50">Cancel</button>
          </div>
        </div>
      )}
    </div>
  );
}

const inputCls = "w-full rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm text-slate-900 outline-none focus:border-teal-500 focus:ring-2 focus:ring-teal-500/15";

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <label className="block">
      <span className="mb-1 block text-xs font-semibold text-slate-600">{label}</span>
      {children}
    </label>
  );
}
