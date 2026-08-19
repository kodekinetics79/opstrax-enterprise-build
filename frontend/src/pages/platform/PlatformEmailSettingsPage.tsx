import { useCallback, useEffect, useState } from "react";
import { AlertTriangle, CheckCircle2, Link2, Mail, Send } from "lucide-react";
import { PHeader, PCard, PButton, PField, PInput, PLoading, PError } from "./ui";
import { platformApi } from "@/services/platformApi";
import { usePlatformAuth } from "@/hooks/usePlatformAuth";

type Banner = { tone: "ok" | "error"; text: string };

// Where the current value comes from, so an operator is never left wondering why an
// edit "didn't stick" — environment values are live but not editable from here.
function SourceNote({ source }: { source?: string }) {
  if (source === "database") return <span className="text-slate-500">Managed here in the console.</span>;
  if (source === "environment")
    return <span className="text-amber-700">Currently supplied by a deployment environment variable. Saving here overrides it.</span>;
  return <span className="text-slate-500">Not configured yet.</span>;
}

export function PlatformEmailSettingsPage() {
  const { session } = usePlatformAuth();
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [banner, setBanner] = useState<Banner | null>(null);
  const [saving, setSaving] = useState(false);
  const [testing, setTesting] = useState(false);

  const [host, setHost] = useState("");
  const [port, setPort] = useState("587");
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [passwordSet, setPasswordSet] = useState(false);
  const [fromAddress, setFromAddress] = useState("");
  const [fromName, setFromName] = useState("");
  const [enableSsl, setEnableSsl] = useState(true);
  const [configured, setConfigured] = useState(false);
  const [canStorePassword, setCanStorePassword] = useState(true);
  const [source, setSource] = useState<string | undefined>();

  const [tenantAppUrl, setTenantAppUrl] = useState("");
  const [platformAppUrl, setPlatformAppUrl] = useState("");
  const [urlSource, setUrlSource] = useState<string | undefined>();
  const [savingUrls, setSavingUrls] = useState(false);

  const [testTo, setTestTo] = useState("");

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const [email, urls] = await Promise.all([platformApi.emailSettings(), platformApi.appUrlSettings()]);
      setHost(String(email.host ?? ""));
      setPort(String(email.port ?? 587));
      setUsername(String(email.username ?? ""));
      setPasswordSet(email.passwordSet === true);
      setFromAddress(String(email.fromAddress ?? ""));
      setFromName(String(email.fromName ?? ""));
      setEnableSsl(email.enableSsl !== false);
      setConfigured(email.configured === true);
      setCanStorePassword(email.canStorePassword !== false);
      setSource(email.source as string | undefined);
      setTenantAppUrl(String(urls.tenantAppUrl ?? ""));
      setPlatformAppUrl(String(urls.platformAppUrl ?? ""));
      setUrlSource(urls.tenantUrlSource as string | undefined);
      setTestTo((prev) => prev || session?.admin.email || "");
    } catch (e) {
      setError(e instanceof Error ? e.message : "Failed to load email settings");
    } finally {
      setLoading(false);
    }
  }, [session?.admin.email]);

  useEffect(() => { void load(); }, [load]);

  const save = async () => {
    setSaving(true);
    setBanner(null);
    try {
      await platformApi.saveEmailSettings({
        host: host.trim(),
        port: Number(port) || 587,
        username: username.trim(),
        // Empty means "keep the stored password" — the server never sends it back, so
        // an untouched field must not be read as a request to clear the credential.
        password: password || undefined,
        fromAddress: fromAddress.trim(),
        fromName: fromName.trim(),
        enableSsl,
      });
      setPassword("");
      setBanner({ tone: "ok", text: "Email settings saved. Send a test message to confirm delivery." });
      await load();
    } catch (e) {
      setBanner({ tone: "error", text: e instanceof Error ? e.message : "Save failed" });
    } finally {
      setSaving(false);
    }
  };

  const saveUrls = async () => {
    setSavingUrls(true);
    setBanner(null);
    try {
      await platformApi.saveAppUrlSettings({ tenantAppUrl: tenantAppUrl.trim(), platformAppUrl: platformAppUrl.trim() });
      setBanner({ tone: "ok", text: "Application URLs saved." });
      await load();
    } catch (e) {
      setBanner({ tone: "error", text: e instanceof Error ? e.message : "Save failed" });
    } finally {
      setSavingUrls(false);
    }
  };

  const sendTest = async () => {
    setTesting(true);
    setBanner(null);
    try {
      const res = await platformApi.sendTestEmail(testTo.trim());
      setBanner({ tone: "ok", text: `Test email sent to ${res.to ?? testTo}. Check the inbox (and spam).` });
    } catch (e) {
      // The server returns the real SMTP failure — surfacing it is the whole point of a test.
      setBanner({ tone: "error", text: e instanceof Error ? e.message : "Test send failed" });
    } finally {
      setTesting(false);
    }
  };

  if (loading) return <PLoading />;
  if (error) return <PError message={error} />;

  return (
    <div className="space-y-5">
      <PHeader
        eyebrow="Platform settings"
        title="Email delivery"
        description="Outbound SMTP for tenant administrator invites, operator invites and password resets. Without it, invites are created but never delivered."
      />

      {banner && (
        <div className={`flex items-start gap-2 rounded-[14px] border px-4 py-3 text-sm ${
          banner.tone === "ok"
            ? "border-emerald-200 bg-emerald-50 text-emerald-800"
            : "border-red-200 bg-red-50 text-red-700"}`}>
          {banner.tone === "ok" ? <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0" /> : <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />}
          <span>{banner.text}</span>
        </div>
      )}

      {!configured && (
        <div className="flex items-start gap-2 rounded-[14px] border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-800">
          <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
          <span>
            Email is not configured, so no invite or reset message can be delivered. Invites still work — the
            console shows a one-time activation link to hand over instead.
          </span>
        </div>
      )}

      <PCard>
        <div className="mb-4 flex items-center gap-2">
          <Mail className="h-4 w-4 text-teal-600" />
          <h2 className="text-sm font-bold text-slate-900">SMTP server</h2>
        </div>

        <div className="grid gap-4 sm:grid-cols-2">
          <PField label="Host">
            <PInput value={host} onChange={(e) => setHost(e.target.value)} placeholder="smtp.sendgrid.net" autoComplete="off" />
          </PField>
          <PField label="Port">
            <PInput value={port} onChange={(e) => setPort(e.target.value.replace(/\D/g, ""))} placeholder="587" inputMode="numeric" />
          </PField>
          <PField label="Username">
            <PInput value={username} onChange={(e) => setUsername(e.target.value)} placeholder="apikey" autoComplete="off" />
          </PField>
          <PField label={passwordSet ? "Password (leave blank to keep current)" : "Password"}>
            <PInput
              type="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              disabled={!canStorePassword}
              placeholder={
                !canStorePassword
                  ? "Unavailable — see note below"
                  : passwordSet ? "stored — leave blank to keep" : "SMTP password or API key"
              }
              autoComplete="new-password"
            />
          </PField>
          <PField label="From address">
            <PInput value={fromAddress} onChange={(e) => setFromAddress(e.target.value)} placeholder="no-reply@yourdomain.com" autoComplete="off" />
          </PField>
          <PField label="From name">
            <PInput value={fromName} onChange={(e) => setFromName(e.target.value)} placeholder="OpsTrax" autoComplete="off" />
          </PField>
        </div>

        <label className="mt-4 flex items-center gap-2 text-sm text-slate-700">
          <input type="checkbox" checked={enableSsl} onChange={(e) => setEnableSsl(e.target.checked)} className="h-4 w-4 rounded border-slate-300" />
          Use TLS (leave on for port 587; turn off only for an unencrypted relay)
        </label>

        {!canStorePassword && (
          <p className="mt-3 rounded-lg border border-amber-200 bg-amber-50 px-3 py-2 text-xs leading-5 text-amber-800">
            The password field is disabled because no PII data key is configured, and this console will not write a
            credential to the database unencrypted. Configure <code>DATA_ENCRYPTION_KEY</code>, or supply the password through
            the <code>SMTP_PASS</code> environment variable — every other setting here still applies.
          </p>
        )}

        <p className="mt-3 text-xs leading-5">
          <SourceNote source={source} />
        </p>

        <div className="mt-4 flex flex-wrap items-center gap-2">
          <PButton variant="primary" onClick={() => void save()} disabled={saving}>
            {saving ? "Saving" : "Save settings"}
          </PButton>
        </div>
      </PCard>

      <PCard>
        <div className="mb-4 flex items-center gap-2">
          <Send className="h-4 w-4 text-teal-600" />
          <h2 className="text-sm font-bold text-slate-900">Send a test message</h2>
        </div>
        <p className="mb-3 text-xs leading-5 text-slate-500">
          Delivers a real message through the settings above. Any SMTP rejection is shown verbatim so you can tell a
          wrong password from a blocked port or an unverified sender.
        </p>
        <div className="flex flex-wrap items-end gap-3">
          <div className="min-w-[260px] flex-1">
            <PField label="Recipient">
              <PInput value={testTo} onChange={(e) => setTestTo(e.target.value)} placeholder="you@yourdomain.com" />
            </PField>
          </div>
          <PButton onClick={() => void sendTest()} disabled={testing || !configured}>
            {testing ? "Sending" : "Send test"}
          </PButton>
        </div>
      </PCard>

      <PCard>
        <div className="mb-4 flex items-center gap-2">
          <Link2 className="h-4 w-4 text-teal-600" />
          <h2 className="text-sm font-bold text-slate-900">Application URLs</h2>
        </div>
        <p className="mb-4 text-xs leading-5 text-slate-500">
          The tenant app URL is what an activation link points at. If it is unset, an invited administrator cannot be
          sent anywhere — set it even if you never enable SMTP.
        </p>
        <div className="grid gap-4 sm:grid-cols-2">
          <PField label="Tenant application URL">
            <PInput value={tenantAppUrl} onChange={(e) => setTenantAppUrl(e.target.value)} placeholder="https://opstrax.vercel.app" />
          </PField>
          <PField label="Platform admin URL">
            <PInput value={platformAppUrl} onChange={(e) => setPlatformAppUrl(e.target.value)} placeholder="https://opstrax.vercel.app" />
          </PField>
        </div>
        <p className="mt-3 text-xs leading-5">
          <SourceNote source={urlSource} />
        </p>
        <div className="mt-4">
          <PButton variant="primary" onClick={() => void saveUrls()} disabled={savingUrls}>
            {savingUrls ? "Saving" : "Save URLs"}
          </PButton>
        </div>
      </PCard>
    </div>
  );
}
