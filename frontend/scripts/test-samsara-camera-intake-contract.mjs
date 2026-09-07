import assert from "node:assert/strict";
import { createRequire } from "node:module";
import { fileURLToPath } from "node:url";
import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { webcrypto } from "node:crypto";
import test from "node:test";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const require = createRequire(import.meta.url);
const esbuild = require("esbuild");
const built = await esbuild.build({
  stdin: { contents: 'export * from "@/services/integrationsApi"; export {apiClient} from "@/services/apiClient";', loader: "ts", resolveDir: root },
  bundle: true,
  platform: "node",
  format: "cjs",
  write: false,
  logLevel: "silent",
  alias: { "@": resolve(root, "src") },
  external: ["axios"],
  define: { "import.meta.env": "{}" },
});

function serviceFixture() {
  const module = { exports: {} };
  const localStorage = { getItem: () => null, setItem: () => {}, removeItem: () => {} };
  new Function("module", "exports", "require", "crypto", "localStorage", "window", built.outputFiles[0].text)(
    module, module.exports, require, webcrypto, localStorage,
    { location: { hostname: "example.test", pathname: "/integrations" }, localStorage });
  const calls = [];
  module.exports.apiClient.defaults.adapter = async (config) => {
    calls.push(config);
    return { status: 200, data: { success: true, data: { success: true, message: "fixture", details: {} } }, config, headers: {}, statusText: "fixture" };
  };
  return { api: module.exports, calls };
}

test("camera intake uses its dedicated tenant-scoped endpoint", async () => {
  const fixture = serviceFixture();
  await fixture.api.integrationsApi.syncCameraSafety("23");
  assert.equal(fixture.calls.length, 1);
  assert.equal(fixture.calls[0].method, "post");
  assert.equal(fixture.calls[0].url, "/api/integrations/23/camera-safety/sync");
  assert.deepEqual(JSON.parse(fixture.calls[0].data), {});
});

test("Samsara intake UI states the evidence boundary and refreshes camera status", () => {
  const page = readFileSync(resolve(root, "src/pages/IntegrationsPage.tsx"), "utf8");
  const start = page.indexOf("const cameraSafetyMut = useMutation");
  const end = page.indexOf("const fields =", start);
  assert.ok(start >= 0 && end > start, "camera safety mutation is missing");
  const mutation = page.slice(start, end);
  assert.match(mutation, /integrationsApi\.syncCameraSafety\(integration\.id\)/);
  assert.match(mutation, /\["dashcam", "provider-status"\]/);
  assert.match(mutation, /\["dashcam", "provider-events"\]/);

  const step = page.slice(page.indexOf("4. Intake camera safety events"), page.indexOf("</li>", page.indexOf("4. Intake camera safety events")));
  assert.match(step, /External hold/i);
  assert.match(step, /provider payload evidence only/i);
  assert.match(step, /Camera media, provider verification, privacy acceptance, and certification stay on External hold/i);
  assert.doesNotMatch(step, /camera (?:is )?certified|media available|provider verified: yes/i);

  assert.match(page, /key: "apiRegion"/);
  assert.match(page, /key: "cameraSafetyAutoSync"/);
  assert.match(page, /value: "disabled", label: "Manual only"/);
  assert.match(page, /value: "enabled", label: "Every scheduled connector cycle"/);
  assert.match(page, /runs after successful scheduled GPS cycles/i);
  assert.match(page, /without marking GPS disconnected/i);
  assert.match(page, /value: "us", label: "United States \/ legacy Canada"/);
  assert.match(page, /value: "eu", label: "Europe \/ United Kingdom"/);
  assert.match(page, /value: "ca", label: "Canada cloud"/);
  assert.match(page, /Changing it requires a new connection test and starts a new provider mapping review/);
});
