import { useCallback, useEffect, useRef, useState } from "react";
import { useMutation } from "@tanstack/react-query";
import axios from "axios";
import { AlertCircle, ArrowRight, Building2, ClipboardCheck, Route, ShieldCheck, Wrench } from "lucide-react";
import { flushSync } from "react-dom";
import { Link, useNavigate } from "react-router";
import { getLandingRouteForSession } from "@/auth/sessionRouting";
import { useAuth } from "@/hooks/useAuth";
import { authApi, isMfaChallenge, type MfaChallenge, type SsoConnection } from "@/services/authApi";
import { API_BASE_URL } from "@/services/apiClient";
import { OpsTraxLogo } from "@/components/OpsTraxLogo";
import "./login-access.css";

/** Minimal structural email check — mirrors the backend's non-revealing validation. */
const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

function getLoginErrorMessage(error: unknown): string {
  if (!axios.isAxiosError(error)) {
    return "We could not complete sign-in. Please try again.";
  }

  if (error.code === "ECONNABORTED") {
    return "OpsTrax is taking too long to respond. The backend may be waking up, so please try again in a few seconds.";
  }

  const status = error.response?.status;
  if (status === 401) {
    return "The organization code, email, or password was not recognized. Please verify your credentials and try again.";
  }
  if (status === 403) {
    return "Security verification did not complete. Refresh the page and try signing in again.";
  }
  if (status === 429) {
    return "Too many sign-in attempts were detected. Wait a moment, then try again.";
  }
  if (!error.response) {
    return "We could not reach the OpsTrax API. Check the connection or retry once the service is fully awake.";
  }

  return String(error.response?.data?.message ?? "We could not complete sign-in. Please try again.");
}

/** True when the backend rejected the MFA attempt because the signed challenge
 *  itself is gone (expired or already consumed) rather than a wrong code — the
 *  only case that requires restarting sign-in instead of just retrying the code. */
function isMfaChallengeExpired(error: unknown): boolean {
  if (!axios.isAxiosError(error)) return false;
  const message = String(error.response?.data?.message ?? "");
  return /expired|already used/i.test(message);
}

function getMfaErrorMessage(error: unknown): string {
  if (!axios.isAxiosError(error)) {
    return "We could not verify the code. Please try again.";
  }
  if (error.code === "ECONNABORTED") {
    return "OpsTrax is taking too long to respond. Please try again in a few seconds.";
  }
  if (isMfaChallengeExpired(error)) {
    return "Your sign-in session expired. Please sign in again.";
  }
  const status = error.response?.status;
  if (status === 401) {
    return "That code was not accepted. Check your authenticator app and try again.";
  }
  if (status === 429) {
    return "Too many attempts were detected. Wait a moment, then try again.";
  }
  if (!error.response) {
    return "We could not reach the OpsTrax API. Check the connection and try again.";
  }
  return String(error.response?.data?.message ?? "We could not verify the code. Please try again.");
}

/* ── Product pillars (non-numeric product statements) ───────────────────── */
const PLATFORM_PILLARS = [
  { icon: Route,          label: "Dispatch" },
  { icon: ShieldCheck,    label: "Safety" },
  { icon: ClipboardCheck, label: "Compliance" },
  { icon: Wrench,         label: "Maintenance" },
] as const;

const ACCESS_GUIDANCE = [
  {
    title: "Dispatcher",
    note: "Assignments, live exceptions, and control-room workflow.",
  },
  {
    title: "Operations Lead",
    note: "Fleet health, alerts, compliance, and command center views.",
  },
  {
    title: "Driver",
    note: "Mobile task flow, POD, proof package, and active trips.",
  },
  {
    title: "Customer",
    note: "Track visibility, milestones, and proof status.",
  },
] as const;

