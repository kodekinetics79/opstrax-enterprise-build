import { useCallback, useEffect, useRef, useState } from "react";
import { useMutation } from "@tanstack/react-query";
import axios from "axios";
import { AlertCircle, ArrowRight, Building2, ClipboardCheck, Lock, Route, ShieldCheck, Wrench } from "lucide-react";
import { flushSync } from "react-dom";
import { Link, useNavigate } from "react-router";
import { getLandingRouteForSession } from "@/auth/sessionRouting";
import { useAuth } from "@/hooks/useAuth";
import { authApi, isMfaChallenge, type MfaChallenge, type SsoConnection } from "@/services/authApi";
import { API_BASE_URL } from "@/services/apiClient";
import { OpsTraxLogo } from "@/components/OpsTraxLogo";

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

/* ── Pointer-driven parallax tilt for the 3D scene ──────────────────────────
   Rotates the scene element a few degrees toward the pointer. Transform-only,
   rAF-throttled with lerp smoothing, passive listeners, and fully disabled
   under prefers-reduced-motion. */
function usePointerTilt(maxTiltX = 2.4, maxTiltY = 3.2) {
  const panelRef = useRef<HTMLDivElement>(null);
  const sceneRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const panel = panelRef.current;
    const scene = sceneRef.current;
    if (!panel || !scene) return;
    if (window.matchMedia("(prefers-reduced-motion: reduce)").matches) return;

    let raf = 0;
    let running = false;
    let rect: DOMRect | null = null;
    let targetX = 0;
    let targetY = 0;
    let curX = 0;
    let curY = 0;

    const step = () => {
      curX += (targetX - curX) * 0.08;
      curY += (targetY - curY) * 0.08;
      scene.style.transform = `rotateX(${curX.toFixed(3)}deg) rotateY(${curY.toFixed(3)}deg)`;
      if (Math.abs(targetX - curX) > 0.005 || Math.abs(targetY - curY) > 0.005) {
        raf = requestAnimationFrame(step);
      } else {
        running = false;
      }
    };
    const kick = () => {
      if (!running) {
        running = true;
        raf = requestAnimationFrame(step);
      }
    };

    const onEnter = () => { rect = panel.getBoundingClientRect(); };
    const onMove = (e: PointerEvent) => {
      if (!rect) rect = panel.getBoundingClientRect();
      const nx = ((e.clientX - rect.left) / rect.width) * 2 - 1;  // -1..1
      const ny = ((e.clientY - rect.top) / rect.height) * 2 - 1;  // -1..1
      targetY = nx * maxTiltY;
      targetX = -ny * maxTiltX;
      kick();
    };
    const onLeave = () => { targetX = 0; targetY = 0; kick(); };
    const onResize = () => { rect = null; };

    panel.addEventListener("pointerenter", onEnter, { passive: true });
    panel.addEventListener("pointermove", onMove, { passive: true });
    panel.addEventListener("pointerleave", onLeave, { passive: true });
    window.addEventListener("resize", onResize, { passive: true });
    return () => {
      panel.removeEventListener("pointerenter", onEnter);
      panel.removeEventListener("pointermove", onMove);
      panel.removeEventListener("pointerleave", onLeave);
      window.removeEventListener("resize", onResize);
      cancelAnimationFrame(raf);
    };
  }, [maxTiltX, maxTiltY]);

  return { panelRef, sceneRef };
}

