import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

const shell = readFileSync(fileURLToPath(new URL("../src/layouts/AppShell.tsx", import.meta.url)), "utf8");
const integrations = readFileSync(fileURLToPath(new URL("../src/pages/IntegrationsPage.tsx", import.meta.url)), "utf8");
const tenants = readFileSync(fileURLToPath(new URL("../src/pages/platform/PlatformTenantsPage.tsx", import.meta.url)), "utf8");

assert.ok(
  shell.includes('const runtimeState = runtimeQuery.data?.state ?? "Unavailable"'),
  "tenant data provenance must not override deployment health",
);
assert.ok(!shell.includes('tenantIsExplicitlySynthetic ? "Demo Data"'), "demo data must not mask a release mismatch");
assert.ok(shell.includes('data-testid="synthetic-data-badge"'), "demo data needs its own visible provenance badge");
assert.ok(shell.includes('data-testid="release-mismatch-banner"'), "release mismatch needs a prominent page-level warning");
assert.ok(shell.includes("window.location.reload()"), "release mismatch must offer a direct recovery action");

assert.ok(
  integrations.includes('useState<ConnectorView>("available")'),
  "integrations must default to available adapters",
);
assert.ok(integrations.includes('connectorView === "available" ? availableIntegrations : evaluationIntegrations'), "evaluation catalog must be separated from available adapters");
assert.ok(integrations.includes("Evaluation catalog"), "evaluation-only entries need a clear customer-facing label");
assert.ok(integrations.includes("No credentials can be stored and no connection is claimed."), "evaluation catalog must state its commercial truth");

assert.ok(tenants.includes('data-testid="client-poc-preflight"'), "platform tenant controls need an independent POC preflight");
assert.ok(tenants.includes('policyMode === "package_allowlist"'), "POC preflight must require deny-by-default access");
assert.ok(tenants.includes("hasActiveClientAdmin"), "POC preflight must require an active client administrator");
assert.ok(tenants.includes("delivered client invitation"), "POC GO must keep email delivery as explicit launch evidence");
assert.ok(tenants.includes("approved real client data"), "POC GO must require real client data");
assert.ok(tenants.includes("exact-SHA browser smoke test"), "POC GO must require exact release browser evidence");

console.log("Client POC truth contract: release identity, tenant isolation, data provenance, and connector scope are explicit.");
