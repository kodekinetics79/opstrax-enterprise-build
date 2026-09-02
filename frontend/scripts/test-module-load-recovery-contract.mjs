import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { fileURLToPath } from "node:url";
import ts from "typescript";

const frontendRoot = resolve(fileURLToPath(new URL("..", import.meta.url)));
const sourcePath = resolve(frontendRoot, "src/utils/moduleLoadRecovery.ts");
const source = readFileSync(sourcePath, "utf8");
const transpiled = ts.transpileModule(source, {
  compilerOptions: {
    module: ts.ModuleKind.ES2022,
    target: ts.ScriptTarget.ES2022,
  },
  fileName: sourcePath,
});
const recovery = await import(`data:text/javascript;base64,${Buffer.from(transpiled.outputText).toString("base64")}`);

for (const error of [
  new TypeError("Failed to fetch dynamically imported module: https://example.test/assets/FleetOverviewPage-old.js"),
  new Error("Importing a module script failed."),
  new Error("ChunkLoadError: Loading chunk 42 failed"),
]) {
  assert.equal(recovery.isDynamicImportFailure(error), true);
  assert.equal(recovery.shouldReloadForDynamicImportFailure(error, false), true);
  assert.equal(recovery.shouldReloadForDynamicImportFailure(error, true), false);
}

assert.equal(recovery.isDynamicImportFailure(new Error("Request failed with status 500")), false);
assert.equal(recovery.shouldReloadForDynamicImportFailure(new Error("Ordinary render error"), false), false);
assert.equal(recovery.moduleLoadRecoveryKey("abc123"), "opstrax:module-load-recovery:abc123");

const boundary = readFileSync(resolve(frontendRoot, "src/components/ErrorBoundary.tsx"), "utf8");
assert.match(boundary, /window\.sessionStorage\.getItem\(recoveryKey\)/);
assert.match(boundary, /window\.sessionStorage\.setItem\(recoveryKey, "attempted"\)/);
assert.match(boundary, /shouldReloadForDynamicImportFailure\(error, storedAttempt === "attempted"\)/);
assert.match(boundary, /window\.location\.reload\(\)/);
assert.match(boundary, /catch \{[\s\S]*Do not reload[\s\S]*explicit recovery action visible/);

const vercelConfig = JSON.parse(readFileSync(resolve(frontendRoot, "../vercel.json"), "utf8"));
assert.equal(vercelConfig.rewrites, undefined);
assert.equal(vercelConfig.headers, undefined);
assert.equal(vercelConfig.routes[0].src, "/.*");
assert.equal(vercelConfig.routes[0].continue, true);
assert.equal(vercelConfig.routes[0].headers["Content-Security-Policy"].includes("default-src 'self'"), true);
assert.equal(vercelConfig.routes[0].headers["X-Content-Type-Options"], "nosniff");
assert.deepEqual(vercelConfig.routes.slice(1), [
  { handle: "filesystem" },
  { src: "/assets/.*", status: 404 },
  { src: "/.*", dest: "/index.html" },
]);

console.log("Dynamic module-load recovery and stale-asset routing contract passed.");
