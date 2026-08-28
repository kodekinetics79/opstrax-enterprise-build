import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

const source = readFileSync(fileURLToPath(new URL("../src/services/runtimeDiagnostics.ts", import.meta.url)), "utf8");
const fetchBlock = source.slice(
  source.indexOf("export async function fetchRuntimeDiagnostics"),
  source.indexOf("export function useRuntimeDiagnostics"),
);

assert.ok(fetchBlock.includes('apiClient.get("/health/ready"'), "browser diagnostics must use public readiness");
assert.ok(!fetchBlock.includes("/health/deep"), "browser diagnostics must not call the protected deep-health endpoint");
assert.ok(source.includes("critical_worker_violations"), "runtime truth must enforce the public worker contract");
assert.ok(source.includes("critical_worker_startup_grace_active"), "runtime truth must distinguish startup grace");

console.log("Runtime diagnostics contract: public readiness only, protected deep health preserved.");
