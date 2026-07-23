// Single source of truth for the browser session storage key. EVERY reader/writer of the stored
// session (useAuth, apiClient's request interceptor + logout cleanup, the node client, the SSO
// callback, telematicsService) MUST import from here. Hardcoding the key in each place is what let
// a version bump desync them: the session was written under one key while the token was read from
// another, so no Authorization header was attached and every authenticated call 401'd.
//
// Bump SESSION_STORAGE_KEY (and push the old value onto RETIRED_SESSION_KEYS) only when the session
// SHAPE or permission semantics change and stale sessions must be force-invalidated. Because it is
// centralized, a bump updates all sites at once.
export const SESSION_STORAGE_KEY = "opstrax.session.v3";

// Older keys: cleared on load so a stale session can't linger, and read as a last-resort fallback so
// an in-flight session mid-deploy isn't dropped. Newest-first is irrelevant here; these are legacy.
export const RETIRED_SESSION_KEYS = ["opstrax.session.v2", "opstrax.session"] as const;

// All keys we may still find a token/session under, current first.
export const SESSION_KEY_LOOKUP = [SESSION_STORAGE_KEY, ...RETIRED_SESSION_KEYS] as const;

/** Read the raw stored session string from the current key, falling back to retired keys. */
export function readRawSession(): string | null {
  for (const k of SESSION_KEY_LOOKUP) {
    const v = (typeof window !== "undefined" ? window.localStorage.getItem(k) : null);
    if (v) return v;
  }
  return null;
}

/** Remove the session under every known key (current + retired). */
export function clearAllSessionKeys(): void {
  if (typeof window === "undefined") return;
  for (const k of SESSION_KEY_LOOKUP) window.localStorage.removeItem(k);
}
