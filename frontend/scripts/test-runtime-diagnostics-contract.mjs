import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import ts from "typescript";

const source = readFileSync(fileURLToPath(new URL("../src/services/runtimeDiagnostics.ts", import.meta.url)), "utf8");
const identityPath = fileURLToPath(new URL("../src/services/runtimeDeploymentIdentity.ts", import.meta.url));
const identitySource = readFileSync(identityPath, "utf8");
const transpiledIdentity = ts.transpileModule(identitySource, {
  compilerOptions: { module: ts.ModuleKind.ES2022, target: ts.ScriptTarget.ES2022 },
  fileName: identityPath,
});
const { exactDeploymentSha } = await import(`data:text/javascript;base64,${Buffer.from(transpiledIdentity.outputText).toString("base64")}`);
const sha = "469209a8db329a5313cb66fdea66c1c394eb6021";

assert.equal(exactDeploymentSha(sha.toUpperCase()), sha, "a raw provider SHA remains exact");
assert.equal(exactDeploymentSha(`1.0.0+${sha}`), sha, ".NET informational versions expose their exact embedded commit");
for (const unverifiable of [
  `build-${sha}`,
  `1.0.0+${sha}.dirty`,
  `1.0.0+${sha.slice(1)}`,
  "unknown",
  null,
]) assert.equal(exactDeploymentSha(unverifiable), "", `unverifiable identity must fail closed: ${unverifiable}`);
const fetchBlock = source.slice(
  source.indexOf("export async function fetchRuntimeDiagnostics"),
  source.indexOf("export function useRuntimeDiagnostics"),
);

assert.ok(fetchBlock.includes('apiClient.get("/health/ready"'), "browser diagnostics must use public readiness");
assert.ok(!fetchBlock.includes("/health/deep"), "browser diagnostics must not call the protected deep-health endpoint");
assert.ok(source.includes("critical_worker_violations"), "runtime truth must enforce the public worker contract");
assert.ok(source.includes("critical_worker_startup_grace_active"), "runtime truth must distinguish startup grace");
assert.ok(source.includes("frontendSha === apiSha"), "runtime truth must require exact frontend/API SHA parity");
assert.ok(source.includes("exactDeploymentSha(ready.version || deep.version)"), "runtime truth must normalize only recognized exact API commit formats");
assert.ok(source.includes("frontendEnvironment === apiEnvironment"), "runtime truth must require frontend/API environment parity");
assert.ok(source.includes('state = "Mismatch"'), "runtime truth must expose deployment identity mismatch visibly");
assert.match(source, /verifiedLive[^;]+provenanceMatches/, "green runtime truth must fail closed on deployment provenance");

console.log("Runtime diagnostics contract: public readiness only, protected deep health preserved.");
