#!/usr/bin/env node

const required = (name) => {
  const value = process.env[name]?.trim();
  if (!value) throw new Error(`${name} is required`);
  return value;
};

const candidateSha = required("CANDIDATE_SHA");
const apiKey = required("RENDER_API_KEY");
const serviceId = required("RENDER_SERVICE_ID");
const healthUrl = new URL(required("RENDER_HEALTH_URL"));

if (!/^[0-9a-f]{40}$/.test(candidateSha)) throw new Error("CANDIDATE_SHA must be a full lowercase SHA");
if (!/^srv-[a-z0-9]+$/.test(serviceId)) throw new Error("RENDER_SERVICE_ID is invalid");
if (healthUrl.protocol !== "https:" || healthUrl.pathname !== "/health/ready") {
  throw new Error("RENDER_HEALTH_URL must be an HTTPS /health/ready endpoint");
}

const renderRequest = async (path, init = {}) => {
  const response = await fetch(`https://api.render.com/v1${path}`, {
    ...init,
    headers: {
      Accept: "application/json",
      Authorization: `Bearer ${apiKey}`,
      ...(init.body ? { "Content-Type": "application/json" } : {}),
    },
  });
  const body = await response.json().catch(() => null);
  if (!response.ok) throw new Error(`Render API ${response.status}: ${body?.message ?? "request failed"}`);
  return body;
};

const deployment = await renderRequest(`/services/${serviceId}/deploys`, {
  method: "POST",
  body: JSON.stringify({ clearCache: "do_not_clear", commitId: candidateSha }),
});
const deploymentId = deployment?.id;
if (!/^dep-[a-z0-9]+$/.test(deploymentId ?? "")) throw new Error("Render did not return a deployment id");
console.log(`Triggered Render deployment ${deploymentId} for ${candidateSha}`);

const terminalFailures = new Set([
  "build_failed",
  "canceled",
  "deactivated",
  "pre_deploy_failed",
  "update_failed",
]);
const deadline = Date.now() + 40 * 60 * 1000;
let status = deployment.status;
while (Date.now() < deadline) {
  const current = await renderRequest(`/services/${serviceId}/deploys/${deploymentId}`);
  status = current?.status;
  console.log(`Render deployment ${deploymentId}: ${status ?? "unknown"}`);
  if (status === "live") break;
  if (terminalFailures.has(status)) throw new Error(`Render deployment failed with status ${status}`);
  await new Promise((resolve) => setTimeout(resolve, 15_000));
}
if (status !== "live") throw new Error("Timed out waiting for Render deployment to become live");

let health;
for (let attempt = 1; attempt <= 12; attempt += 1) {
  const response = await fetch(healthUrl, { headers: { Accept: "application/json" } });
  const body = await response.json().catch(() => null);
  if (response.ok && body?.status === "ready" && body?.version === candidateSha) {
    health = body;
    break;
  }
  console.log(`Readiness attempt ${attempt}: HTTP ${response.status}, status=${body?.status ?? "unknown"}, version=${body?.version ?? "unknown"}`);
  await new Promise((resolve) => setTimeout(resolve, 10_000));
}
if (!health) throw new Error("Render became live but exact-SHA readiness verification failed");
console.log(`Verified ready production API at exact SHA ${health.version}`);
