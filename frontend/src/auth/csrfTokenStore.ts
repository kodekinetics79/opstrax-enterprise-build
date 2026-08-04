// Request-scoped browser CSRF state shared by auth and the API interceptors.
// Keep this independent of React hooks to avoid an auth-hook import cycle.
let csrfToken: string | null = null;

export function setGlobalCsrfToken(token: string) {
  csrfToken = token;
}

// Persisted sessions are only a bootstrap source. The cookie can be renewed by
// the server independently (for example after its eight-hour lifetime), so a
// stored token must never overwrite a newer response-header token on every
// request. Hydrate once when the in-memory store is empty; subsequent server
// responses remain authoritative.
export function hydrateGlobalCsrfToken(token: string) {
  if (!csrfToken) csrfToken = token;
}

export function clearGlobalCsrfToken() {
  csrfToken = null;
}

export function getGlobalCsrfToken(): string | null {
  return csrfToken;
}