/* ── Telemetry particle canvas (decorative) ─────────────────────────────── */
function TelemetryCanvas() {
  const ref = useRef<HTMLCanvasElement>(null);

  useEffect(() => {
    const canvas = ref.current;
    if (!canvas) return;
    const ctx = canvas.getContext("2d");
    if (!ctx) return;

    const W = canvas.offsetWidth;
    const H = canvas.offsetHeight;
    if (!W || !H) return; // panel hidden (below lg) — skip the loop entirely

    const dpr = Math.min(window.devicePixelRatio || 1, 2);
    canvas.width = W * dpr;
    canvas.height = H * dpr;
    ctx.scale(dpr, dpr);

    let raf = 0;

    type Particle = { x: number; y: number; speed: number; size: number; opacity: number; color: string; burst: boolean };

    const COLORS = ["#2dd4bf", "#7dd3fc", "#a5f3fc", "#ffffff", "#2dd4bf", "#2dd4bf"];
    const particles: Particle[] = Array.from({ length: 55 }, () => ({
      x:       Math.random() * W,
      y:       Math.random() * H,
      speed:   0.3 + Math.random() * 0.9,
      size:    Math.random() < 0.15 ? 2.2 : 1.1,
      opacity: 0.05 + Math.random() * 0.4,
      color:   COLORS[Math.floor(Math.random() * COLORS.length)],
      burst:   Math.random() < 0.08,
    }));

    function drawFrame() {
      ctx!.clearRect(0, 0, W, H);
      for (const p of particles) {
        ctx!.beginPath();
        ctx!.arc(p.x, p.y, p.size, 0, Math.PI * 2);
        ctx!.fillStyle = p.color;
        ctx!.globalAlpha = p.opacity;
        ctx!.fill();

        // occasional connecting lines between nearby particles
        if (p.burst) {
          for (const q of particles) {
            const d = Math.hypot(p.x - q.x, p.y - q.y);
            if (d < 60 && d > 0) {
              ctx!.beginPath();
              ctx!.moveTo(p.x, p.y);
              ctx!.lineTo(q.x, q.y);
              ctx!.strokeStyle = "#2dd4bf";
              ctx!.globalAlpha = 0.04 * (1 - d / 60);
              ctx!.lineWidth = 0.5;
              ctx!.stroke();
            }
          }
        }

        p.y -= p.speed;
        if (p.y < -4) {
          p.y = H + 4;
          p.x = Math.random() * W;
          p.opacity = 0.05 + Math.random() * 0.35;
        }
      }
      ctx!.globalAlpha = 1;
    }

    // Reduced motion: paint one static frame instead of animating.
    if (window.matchMedia("(prefers-reduced-motion: reduce)").matches) {
      drawFrame();
      return;
    }

    function loop() {
      drawFrame();
      raf = requestAnimationFrame(loop);
    }
    loop();
    return () => cancelAnimationFrame(raf);
  }, []);

  return (
    <canvas
      ref={ref}
      className="absolute inset-0 h-full w-full"
      style={{ opacity: 0.55 }}
      aria-hidden="true"
    />
  );
}

/* ── Floating vehicle status cards (ambient product illustration) ───────── */
const vehicleEvents = [
  { code: "LIVE FLEET", status: "Connected", detail: "Tenant-isolated operational visibility", color: "#2dd4bf", dot: "●" },
  { code: "DISPATCH", status: "Coordinated", detail: "Assignments, routes and exceptions", color: "#4ade80", dot: "✓" },
  { code: "SAFETY", status: "Monitored", detail: "Policy-driven alerts and coaching", color: "#fbbf24", dot: "●" },
  { code: "MAINTENANCE", status: "Planned", detail: "Readiness, diagnostics and service", color: "#7dd3fc", dot: "●" },
];

function FloatingStatusCards() {
  return (
    <div className="login-card-rail absolute right-10 top-1/2 flex flex-col gap-3" aria-hidden="true">
      {vehicleEvents.map((v, i) => (
        <div
          key={v.code}
          className="login-float-wrap"
          style={{ "--z": `${22 + i * 16}px`, animationDelay: `${0.8 + i * 0.3}s` } as React.CSSProperties}
        >
          <div
            className="login-float-card rounded-xl border px-3.5 py-2.5"
            style={{
              animationDelay: `${i * 1.3}s`,
              borderColor: `${v.color}30`,
              background: "rgba(12,21,38,0.78)",
              backdropFilter: "blur(8px)",
              minWidth: 188,
              boxShadow: `0 14px 34px -14px ${v.color}40, 0 3px 10px rgba(2,8,20,.5)`,
            }}
          >
            <div className="flex items-center justify-between gap-4">
              <span className="text-[11px] font-bold text-white/80">{v.code}</span>
              <span className="text-[10px] font-semibold" style={{ color: v.color }}>
                {v.dot} {v.status}
              </span>
            </div>
            <p className="mt-0.5 text-[10px] text-slate-500">{v.detail}</p>
          </div>
        </div>
      ))}
    </div>
  );
}

