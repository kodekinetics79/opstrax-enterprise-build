// Request-scoped browser CSRF state shared by auth and the API interceptors.
// Keep this independent of React hooks to avoid an auth-hook import cycle.
let csrfToken: string | null = null;

export function setGlobalCsrfToken(token: string) {
  csrfToken = token;
}

export function clearGlobalCsrfToken() {
  csrfToken = null;
}

export function getGlobalCsrfToken(): string | null {
  return csrfToken;
}
