import { useCallback, useEffect, useState } from "react";
import axios from "axios";
import { AlertTriangle, CheckCircle2, Link2, Mail, Send } from "lucide-react";
import { PHeader, PCard, PButton, PField, PInput, PSelect, PLoading, PError } from "./ui";
import { platformApi } from "@/services/platformApi";
import { usePlatformAuth } from "@/hooks/usePlatformAuth";

type Banner = { tone: "ok" | "error"; text: string };

// SMTP presets for the common providers, so configuring mail is normally just
// "pick provider, paste credentials". Every entry uses port 587 with STARTTLS —
// deliberately: the server sends via .NET SmtpClient, which does NOT support
// implicit TLS on 465, and all of these providers accept 587. Host/port stay
// editable after selection, and Custom leaves everything open, so an unlisted
// provider is never blocked.
type SmtpPreset = {
  key: string;
  label: string;
  host: string;
  port: number;
  /** Username the provider mandates (e.g. SendGrid's literal "apikey"), auto-filled. */
  fixedUsername?: string;
  /** Matches every regional/alternate host this provider uses, so a stored
   *  eu-west-1 SES host or smtp.eu.mailgun.org is recognized as this provider AND
   *  survives re-selecting the preset without being reset to the default host. */
  hostPattern?: RegExp;
  usernamePlaceholder: string;
  passwordPlaceholder: string;
  note: string;
};

const SMTP_PRESETS: SmtpPreset[] = [
  {
    key: "gmail", label: "Gmail / Google Workspace", host: "smtp.gmail.com", port: 587,
    usernamePlaceholder: "you@yourdomain.com", passwordPlaceholder: "16-character App Password",
    note: "Requires an App Password (Google Account → Security → 2-Step Verification → App passwords). Your normal Google password will not work.",
  },
  {
    key: "microsoft365", label: "Microsoft 365 / Outlook", host: "smtp.office365.com", port: 587,
    usernamePlaceholder: "you@yourdomain.com", passwordPlaceholder: "Mailbox password",
    note: "The mailbox must have SMTP AUTH enabled (Microsoft 365 admin → user → Mail → Manage email apps → Authenticated SMTP). Note: Microsoft is retiring password-based SMTP AUTH — it is disabled by default from January 2027, so plan to move to a dedicated sending provider.",
  },
  {
    key: "sendgrid", label: "SendGrid", host: "smtp.sendgrid.net", port: 587,
    fixedUsername: "apikey",
    usernamePlaceholder: "apikey", passwordPlaceholder: "SendGrid API key (SG.…)",
    note: "Username is the literal word \"apikey\" (already filled in). The password is an API key with Mail Send permission.",
  },
  {
    key: "ses", label: "Amazon SES", host: "email-smtp.us-east-1.amazonaws.com", port: 587,
    hostPattern: /^email-smtp\.[a-z0-9-]+\.amazonaws\.com$/,
    usernamePlaceholder: "SES SMTP username (AKIA…)", passwordPlaceholder: "SES SMTP password",
    note: "Adjust the region in the host if your SES identity is not in us-east-1. Use SMTP credentials from the SES console (not plain IAM keys), and note the from address must be a verified identity.",
  },
  {
    key: "mailgun", label: "Mailgun", host: "smtp.mailgun.org", port: 587,
    hostPattern: /^smtp(\.eu)?\.mailgun\.org$/,
    usernamePlaceholder: "postmaster@mg.yourdomain.com", passwordPlaceholder: "SMTP password",
    note: "Use the SMTP credentials from Mailgun → Sending → Domain settings. EU-hosted domains use smtp.eu.mailgun.org — edit the host if so.",
  },
  {
    key: "brevo", label: "Brevo (Sendinblue)", host: "smtp-relay.brevo.com", port: 587,
    usernamePlaceholder: "SMTP login from Brevo", passwordPlaceholder: "SMTP key (xsmtpsib-…)",
    note: "Use the SMTP key from Brevo → SMTP & API — not your account password.",
  },
  {
    key: "postmark", label: "Postmark", host: "smtp.postmarkapp.com", port: 587,
    usernamePlaceholder: "Server API token", passwordPlaceholder: "Server API token (same value)",
    note: "Postmark uses the Server API token as BOTH the username and the password.",
  },
  {
    key: "zoho", label: "Zoho Mail", host: "smtp.zoho.com", port: 587,
    hostPattern: /^smtp\.zoho\.(com|eu|in)$/,
    usernamePlaceholder: "you@yourdomain.com", passwordPlaceholder: "Password or app password",
    note: "Accounts on Zoho's EU or IN data centers use smtp.zoho.eu / smtp.zoho.in — edit the host if so. With 2FA enabled, use an app-specific password.",
  },
  {
    key: "godaddy", label: "GoDaddy Workspace Email", host: "smtpout.secureserver.net", port: 587,
    hostPattern: /^smtpout(\.(europe|asia))?\.secureserver\.net$/,
    usernamePlaceholder: "you@yourdomain.com", passwordPlaceholder: "Mailbox password",
    note: "For classic GoDaddy Workspace Email — username is the full email address. GoDaddy's newer Professional Email is powered by Titan (use the Titan preset), and Microsoft 365 from GoDaddy uses the Microsoft 365 preset.",
  },
  {
    key: "titan", label: "Titan Mail", host: "smtp.titan.email", port: 587,
    usernamePlaceholder: "you@yourdomain.com", passwordPlaceholder: "Mailbox password",
    note: "Username is the full email address, and the from address must match that mailbox exactly — Titan rejects mismatched senders. Third-party app access must be enabled on the account (and with 2FA on, use an app password). Also the right preset for GoDaddy Professional Email, which Titan powers.",
  },
  {
    key: "resend", label: "Resend", host: "smtp.resend.com", port: 587,
    fixedUsername: "resend",
    usernamePlaceholder: "resend", passwordPlaceholder: "Resend API key (re_…)",
    note: "Username is the literal word \"resend\" (already filled in). The password is an API key.",
  },
  {
    key: "custom", label: "Custom / other", host: "", port: 587,
    usernamePlaceholder: "SMTP username", passwordPlaceholder: "SMTP password or API key",
    note: "",
  },
];

