import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

const repository = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const read = (name) => fs.readFileSync(path.join(repository, name), "utf8");
const frontendFiles = () => fs.readdirSync(path.join(repository, "frontend/src"), { recursive: true })
  .filter((name) => typeof name === "string" && /\.(ts|tsx)$/.test(name))
  .map((name) => `frontend/src/${name}`);

test("production application code cannot import synthetic fleet records", () => {
  const offenders = frontendFiles()
    .filter((name) => !name.includes("/data/"))
    .filter((name) => /^\s*import\s+(?!type\b)[^;\n]*(developmentFleetSeedData|mockOperatingData)/m.test(read(name)));
  assert.deepEqual(offenders, [], `synthetic imports escaped the data quarantine: ${offenders.join(", ")}`);
});

test("runtime Live is fail-closed on API, database, worker and telemetry truth", () => {
  const policy = read("frontend/src/services/runtimeDiagnostics.ts");
  const shell = read("frontend/src/layouts/AppShell.tsx");
  for (const evidence of ["apiReady", "databaseReady", "databaseContractReady", "criticalWorkersFresh", "telemetryFresh"]) {
    assert.match(policy, new RegExp(`verifiedLive[^;]+${evidence}`), `Live policy omits ${evidence}`);
  }
  assert.match(policy, /apiClient\.get\("\/health\/ready"/, "browser runtime truth must use the public readiness contract");
  assert.doesNotMatch(policy, /apiClient\.get\("\/health\/deep"/, "browser runtime truth must not call protected operator health");
  assert.doesNotMatch(shell, />\s*Live\s*</, "global shell still renders an unconditional Live label");
  assert.match(shell, /runtimeState === "Live"/);
  assert.match(shell, /tenantIsExplicitlySynthetic \? "Demo Data"/);
});

test("runtime provenance is exact in operator diagnostics and speakable-only on tenant About", () => {
  const vite = read("frontend/vite.config.ts");
  const diagnostics = read("frontend/src/services/runtimeDiagnostics.ts");
  const about = read("frontend/src/pages/AboutPage.tsx");
  for (const marker of ["VERCEL_GIT_COMMIT_SHA", "VITE_DEPLOYMENT_SHA", "__OPSTRAX_FRONTEND_SHA__", "__OPSTRAX_FRONTEND_ENVIRONMENT__", "__OPSTRAX_API_BASE_URL__"]) {
    assert.ok(vite.includes(marker), `build provenance omits ${marker}`);
  }
  assert.ok(vite.indexOf("RENDER_GIT_COMMIT") < vite.indexOf("VITE_DEPLOYMENT_SHA"),
    "Render's immutable commit identity must override a stale manually pinned deployment SHA");
  for (const marker of ["frontendSha", "apiSha", "frontendEnvironment", "apiEnvironment", "apiBaseUrl"]) {
    assert.ok(diagnostics.includes(marker), `runtime diagnostics omit ${marker}`);
  }
  // Security review moved raw internals OFF the tenant-facing About page: it shows a
  // short speakable build reference only; exact SHAs/URLs stay in operator
  // diagnostics (runtimeDiagnostics + authenticated /health/deep). Pin both sides.
  assert.ok(about.includes("frontendBuild"), "About lost its build identity source");
  assert.ok(about.includes("buildRef"), "About lost the speakable build reference");
  for (const leaked of ["frontendSha", "apiSha", "apiEnvironment", "apiBaseUrl"]) {
    assert.ok(!about.includes(leaked), `tenant About leaks operator diagnostic ${leaked}`);
  }
});

test("target modules preserve API error and Retry states without demo substitution", () => {
  for (const name of [
    "frontend/src/pages/TelematicsCommandPage.tsx",
    "frontend/src/pages/IotDevicesPage.tsx",
    "frontend/src/pages/MaintenancePlanningPage.tsx",
    "frontend/src/pages/MaintenanceCommandPage.tsx",
  ]) {
    const source = read(name);
    assert.doesNotMatch(source, /developmentFleetSeedData|mockOperatingData/);
    assert.ok(/isError|error/.test(source), `${name} has no error state`);
    assert.ok(/refetch|Retry|onRetry/.test(source), `${name} has no retry path`);
  }
});

test("tenant identity cannot fall back to numeric tenant one", () => {
  const offenders = frontendFiles().filter((name) => {
    if (name.includes("/data/")) return false;
    const source = read(name);
    return /(tenantId|tenant_id|companyId|company_id)\s*(\?\?|\|\|)\s*1\b/.test(source);
  });
  assert.deepEqual(offenders, [], `numeric tenant fallback found: ${offenders.join(", ")}`);
});

test("maintenance totals use the displayed API rows and never relabel estimates as actuals", () => {
  const page = read("frontend/src/pages/MaintenancePlanningPage.tsx");
  assert.match(page, /totalCost = rows\.reduce/);
  assert.match(page, /totalHours = rows\.reduce/);
  assert.match(page, /Recorded Actual Cost/);
  assert.doesNotMatch(page, /actualCost[^\n]+estimatedCost/);
});

test("every exposed target module names an executable backend contract", () => {
  const service = read("frontend/src/services/telematicsService.ts");
  const maintenance = read("frontend/src/services/maintenanceApi.ts") + read("frontend/src/pages/MaintenancePlanningPage.tsx");
  const backend = read("backend-dotnet/Controllers/EndpointMappings.cs");
  for (const endpoint of [
    "/api/telemetry/devices",
    "/api/telemetry/positions",
    "/api/maintenance/fault-codes",
    "/api/maintenance/work-orders",
    "/api/service-history",
    "/api/downtime",
    "/api/preventive-maintenance",
  ]) {
    assert.ok(service.includes(endpoint) || maintenance.includes(endpoint), `frontend omits ${endpoint}`);
    assert.ok(backend.includes(endpoint), `backend omits ${endpoint}`);
  }
});

test("known legacy Batch 3 rows are classified as seeded synthetic database data", () => {
  const backend = read("backend-dotnet/Controllers/EndpointMappings.cs");
  const planning = read("frontend/src/pages/MaintenancePlanningPage.tsx");
  assert.match(backend, /seeded_synthetic_database/);
  assert.match(backend, /unknown_database_record/);
  assert.match(backend, /WO-B3-/);
  assert.match(backend, /B3 maintenance item/);
  assert.match(planning, /Demo Data/);
});
