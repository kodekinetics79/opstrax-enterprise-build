import { useEffect, useRef, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { AlertCircle } from "lucide-react";
import { getLandingRouteForSession } from "@/auth/sessionRouting";
import { useAuth } from "@/hooks/useAuth";
import { authApi } from "@/services/authApi";
import { setGlobalCsrfToken } from "@/hooks/useCsrf";
import { OpsTraxLogo } from "@/components/OpsTraxLogo";

const SESSION_KEY = "opstrax.session.v2";
const SESSION_TTL_MS = 8 * 60 * 60 * 1000;

/**
 * Landing route for the OIDC round-trip. The API redirects here with the freshly
 * minted session token + CSRF in the URL fragment (never a query string, so it is
 * not sent to servers or written to access logs). We store the token, hydrate the
 * full session via /api/auth/me, then route to the user's workspace.
 */
export function SsoCallbackPage() {
  const { setSession } = useAuth();
  const navigate = useNavigate();
  const [error, setError] = useState("");
  const ran = useRef(false);

  useEffect(() => {
    if (ran.current) return;
    ran.current = true;

    const hash = new URLSearchParams(window.location.hash.replace(/^#/, ""));
    const token = hash.get("token");
    const csrf = hash.get("csrf") ?? "";
    // Strip the fragment from the address bar immediately.
    window.history.replaceState(null, "", window.location.pathname);

    if (!token) {
      navigate("/login?sso_error=sso_no_token", { replace: true });
      return;
    }

    (async () => {
      try {
        // Seed the bearer so the apiClient interceptor authenticates the hydrate call.
        localStorage.setItem(
          SESSION_KEY,
          JSON.stringify({ session: { token, csrfToken: csrf }, expiresAt: Date.now() + SESSION_TTL_MS }),
        );
        setGlobalCsrfToken(csrf);
        const me = await authApi.me();
        const session = { ...me, token, csrfToken: csrf || me.csrfToken };
        setSession(session);
        navigate(getLandingRouteForSession(session), { replace: true });
      } catch {
        localStorage.removeItem(SESSION_KEY);
        setError("We couldn’t complete single sign-on. Please return to sign in and try again.");
      }
    })();
  }, [navigate, setSession]);

  return (
    <main className="flex min-h-screen items-center justify-center bg-gradient-to-br from-slate-950 via-slate-900 to-teal-950 px-5 py-12">
      <div className="w-full max-w-md rounded-[26px] border border-white/15 bg-white/95 p-8 text-center shadow-2xl">
        <div className="mb-6 flex items-center justify-center gap-3">
          <OpsTraxLogo size={38} />
          <span className="text-lg font-bold text-slate-900">OpsTrax</span>
        </div>
        {error ? (
          <>
            <div role="alert" className="flex items-center justify-center gap-2 rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
              <AlertCircle className="h-4 w-4 shrink-0" />
              {error}
            </div>
            <Link to="/login" className="mt-5 inline-block text-sm font-semibold text-teal-700 hover:text-teal-600">
              Return to sign in
            </Link>
          </>
        ) : (
          <div className="flex flex-col items-center gap-3 py-4">
            <span className="h-8 w-8 animate-spin rounded-full border-2 border-teal-200 border-t-teal-600" />
            <p className="text-sm font-medium text-slate-600">Completing single sign-on…</p>
          </div>
        )}
      </div>
    </main>
  );
}