/* ── Main component ─────────────────────────────────────────────────────── */
export function LoginPage() {
  const { setSession } = useAuth();
  const navigate = useNavigate();
  const [companyCode, setCompanyCode] = useState("");
  const [email, setEmail]           = useState("");
  const [password, setPassword]     = useState("");
  const [showPassword, setShowPass] = useState(false);
  const [passwordError, setPasswordError] = useState("");
  const [companyCodeError, setCompanyCodeError] = useState("");
  const [emailError, setEmailError] = useState("");
  // Identifier-first: "identify" collects the email; "authenticate" reveals the
  // password field OR the SSO button depending on the domain's SSO config; "mfa"
  // is a third step reached only when the tenant requires a second factor.
  const [step, setStep]             = useState<"identify" | "authenticate" | "mfa">("identify");
  const [ssoConn, setSsoConn]       = useState<SsoConnection | null>(null);
  const [mfaChallenge, setMfaChallenge] = useState<MfaChallenge | null>(null);
  const [mfaCode, setMfaCode]       = useState("");
  const companyCodeRef = useRef<HTMLInputElement>(null);
  const emailRef = useRef<HTMLInputElement>(null);
  const passwordRef = useRef<HTMLInputElement>(null);
  const mfaCodeRef = useRef<HTMLInputElement>(null);

  // Resolve whether the email's domain routes to SSO. Fails OPEN to the password
  // field so a discovery outage never blocks a password login.
  const identify = useMutation({
    mutationFn: async ({ email: e, companyCode: code }: { email: string; companyCode: string }) =>
      authApi.ssoDiscover(e, code),
    onSuccess: (result) => {
      setSsoConn(result.ssoConfigured && result.connection ? result.connection : null);
      setStep("authenticate");
    },
    onError: () => {
      setSsoConn(null);
      setStep("authenticate");
    },
  });

  const login = useMutation({
    mutationFn: async ({ email: e, password: p, companyCode: code }: { email: string; password: string; companyCode: string }) => {
      await authApi.bootstrap();
      return authApi.login(e, p, code);
    },
    onSuccess: (result) => {
      if (isMfaChallenge(result)) {
        // No session yet — the tenant requires a second factor. Hold the signed
        // challenge and move to the code-entry step instead of signing in.
        setMfaChallenge(result);
        setMfaCode("");
        setStep("mfa");
        return;
      }
      flushSync(() => {
        setSession(result);
      });
      navigate(getLandingRouteForSession(result), { replace: true });
    },
    // Do not leave a rejected credential resident in the rendered document or
    // accidentally resubmit it. Keep the non-enumerating error, clear only the
    // password, and return focus for a deliberate retry.
    onError: () => {
      setPassword("");
      if (passwordRef.current) passwordRef.current.value = "";
      requestAnimationFrame(() => passwordRef.current?.focus());
    },
  });

  const mfaVerify = useMutation({
    mutationFn: async (code: string) => {
      if (!mfaChallenge) throw new Error("Your sign-in session expired. Please sign in again.");
      return authApi.mfaLoginVerify(mfaChallenge.challengeToken, code);
    },
    onSuccess: (session) => {
      flushSync(() => {
        setSession(session);
      });
      navigate(getLandingRouteForSession(session), { replace: true });
    },
    // A wrong code should not lose the in-flight challenge — only a genuinely
    // expired/consumed challenge forces the user back to the password step.
    onError: (err) => {
      setMfaCode("");
      if (isMfaChallengeExpired(err)) {
        setMfaChallenge(null);
        setStep("authenticate");
        setPassword("");
        if (passwordRef.current) passwordRef.current.value = "";
        requestAnimationFrame(() => passwordRef.current?.focus());
        return;
      }
      requestAnimationFrame(() => mfaCodeRef.current?.focus());
    },
  });

  // Move focus to the password field the moment it is revealed (a11y + speed).
  useEffect(() => {
    if (step === "authenticate" && !ssoConn) passwordRef.current?.focus();
  }, [step, ssoConn]);

  // Password managers can populate a controlled input without dispatching the
  // input/change event React normally uses to update state. Copy only values
  // already present in the browser-owned fields into the existing in-memory
  // form state; never persist, log, serialize, or expose detected credentials.
  const syncBrowserFilledFields = useCallback(() => {
    const nextCompanyCode = companyCodeRef.current?.value ?? "";
    const nextEmail = emailRef.current?.value ?? "";
    const nextPassword = passwordRef.current?.value ?? "";
    if (nextCompanyCode) setCompanyCode((current) => current === nextCompanyCode ? current : nextCompanyCode);
    if (nextEmail) setEmail((current) => current === nextEmail ? current : nextEmail);
    if (nextPassword) setPassword((current) => current === nextPassword ? current : nextPassword);
  }, []);

  useEffect(() => {
    // Autofill may settle after paint or after Chrome finishes its credential UI.
    // Keep this bounded so the page does not poll for the lifetime of a session.
    syncBrowserFilledFields();
    const frame = requestAnimationFrame(syncBrowserFilledFields);
    const interval = window.setInterval(syncBrowserFilledFields, 200);
    const stop = window.setTimeout(() => window.clearInterval(interval), 2_000);
    const onVisibility = () => { if (document.visibilityState === "visible") syncBrowserFilledFields(); };
    document.addEventListener("visibilitychange", onVisibility);
    return () => {
      cancelAnimationFrame(frame);
      window.clearInterval(interval);
      window.clearTimeout(stop);
      document.removeEventListener("visibilitychange", onVisibility);
    };
  }, [step, ssoConn, syncBrowserFilledFields]);

  // Move focus to the MFA code field the moment it is revealed.
  useEffect(() => {
    if (step === "mfa") mfaCodeRef.current?.focus();
  }, [step]);

  const continueWithEmail = () => {
    const code = companyCode.trim();
    const value = email.trim();
    if (!code) { setCompanyCodeError("Enter your organization code."); return; }
    setCompanyCodeError("");
    if (!EMAIL_RE.test(value)) { setEmailError("Enter a valid work email address."); return; }
    setEmailError("");
    identify.mutate({ email: value, companyCode: code });
  };

  const editEmail = () => {
    setStep("identify");
    setSsoConn(null);
    setPassword("");
    setPasswordError("");
    setMfaChallenge(null);
    setMfaCode("");
    login.reset();
    mfaVerify.reset();
  };

  const goToSso = () => {
    // Initiate the flow through our own start endpoint on the API host; it derives
    // the IdP authorize URL from the connection and 302-redirects to the provider.
    if (ssoConn) window.location.assign(`${API_BASE_URL}/api/auth/sso/start/${ssoConn.id}`);
  };

  const submit = (e: React.FormEvent) => {
    e.preventDefault();
    if (step === "identify") { continueWithEmail(); return; }
    if (step === "mfa") { if (mfaCode.trim()) mfaVerify.mutate(mfaCode.trim()); return; }
    if (ssoConn) { goToSso(); return; }
    // Read the submitted password from the browser-owned input. Chrome may
    // render a saved credential without emitting React's change event; keeping
    // this field uncontrolled prevents a render with empty state from erasing
    // that native autofill before submission.
    const submittedPassword = passwordRef.current?.value ?? password;
    if (!submittedPassword) {
      setPasswordError("Enter your password.");
      passwordRef.current?.focus();
      return;
    }
    setPasswordError("");
    if (companyCode.trim() && email.trim()) {
      login.mutate({ companyCode: companyCode.trim(), email: email.trim(), password: submittedPassword });
    }
  };

  const identifying = identify.isPending;
  // Surface a friendly message if the SSO round-trip bounced back to /login.
  const ssoErrorCode = new URLSearchParams(window.location.search).get("sso_error");
  const ssoErrorMessage = ssoErrorCode
    ? ssoErrorCode === "sso_no_account"
      ? "Single sign-on succeeded, but no OpsTrax account matches that identity. Contact your administrator."
      : ssoErrorCode === "sso_tenant_suspended"
        ? "Your organization's OpsTrax access is currently suspended. Contact your administrator."
        : ssoErrorCode === "sso_account_inactive"
          ? "Your OpsTrax account is not active. Contact your administrator."
          : "We couldn't complete single sign-on. Please try again or sign in with your password."
    : "";
  return (
    <main className="login-access">
      <aside className="login-access-brand" aria-label="OpsTrax fleet workspace">
        <div className="login-access-logo"><OpsTraxLogo size={40} /><span>OpsTrax</span></div>
        <div className="login-access-story">
          <h2>Your fleet.<br /><span>One workspace.</span></h2>
          <p>From job assignment to proof of delivery. Bring your teams and day-to-day operations together.</p>
          <div className="login-access-illustration" aria-hidden="true">
            <div className="login-access-plane login-access-plane-back" />
            <div className="login-access-plane login-access-plane-front">
              <svg viewBox="0 0 360 200" fill="none">
                <path d="M40 145H120V60H235V120H315" stroke="currentColor" strokeWidth="2" strokeDasharray="6 6" />
                <path d="M40 145H235V120" stroke="currentColor" strokeWidth="1" opacity=".35" />
                <circle cx="40" cy="145" r="8" fill="currentColor" /><circle cx="120" cy="60" r="8" fill="currentColor" /><circle cx="235" cy="120" r="8" fill="currentColor" /><circle cx="315" cy="120" r="8" fill="currentColor" />
                <rect x="166" y="35" width="40" height="26" rx="5" fill="currentColor" opacity=".12" /><path d="M176 48h20m-10-6v12" stroke="currentColor" strokeWidth="2" />
              </svg>
            </div>
          </div>
          <div className="login-access-capabilities">
            {PLATFORM_PILLARS.map(({ icon: Icon, label }) => <span key={label}><Icon size={16} aria-hidden="true" />{label}</span>)}
          </div>
        </div>
        <p className="login-access-brand-footer">Fleet management by <a href="https://www.kodekinetics.com" target="_blank" rel="noopener noreferrer">Kode Kinetics</a></p>
      </aside>

      <section className="login-access-form-panel" aria-labelledby="login-title">
        <div className="login-access-mobile-logo"><OpsTraxLogo size={32} /><span>OpsTrax</span></div>
        <div className="login2 login-access-form-wrap">
          <div className="login2-card login-access-card">
              <ol className="login-access-steps" aria-label="Sign-in progress">
                <li aria-current={step === "identify" ? "step" : undefined}><span>1</span> Workspace</li>
                <li aria-current={step === "authenticate" ? "step" : undefined}><span>2</span> Sign in</li>
                {step === "mfa" && <li aria-current="step"><span>3</span> Verify</li>}
              </ol>
              <div className="login-access-heading">
                <h1 id="login-title">{step === "mfa" ? "Verify your sign-in" : step === "identify" ? "Sign in to OpsTrax" : "Sign in to your workspace"}</h1>
                <p>
                  {step === "identify"
                    ? "Enter your organization code and work email to continue."
                    : step === "mfa"
                      ? "Enter the 6-digit code from your authenticator app."
                      : ssoConn
                        ? "Single sign-on is available for your organization."
                        : "Enter your password to sign in."}
                </p>
              </div>

              {ssoErrorMessage && !login.isError && !mfaVerify.isError && (
                <div role="alert" className="mb-5 flex items-center gap-2.5 rounded-lg border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-800">
                  <AlertCircle className="h-4 w-4 shrink-0" />
                  {ssoErrorMessage}
                </div>
              )}

              {login.isError && (
                <div role="alert" className="mb-5 flex items-center gap-2.5 rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
                  <AlertCircle className="h-4 w-4 shrink-0" />
                  {getLoginErrorMessage(login.error)}
                </div>
              )}

              {mfaVerify.isError && (
                <div role="alert" className="mb-5 flex items-center gap-2.5 rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
                  <AlertCircle className="h-4 w-4 shrink-0" />
                  {getMfaErrorMessage(mfaVerify.error)}
                </div>
              )}

              <form onSubmit={submit} onInputCapture={syncBrowserFilledFields} autoComplete="on" className="space-y-4" noValidate>
                {/* Identity: editable email (step 1) → read-only chip (step 2) */}
                {step === "identify" ? (
                  <>
                    <div>
                      <label htmlFor="login-company-code" className="mb-1.5 block text-sm font-medium text-slate-700">Organization code</label>
                      <input
                        ref={companyCodeRef} id="login-company-code" name="organization" value={companyCode}
                        onChange={(e) => { setCompanyCode(e.target.value); if (companyCodeError) setCompanyCodeError(""); }}
                        autoComplete="organization" autoFocus placeholder="Your tenant code"
                        aria-invalid={companyCodeError ? true : undefined}
                        aria-describedby={companyCodeError ? "login-company-code-error" : "login-company-code-help"}
                        className="login2-field" />
                      {companyCodeError ? (
                        <p id="login-company-code-error" role="alert" className="mt-1.5 text-xs font-medium text-red-600">{companyCodeError}</p>
                      ) : (
                        <p id="login-company-code-help" className="mt-1.5 text-xs text-slate-500">Provided by your OpsTrax administrator.</p>
                      )}
                    </div>
                    <div>
                      <label htmlFor="login-email" className="mb-1.5 block text-sm font-medium text-slate-700">Work email</label>
                      <input
                        ref={emailRef} id="login-email" name="username" type="email" inputMode="email" value={email}
                        onChange={(e) => { setEmail(e.target.value); if (emailError) setEmailError(""); }}
                        autoComplete="username" placeholder="you@company.com"
                        aria-invalid={emailError ? true : undefined}
                        aria-describedby={emailError ? "login-email-error" : undefined}
                        className="login2-field" />
                      {emailError && (
                        <p id="login-email-error" role="alert" className="mt-1.5 text-xs font-medium text-red-600">{emailError}</p>
                      )}
                    </div>
                  </>
                ) : (
                  <div className="login2-idchip">
                    {/* Keep the username in the authentication form so password
                        managers can associate current-password with its identity. */}
                    <input
                      ref={emailRef}
                      name="username"
                      type="email"
                      autoComplete="username"
                      value={email}
                      readOnly
                      tabIndex={-1}
                      className="sr-only"
                      aria-hidden="true"
                    />
                    <span className="flex min-w-0 items-center gap-2">
                      <Building2 className="h-4 w-4 shrink-0 text-teal-600" aria-hidden="true" />
                      <span className="min-w-0">
                        <span className="login-access-identity text-sm font-medium text-slate-700">{email.trim()}</span>
                        <span className="login-access-identity text-xs text-slate-600">Organization {companyCode.trim()}</span>
                      </span>
                    </span>
                    <button type="button" onClick={editEmail}
                      className="login-access-change">
                      Change
                    </button>
                  </div>
                )}

                {/* Reveal block — password, SSO, or MFA code, animated open past step 1 */}
                <div className="login2-reveal" data-open={step !== "identify"} aria-hidden={step === "identify"}>
                  <div>
                    <div className="space-y-4 pt-1" aria-live="polite">
                      {step === "mfa" ? (
                        <div>
                          <label htmlFor="login-mfa-code" className="mb-1.5 block text-sm font-medium text-slate-700">Authenticator code</label>
                          <input
                            ref={mfaCodeRef} id="login-mfa-code" inputMode="numeric" autoComplete="one-time-code"
                            maxLength={6} value={mfaCode}
                            onChange={(e) => setMfaCode(e.target.value.replace(/\D/g, ""))}
                            placeholder="123456"
                            className="login2-field text-center font-mono text-lg tracking-[0.4em]" />
                        </div>
                      ) : step !== "authenticate" ? null : ssoConn ? (
                        <>
                          <button type="button" onClick={goToSso} className="login2-sso">
                            <ShieldCheck className="h-4 w-4" aria-hidden="true" />
                            Continue with {ssoConn.displayName}
                          </button>
                          <p className="text-center text-[11px] leading-5 text-slate-500">
                            You’ll continue to your organization’s identity provider to finish signing in.
                          </p>
                        </>
                      ) : (
                        <div>
                          <div className="mb-1.5 flex items-center justify-between">
                            <label htmlFor="login-password" className="block text-sm font-medium text-slate-700">Password</label>
                            <Link to="/forgot-password" className="text-xs font-semibold text-teal-700 hover:text-teal-600">Forgot password?</Link>
                          </div>
                          <div className="relative">
                            <input
                              ref={passwordRef} id="login-password" name="password" type={showPassword ? "text" : "password"}
                              defaultValue="" onChange={(e) => { setPassword(e.target.value); if (passwordError) setPasswordError(""); }}
                              onFocus={syncBrowserFilledFields}
                              onBlur={syncBrowserFilledFields}
                              autoComplete="current-password" placeholder="••••••••"
                              aria-invalid={passwordError ? true : undefined}
                              aria-describedby={passwordError ? "login-password-error" : undefined}
                              className="login2-field pr-20" />
                            <button type="button" onClick={() => setShowPass((v) => !v)}
                              aria-label={showPassword ? "Hide password" : "Show password"} aria-pressed={showPassword}
                              className="login2-eye absolute right-2.5 top-1/2 -translate-y-1/2">
                              {showPassword ? "Hide" : "Show"}
                            </button>
                          </div>
                          {passwordError && (
                            <p id="login-password-error" role="alert" className="mt-1.5 text-xs font-medium text-red-600">{passwordError}</p>
                          )}
                        </div>
                      )}
                    </div>
                  </div>
                </div>

                {/* Primary CTA — hidden in SSO mode where the SSO button is the action */}
                {!(ssoConn && step === "authenticate") && (
                  <button type="submit" className="login2-cta"
                    disabled={
                      step === "identify" ? (identifying || !companyCode.trim() || !email.trim())
                        : step === "mfa" ? (mfaVerify.isPending || mfaCode.trim().length !== 6)
                          : login.isPending
                    }>
                    {step === "identify"
                      ? (identifying
                          ? <><span className="h-4 w-4 animate-spin rounded-full border-2 border-white/30 border-t-white" /> Checking…</>
                          : <>Continue <ArrowRight className="h-4 w-4" /></>)
                      : step === "mfa"
                        ? (mfaVerify.isPending
                            ? <><span className="h-4 w-4 animate-spin rounded-full border-2 border-white/30 border-t-white" /> Verifying…</>
                            : <>Verify code <ArrowRight className="h-4 w-4" /></>)
                        : (login.isPending
                            ? <><span className="h-4 w-4 animate-spin rounded-full border-2 border-white/30 border-t-white" /> Signing in…</>
                            : <>Sign in <ArrowRight className="h-4 w-4" /></>)}
                  </button>
                )}
              </form>

          </div>
          <details className="login-access-help">
            <summary>Need help accessing your workspace?</summary>
            <p>Use the organization code and credentials issued by your administrator. Your account determines the workspace and actions you can access.</p>
            <dl>{ACCESS_GUIDANCE.map(account => <div key={account.title}><dt>{account.title}</dt><dd>{account.note}</dd></div>)}</dl>
            <p>Missing your code or need access? Contact your organization’s OpsTrax administrator.</p>
          </details>
          <footer className="login-access-footer">
            <a href="https://www.kodekinetics.com" target="_blank" rel="noopener noreferrer">Kode Kinetics</a>
            <a href="mailto:info@kodekinetics.com">Contact support</a>
          </footer>
        </div>
      </section>
    </main>
  );
}
