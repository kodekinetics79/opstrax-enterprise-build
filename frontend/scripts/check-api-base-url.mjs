// Build-time guard for the API origin that gets COMPILED INTO the bundle.
//
// Why this exists: production once shipped a build pointing at a decommissioned
// Render service. That host still completed the TCP/TLS handshake (the platform
// edge answers for a retired service) but no application ever replied, so the
// browser had no reason to fail fast — every sign-in hung until the axios 30s
// timeout and surfaced as a generic "login failed". Nothing in the build, the
// deploy, or /health caught it, because the frontend build never asks whether the
// origin it is baking in is actually alive.
//
// It also prints WHICH variable won. The precedence below is duplicated from
// src/services/apiClient.ts, and VITE_API_BASE_URL set in the Vercel dashboard
// silently overrides the VITE_PLATFORM_API_BASE_URL committed in vercel.json —
// so editing vercel.json to repoint the API appears to do nothing at all.
//
// Escape hatch: set SKIP_API_BASE_URL_CHECK=1 to skip only the reachability
// probe (the "is one of these set" check always runs). Needed so a frontend fix
// can still deploy while the API is down.

// Precedence MUST match src/services/apiClient.ts.
const CANDIDATES = [
  "VITE_API_BASE_URL",
  "VITE_DOTNET_API_URL",
  "VITE_PLATFORM_API_BASE_URL",
];

const PROBE_PATH = "/health/live";
const ATTEMPTS = 3;
const TIMEOUT_MS = 10_000;

const fail = (message) => {
  console.error(`\n  API base URL check FAILED\n\n  ${message}\n`);
  process.exit(1);
};

const selected = CANDIDATES.find((name) => (process.env[name] ?? "").trim());

if (!selected) {
  fail(
    `None of ${CANDIDATES.join(" / ")} is set.\n  ` +
    `A production build with no API origin falls back to the deploy's own origin,\n  ` +
    `which serves the SPA rather than the API — every request would 404.`
  );
}

const baseUrl = process.env[selected].trim().replace(/\/+$/, "");

const shadowed = CANDIDATES.filter((name) => name !== selected && (process.env[name] ?? "").trim());
console.log(`  API base URL: ${baseUrl}  (from ${selected})`);
if (shadowed.length > 0) {
  console.log(`  note: ${shadowed.join(", ")} also set but IGNORED — ${selected} takes precedence.`);
}

let parsed;
try {
  parsed = new URL(baseUrl);
} catch {
  fail(`${selected} is not a valid absolute URL: ${baseUrl}`);
}
if (parsed.protocol !== "https:" && parsed.hostname !== "localhost") {
  fail(`${selected} must be https for a deployed build (got ${parsed.protocol}//${parsed.hostname}).`);
}

if ((process.env.SKIP_API_BASE_URL_CHECK ?? "") === "1") {
  console.log("  reachability probe SKIPPED (SKIP_API_BASE_URL_CHECK=1)");
  process.exit(0);
}

// A dead origin's failure mode is a HANG, not a refusal — so a timeout here is a
// real signal, not flakiness, and is retried only to absorb genuine transients.
let lastFailure = "";
for (let attempt = 1; attempt <= ATTEMPTS; attempt++) {
  try {
    const response = await fetch(`${baseUrl}${PROBE_PATH}`, {
      signal: AbortSignal.timeout(TIMEOUT_MS),
      headers: { Accept: "application/json" },
    });
    if (response.ok) {
      console.log(`  reachability: OK (${response.status} from ${PROBE_PATH})\n`);
      process.exit(0);
    }
    lastFailure = `${PROBE_PATH} returned HTTP ${response.status}`;
  } catch (error) {
    lastFailure = error.name === "TimeoutError"
      ? `${PROBE_PATH} did not respond within ${TIMEOUT_MS}ms (connection may open but never answer — the signature of a retired service)`
      : `${error.name}: ${error.message}`;
  }
  if (attempt < ATTEMPTS) await new Promise((resolve) => setTimeout(resolve, 1000 * attempt));
}

fail(
  `${baseUrl} is not serving the OpsTrax API.\n  ` +
  `Last attempt: ${lastFailure}\n\n  ` +
  `Fix ${selected} (Vercel dashboard env vars override vercel.json), or set\n  ` +
  `SKIP_API_BASE_URL_CHECK=1 to deploy anyway while the API is down.`
);