/** Whether a configured host belongs to this preset's provider (regional hosts included). */
function hostMatchesPreset(preset: SmtpPreset, host: string): boolean {
  const trimmed = host.trim().toLowerCase();
  if (preset.key === "custom" || !trimmed) return false;
  return preset.hostPattern ? preset.hostPattern.test(trimmed) : preset.host === trimmed;
}

/** The preset whose host matches what is configured, so reopening the page shows the right provider. */
function presetForHost(host: string): SmtpPreset {
  return SMTP_PRESETS.find((p) => hostMatchesPreset(p, host)) ?? SMTP_PRESETS[SMTP_PRESETS.length - 1];
}

// Surfaces the server's real rejection reason (e.g. why SMTP failed) instead of axios's
// generic "Request failed with status code 400". Mirrors the pattern in LoginPage.tsx.
function getErrorMessage(err: unknown, fallback: string): string {
  const serverMessage = axios.isAxiosError(err)
    ? (err.response?.data?.message ?? err.response?.data?.errors?.[0])
    : undefined;
  return serverMessage ? String(serverMessage) : err instanceof Error ? err.message : fallback;
}

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
  const [storageAvailable, setStorageAvailable] = useState(true);
  const [provider, setProvider] = useState("custom");
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
      setStorageAvailable(email.storageAvailable !== false);
      setProvider(presetForHost(String(email.host ?? "")).key);
      setSource(email.source as string | undefined);
      setTenantAppUrl(String(urls.tenantAppUrl ?? ""));
      setPlatformAppUrl(String(urls.platformAppUrl ?? ""));
      setUrlSource(urls.tenantUrlSource as string | undefined);
      setTestTo((prev) => prev || session?.admin.email || "");
    } catch (e) {
      setError(getErrorMessage(e, "Failed to load email settings"));
    } finally {
      setLoading(false);
    }
  }, [session?.admin.email]);

  useEffect(() => { void load(); }, [load]);

  const activePreset = SMTP_PRESETS.find((p) => p.key === provider) ?? SMTP_PRESETS[SMTP_PRESETS.length - 1];

  const applyPreset = (key: string) => {
    setProvider(key);
    const preset = SMTP_PRESETS.find((p) => p.key === key);
    if (!preset) return;
    if (preset.key === "custom") {
      // A stranded provider literal is always wrong on a custom relay.
      if (username === "apikey" || username === "resend") setUsername("");
      return;
    }
    // A host already belonging to this provider (any region) is a deliberate edit —
    // e.g. eu-west-1 SES or smtp.eu.mailgun.org. Flipping through the dropdown to read
    // another provider's note and coming back must NOT reset it to the default region,
    // nor stomp a tuned port/TLS setting.
    if (!hostMatchesPreset(preset, host)) {
      setHost(preset.host);
      setPort(String(preset.port));
      setEnableSsl(true);
    }
    // Only write the username when the provider mandates a literal value; a
    // typed-in mailbox name must survive switching between providers.
    if (preset.fixedUsername) setUsername(preset.fixedUsername);
    else if (username === "apikey" || username === "resend") setUsername("");
  };

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
      setBanner({ tone: "error", text: getErrorMessage(e, "Save failed") });
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
      setBanner({ tone: "error", text: getErrorMessage(e, "Save failed") });
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
      setBanner({ tone: "error", text: getErrorMessage(e, "Test send failed") });
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

      {!storageAvailable && (
        <div className="flex items-start gap-2 rounded-[14px] border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
          <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
          <span>
            Settings storage is not provisioned on this deployment, so values cannot be saved from the console
            yet — apply <code className="rounded bg-red-100 px-1">database/migrations/2026_08_21_stage83_platform_settings.sql</code> to
            the database (or redeploy so the API can create the table). Environment-variable configuration
            (<code className="rounded bg-red-100 px-1">SMTP_*</code>) still works in the meantime.
          </span>
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

        <div className="mb-4">
          <PField label="Provider">
            <PSelect value={provider} onChange={(e) => applyPreset(e.target.value)}>
              {SMTP_PRESETS.map((preset) => (
                <option key={preset.key} value={preset.key}>{preset.label}</option>
              ))}
            </PSelect>
          </PField>
          {activePreset.note && (
            <p className="mt-2 rounded-lg border border-sky-200 bg-sky-50 px-3 py-2 text-xs leading-5 text-sky-800">
              {activePreset.note}
            </p>
          )}
        </div>

        <div className="grid gap-4 sm:grid-cols-2">
          <PField label="Host">
            <PInput value={host} onChange={(e) => setHost(e.target.value)} placeholder="smtp.example.com" autoComplete="off" />
          </PField>
          <PField label="Port">
            <PInput value={port} onChange={(e) => setPort(e.target.value.replace(/\D/g, ""))} placeholder="587" inputMode="numeric" />
          </PField>
          <PField label="Username">
            <PInput value={username} onChange={(e) => setUsername(e.target.value)} placeholder={activePreset.usernamePlaceholder} autoComplete="off" />
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
                  : passwordSet ? "stored — leave blank to keep" : activePreset.passwordPlaceholder
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
          <PButton variant="primary" onClick={() => void save()} disabled={saving || !storageAvailable}>
            {!storageAvailable ? "Storage not provisioned" : saving ? "Saving" : "Save settings"}
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
          <PButton variant="primary" onClick={() => void saveUrls()} disabled={savingUrls || !storageAvailable}>
            {!storageAvailable ? "Storage not provisioned" : savingUrls ? "Saving" : "Save URLs"}
          </PButton>
        </div>
      </PCard>
    </div>
  );
}