/* ── Event ticker (decorative flavor) ───────────────────────────────────── */
const tickerItems = [
  "LIVE OPERATIONS · GPS visibility and exception awareness",
  "DISPATCH · Assignment, route and delivery coordination",
  "SAFETY · Evidence, coaching and compliance workflows",
  "MAINTENANCE · Diagnostics, readiness and service planning",
];

function LiveTicker() {
  const text = tickerItems.join("   ·   ");
  return (
    <div className="relative overflow-hidden border-t border-white/6 py-2" aria-hidden="true">
      <div className="flex gap-0 whitespace-nowrap login-ticker-track">
        {[text, text].map((t, i) => (
          <span key={i} className="inline-block shrink-0 pr-16 text-[10px] font-medium text-slate-500">
            {t}
          </span>
        ))}
      </div>
    </div>
  );
}

/* ── Route SVG with animated vehicles ──────────────────────────────────── */
function RouteMap() {
  return (
    <svg
      viewBox="0 0 600 480"
      fill="none"
      className="absolute inset-0 h-full w-full"
      preserveAspectRatio="xMidYMid slice"
      aria-hidden="true"
    >
      <defs>
        {/* Glow filter for vehicle dots */}
        <filter id="vglow" x="-80%" y="-80%" width="260%" height="260%">
          <feGaussianBlur stdDeviation="4" result="blur" />
          <feMerge>
            <feMergeNode in="blur" />
            <feMergeNode in="SourceGraphic" />
          </feMerge>
        </filter>
        <filter id="sglow" x="-60%" y="-60%" width="220%" height="220%">
          <feGaussianBlur stdDeviation="7" result="blur" />
          <feMerge>
            <feMergeNode in="blur" />
            <feMergeNode in="SourceGraphic" />
          </feMerge>
        </filter>

        {/* Scanning-line gradient */}
        <linearGradient id="scanGrad" x1="0" y1="0" x2="1" y2="0">
          <stop offset="0%"   stopColor="transparent" />
          <stop offset="35%"  stopColor="#2dd4bf" stopOpacity="0.25" />
          <stop offset="50%"  stopColor="#2dd4bf" stopOpacity="0.55" />
          <stop offset="65%"  stopColor="#2dd4bf" stopOpacity="0.25" />
          <stop offset="100%" stopColor="transparent" />
        </linearGradient>

        {/* Route paths (referenced by animateMotion) */}
        <path id="r1" d="M 20 400 Q 160 300 320 210 L 510 120" />
        <path id="r2" d="M 50 450 Q 220 370 390 330 L 570 270" />
        <path id="r3" d="M 10 190 Q 150 145 300 160 Q 430 175 550 115" />
      </defs>

      {/* Visible route strokes */}
      <use href="#r1" stroke="#2dd4bf"  strokeWidth="1.4" strokeDasharray="7 5"  opacity="0.28" />
      <use href="#r2" stroke="#7dd3fc"  strokeWidth="1"   strokeDasharray="5 7"  opacity="0.2" />
      <use href="#r3" stroke="#a5f3fc"  strokeWidth="0.8"                        opacity="0.12" />
      {/* fourth subtle route */}
      <path d="M 120 460 Q 280 400 440 390 L 590 360" stroke="#64748b" strokeWidth="0.7" strokeDasharray="3 9" opacity="0.15" />

      {/* ── Moving vehicle 1 (teal) — primary route ── */}
      {/* Ghost trail dot 2 */}
      <circle r="1.5" fill="#2dd4bf">
        <animateMotion dur="9s" begin="-1.4s" repeatCount="indefinite">
          <mpath href="#r1" />
        </animateMotion>
        <animate attributeName="opacity" values="0;0.22;0.22;0" dur="9s" begin="-1.4s" repeatCount="indefinite" />
      </circle>
      {/* Ghost trail dot 1 */}
      <circle r="2.5" fill="#2dd4bf">
        <animateMotion dur="9s" begin="-0.7s" repeatCount="indefinite">
          <mpath href="#r1" />
        </animateMotion>
        <animate attributeName="opacity" values="0;0.45;0.45;0" dur="9s" begin="-0.7s" repeatCount="indefinite" />
      </circle>
      {/* Main vehicle dot with glow */}
      <circle r="5" filter="url(#vglow)" fill="#2dd4bf">
        <animateMotion dur="9s" repeatCount="indefinite">
          <mpath href="#r1" />
        </animateMotion>
        <animate attributeName="opacity" values="0;1;1;0.8;0" dur="9s" repeatCount="indefinite" />
        <animate attributeName="r" values="5;6;5" dur="1.8s" repeatCount="indefinite" />
      </circle>

      {/* ── Moving vehicle 2 (sky blue) — second route ── */}
      <circle r="1.5" fill="#7dd3fc">
        <animateMotion dur="13s" begin="-7.5s" repeatCount="indefinite">
          <mpath href="#r2" />
        </animateMotion>
        <animate attributeName="opacity" values="0;0.3;0.3;0" dur="13s" begin="-7.5s" repeatCount="indefinite" />
      </circle>
      <circle r="4" filter="url(#vglow)" fill="#7dd3fc">
        <animateMotion dur="13s" begin="-6s" repeatCount="indefinite">
          <mpath href="#r2" />
        </animateMotion>
        <animate attributeName="opacity" values="0;0.85;0.85;0" dur="13s" begin="-6s" repeatCount="indefinite" />
      </circle>

      {/* ── Moving vehicle 3 (cyan soft) — upper route ── */}
      <circle r="3" filter="url(#vglow)" fill="#a5f3fc">
        <animateMotion dur="16s" begin="-9s" repeatCount="indefinite">
          <mpath href="#r3" />
        </animateMotion>
        <animate attributeName="opacity" values="0;0.6;0.6;0" dur="16s" begin="-9s" repeatCount="indefinite" />
      </circle>

      {/* ── Static endpoint nodes ── */}
      {/* Primary terminus — teal */}
      <g filter="url(#sglow)">
        <circle cx="510" cy="120" r="6" fill="#2dd4bf" opacity="0.95" />
      </g>
      <circle cx="510" cy="120" r="13" stroke="#2dd4bf" strokeWidth="1"   fill="none" opacity="0" className="login-dot-ping" />
      <circle cx="510" cy="120" r="20" stroke="#2dd4bf" strokeWidth="0.5" fill="none" opacity="0" className="login-dot-ping-outer" />

      {/* Secondary terminus */}
      <circle cx="570" cy="270" r="4.5" fill="#7dd3fc" opacity="0.8" filter="url(#vglow)" />
      <circle cx="570" cy="270" r="10"  stroke="#7dd3fc" strokeWidth="0.8" fill="none" opacity="0" className="login-dot-ping-2" />

      {/* Waypoint labels */}
      <circle cx="20" cy="400" r="3" fill="#2dd4bf" opacity="0.5" />
      <text x="28" y="404" fill="#2dd4bf" fontSize="8" opacity="0.45" fontFamily="Inter, sans-serif">Origin</text>

      <circle cx="50" cy="450" r="2.5" fill="#7dd3fc" opacity="0.4" />
      <text x="58" y="454" fill="#7dd3fc" fontSize="8" opacity="0.35" fontFamily="Inter, sans-serif">Hub</text>

      <circle cx="10" cy="190" r="2.5" fill="#a5f3fc" opacity="0.35" />
      <text x="18" y="194" fill="#a5f3fc" fontSize="7" opacity="0.3" fontFamily="Inter, sans-serif">Waypoint</text>

      {/* ── Horizontal scanning sweep ── */}
      <rect x="0" y="0" width="600" height="2" fill="url(#scanGrad)" className="login-scan-line" />
    </svg>
  );
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

/* ── Trust signals — all truthful of the platform (tenant isolation, TLS, the
   SSO route this page now supports). No fabricated certs, seals, or metrics. ── */
const TRUST_SIGNALS = [
  { icon: ShieldCheck, label: "Tenant-isolated access" },
  { icon: Lock,        label: "Encrypted in transit" },
  { icon: Building2,   label: "Enterprise SSO (OIDC)" },
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
  const { panelRef, sceneRef } = usePointerTilt();
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
    <div className="flex min-h-screen">

      {/* ── LEFT — 3D command-center brand panel ───────────── */}
      <div
        ref={panelRef}
        className="login-brand-panel login-panel-enter login-persp relative hidden lg:flex lg:w-[58%] xl:w-[62%] flex-col overflow-hidden"
      >
        {/* 3D decorative scene — layers on separate translateZ planes,
            rotated toward the pointer. Purely ambient product illustration. */}
        <div ref={sceneRef} className="login-scene pointer-events-none absolute inset-0" aria-hidden="true">

          {/* Layer: dot-grid texture (deepest) */}
          <div
            className="login-layer-grid absolute inset-0"
            style={{ backgroundImage: "radial-gradient(circle,rgba(255,255,255,0.05) 1px,transparent 1px)", backgroundSize: "32px 32px", opacity: 0.45 }}
          />

          {/* Layer: ambient breathing glows */}
          <div className="login-layer-glow absolute inset-0">
            <div className="login-glow-1 absolute -left-32 top-1/4 h-80 w-80 rounded-full bg-teal-500/12 blur-[96px]" />
            <div className="login-glow-2 absolute right-0 bottom-1/3 h-64 w-64 rounded-full bg-sky-500/8 blur-[80px]"  />
            <div className="login-glow-1 absolute left-1/3 bottom-0  h-48 w-48 rounded-full bg-teal-400/7 blur-[60px]"  style={{ animationDelay: "2s" }} />
          </div>

          {/* Layer: telemetry particle stream */}
          <div className="login-layer-canvas absolute inset-0">
            <TelemetryCanvas />
          </div>

          {/* Layer: route map — tilted plane, reads like a holo map table */}
          <div className="login-layer-map absolute inset-0">
            <RouteMap />
          </div>

          {/* Slow diagonal light sweep across the scene */}
          <div className="login-light-sweep" />

          {/* Layer: floating vehicle status cards (nearest, angled rail) */}
          <FloatingStatusCards />
        </div>

        {/* Content layer — flat, crisp text above the 3D scene */}
        <div className="relative z-10 flex h-full flex-col px-12 py-10 xl:px-16">

          {/* Logo */}
          <div className="flex items-center gap-3">
            <OpsTraxLogo size={40} />
            <span className="text-xl font-semibold tracking-tight text-white">OpsTrax</span>
          </div>

          {/* Hero */}
          <div className="flex flex-1 flex-col justify-center">
            <p className="text-[11px] font-semibold uppercase tracking-widest text-teal-400">Fleet Management Platform</p>
            <h1 className="login-hero-title mt-4 text-5xl font-bold leading-[1.08] tracking-tight text-white xl:text-6xl">
              Fleet intelligence,
              <br />
              <span className="text-teal-400">live.</span>
            </h1>
            <p className="mt-5 max-w-sm text-base leading-7 text-slate-400">
              One command center for the entire operation — from job assignment to proof of delivery.
            </p>

            {/* Product pillars */}
            <div className="mt-10 flex max-w-md flex-wrap gap-2.5">
              {PLATFORM_PILLARS.map(({ icon: Icon, label }, i) => (
                <div
                  key={label}
                  className="login-pillar flex items-center gap-2 rounded-full border border-white/10 bg-white/[0.04] px-3.5 py-2 backdrop-blur-sm"
                  style={{ animationDelay: `${1.1 + i * 0.15}s` }}
                >
                  <Icon className="h-3.5 w-3.5 text-teal-400" />
                  <span className="text-xs font-semibold tracking-wide text-slate-200">{label}</span>
                </div>
              ))}
            </div>
          </div>

          {/* Decorative event ticker */}
          <div className="mb-3">
            <LiveTicker />
          </div>

          {/* Trust strip — honest capability statements, shared clay material */}
          <div className="mb-4 flex flex-wrap items-center gap-2">
            {TRUST_SIGNALS.map(({ icon: Icon, label }) => (
              <span key={label} className="login2-chip-dark">
                <Icon className="h-3.5 w-3.5 text-teal-400" aria-hidden="true" />
                {label}
              </span>
            ))}
          </div>

          {/* Bottom bar */}
          <div className="flex items-end justify-between gap-4">
            <p className="text-xs text-slate-600">Multi-tenant fleet operations platform</p>
            <div className="text-right">
              <p className="text-[11px] text-slate-600">Engineered by</p>
              <a href="https://www.kodekinetics.com" target="_blank" rel="noopener noreferrer"
                className="mt-0.5 inline-flex items-center gap-1.5 text-xs font-semibold text-slate-400 transition hover:text-teal-400">
                <span className="inline-flex h-4 w-4 items-center justify-center rounded bg-teal-500/20 text-[9px] font-bold text-teal-400">KK</span>
                Kode Kinetics
              </a>
            </div>
          </div>
        </div>
      </div>

      {/* ── RIGHT — sign-in form ───────────────────────────── */}
      <div className="relative flex flex-1 flex-col items-center justify-center overflow-hidden bg-gradient-to-br from-white via-slate-50 to-teal-50/40 px-8 py-12 lg:px-12">

        {/* Perspective floor grid + soft glow (decorative) */}
        <div className="login-floor" aria-hidden="true" />
        <div className="pointer-events-none absolute -top-28 right-[-10%] h-72 w-72 rounded-full bg-teal-200/35 blur-[110px]" aria-hidden="true" />

        {/* Mobile logo */}
        <div className="relative z-10 mb-10 flex items-center gap-2.5 lg:hidden">
          <OpsTraxLogo size={32} />
          <span className="text-lg font-bold text-slate-900">OpsTrax</span>
        </div>

        <div className="login2 login-form-enter relative z-10 flex w-full max-w-[400px] flex-col gap-6">

          {/* 3D stage — ghost plates float behind the clay card */}
          <div className="login-card-stage relative">
            <div className="login-card-plate login-card-plate-2" aria-hidden="true" />
            <div className="login-card-plate login-card-plate-1" aria-hidden="true" />

            <div className="login2-card relative">
              <div className="mb-6">
                <p className="text-[11px] font-semibold uppercase tracking-[0.22em] text-teal-600">Secure access</p>
                <h2 className="mt-2 text-2xl font-bold text-slate-950">Sign in</h2>
                <p className="mt-1.5 text-sm leading-6 text-slate-500">
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
                        <span className="block truncate text-sm font-medium text-slate-700">{email.trim()}</span>
                        <span className="block truncate text-[11px] text-slate-500">Organization {companyCode.trim()}</span>
                      </span>
                    </span>
                    <button type="button" onClick={editEmail}
                      className="shrink-0 text-xs font-semibold text-teal-700 transition hover:text-teal-600 focus-visible:outline-2 focus-visible:outline-teal-600">
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

              {/* Trust row — truthful capability statements, no fabricated seals */}
              <div className="mt-6 flex flex-wrap justify-center gap-2">
                {TRUST_SIGNALS.map(({ icon: Icon, label }) => (
                  <span key={label} className="login2-chip">
                    <Icon className="h-3.5 w-3.5 text-teal-600" aria-hidden="true" />
                    {label}
                  </span>
                ))}
              </div>
            </div>
          </div>

          {/* Role guidance — clay tiles */}
          <div className="rounded-[20px] border border-slate-200/80 bg-white/60 p-4 shadow-[0_12px_28px_rgba(15,23,42,.06)] backdrop-blur-md">
            <p className="text-[10px] font-black uppercase tracking-[0.24em] text-slate-400">Workspace roles</p>
            <p className="mt-1 text-sm font-semibold text-slate-800">Sign in with the credentials issued by your organization</p>
            <div className="mt-4 grid gap-3 sm:grid-cols-2">
              {ACCESS_GUIDANCE.map((account) => (
                <div key={account.title} className="login2-role flex flex-col items-start gap-1.5 text-left">
                  <span className="text-sm font-bold text-slate-900">{account.title}</span>
                  <span className="text-[11px] leading-5 text-slate-500">{account.note}</span>
                </div>
              ))}
            </div>
          </div>

          {/* Footer */}
          <div className="mt-8 text-center">
            <p className="text-[11px] text-slate-300">
              Built by{" "}
              <a href="https://www.kodekinetics.com" target="_blank" rel="noopener noreferrer"
                className="font-medium text-slate-500 transition hover:text-teal-500">Kode Kinetics</a>
              {" · "}
              <a href="mailto:info@kodekinetics.com" className="text-slate-500 transition hover:text-teal-500">info@kodekinetics.com</a>
            </p>
          </div>
        </div>
      </div>
    </div>
  );
}
