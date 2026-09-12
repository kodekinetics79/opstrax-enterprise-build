import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { runInNewContext } from "node:vm";
import ts from "typescript";

const shell = readFileSync(fileURLToPath(new URL("../src/layouts/AppShell.tsx", import.meta.url)), "utf8");
const integrations = readFileSync(fileURLToPath(new URL("../src/pages/IntegrationsPage.tsx", import.meta.url)), "utf8");
const tenants = readFileSync(fileURLToPath(new URL("../src/pages/platform/PlatformTenantsPage.tsx", import.meta.url)), "utf8");

assert.ok(
  shell.includes('const runtimeState = runtimeQuery.data?.state ?? "Unavailable"'),
  "tenant data provenance must not override deployment health",
);
assert.doesNotMatch(shell, /synthetic-data-badge/, "the redundant topbar Demo Data badge must remain removed");
const shellTree = ts.createSourceFile("AppShell.tsx", shell, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
const shellNodes = [];
function visit(node) { shellNodes.push(node); ts.forEachChild(node, visit); }
visit(shellTree);
const literalAttribute = (node, name) => {
  const value = node.attributes.properties.find(attribute => ts.isJsxAttribute(attribute) && attribute.name.getText(shellTree) === name)?.initializer;
  return value && ts.isStringLiteral(value) ? value.text : undefined;
};
const contexts = shellNodes.filter(node => ts.isJsxOpeningElement(node) && literalAttribute(node, "data-testid") === "tenant-data-context");
assert.equal(contexts.length, 1, "the tenant name context must remain discoverable in one account detail surface");
let accountContext = false, conditionalContext = false;
for (let parent = contexts[0].parent; parent; parent = parent.parent) {
  if (ts.isJsxElement(parent) && literalAttribute(parent.openingElement, "id") === "tenant-account-menu") accountContext = true;
  if (ts.isConditionalExpression(parent) && parent.condition.getText(shellTree) === "tenantNameSuggestsDemo") conditionalContext = true;
}
assert.ok(accountContext && conditionalContext, "the demo/test name context belongs only inside the authenticated account menu and its conditional name gate");
assert.match(contexts[0].parent.getText(shellTree), /Based on the organization name\. Check individual records for their source\./, "a name heuristic must not claim authoritative all-record provenance");
const initializer = name => shellNodes.find(node => ts.isVariableDeclaration(node) && node.name.getText(shellTree) === name)?.initializer?.getText(shellTree);
assert.ok(initializer("tenantNameSuggestsDemo"));
for (const [name, expected] of [["OpsTrax Demo Logistics", true], ["SYNTHETIC fleet", true], ["Test Fleet", true], ["Meridian Logistics", false], ["Contest Carrier", false], [undefined, false]]) {
  assert.equal(runInNewContext(initializer("tenantNameSuggestsDemo"), { session: { company: { name } } }), expected, `actual tenant name classification: ${String(name)}`);
}
for (const state of ["Live", "Mismatch", "Stale", undefined]) {
  assert.equal(runInNewContext(initializer("runtimeState"), { runtimeQuery: { data: { state } }, tenantNameSuggestsDemo: true }), state ?? "Unavailable", "tenant name context must not override actual deployment state");
}
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
