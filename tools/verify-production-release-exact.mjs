#!/usr/bin/env node

const required = (name) => {
  const value = process.env[name]?.trim();
  if (!value) throw new Error(`${name} is required`);
  return value;
};

const candidateSha = required("CANDIDATE_SHA");
const frontendUrl = new URL(required("FRONTEND_URL"));
const apiHealthUrl = new URL(required("API_HEALTH_URL"));

if (!/^[0-9a-f]{40}$/.test(candidateSha)) {
  throw new Error("CANDIDATE_SHA must be a full lowercase SHA");
}
for (const [name, url] of [["FRONTEND_URL", frontendUrl], ["API_HEALTH_URL", apiHealthUrl]]) {
  if (url.protocol !== "https:") throw new Error(`${name} must use HTTPS`);
}
if (apiHealthUrl.pathname !== "/health/ready") {
  throw new Error("API_HEALTH_URL must identify the /health/ready endpoint");
}

const manifestUrl = new URL("/deployment.json", frontendUrl);
const deadline = Date.now() + 10 * 60 * 1000;
let lastFailure = "no response";

while (Date.now() < deadline) {
  try {
    const [manifestResponse, healthResponse] = await Promise.all([
      fetch(manifestUrl, {
        headers: { Accept: "application/json", "Cache-Control": "no-cache" },
      }),
      fetch(apiHealthUrl, {
        headers: { Accept: "application/json", "Cache-Control": "no-cache" },
      }),
    ]);
    const [manifest, health] = await Promise.all([
      manifestResponse.json().catch(() => null),
      healthResponse.json().catch(() => null),
    ]);

    const frontendMatches = manifestResponse.ok
      && manifest?.frontendSha === candidateSha
      && manifest?.frontendEnvironment === "production";
    const apiMatches = healthResponse.ok
      && health?.status === "ready"
      && health?.version === candidateSha;

    if (frontendMatches && apiMatches) {
      console.log(`Verified customer POC frontend and API at exact SHA ${candidateSha}.`);
      process.exit(0);
    }
    lastFailure = [
      `frontend HTTP ${manifestResponse.status}, SHA=${manifest?.frontendSha ?? "unknown"}, environment=${manifest?.frontendEnvironment ?? "unknown"}`,
      `API HTTP ${healthResponse.status}, status=${health?.status ?? "unknown"}, SHA=${health?.version ?? "unknown"}`,
    ].join("; ");
  } catch (error) {
    lastFailure = `${error.name}: ${error.message}`;
  }
  console.log(`Production parity pending: ${lastFailure}`);
  await new Promise((resolve) => setTimeout(resolve, 10_000));
}

throw new Error(`Customer POC exact-SHA verification timed out: ${lastFailure}`);
