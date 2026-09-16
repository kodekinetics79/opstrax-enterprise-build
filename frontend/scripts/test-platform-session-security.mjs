import assert from "node:assert/strict";
import { existsSync, readFileSync } from "node:fs";
import { createRequire } from "node:module";
import { buildSync } from "esbuild";

const require = createRequire(import.meta.url);
const source = readFileSync(new URL("../src/auth/accessScope.ts", import.meta.url), "utf8");
assert.doesNotMatch(source, /@northshore-fleet|@local-fleet|@client\.com/i,
  "portal scope must not contain named account mappings");
assert.doesNotMatch(source, /assignedDriverName|customerName|companyName|\.includes\(target\)/,
  "portal scope must not use display names or partial string matching as identity");

const bundled = buildSync({
  stdin: { contents: source, loader: "ts", resolveDir: new URL("../src/auth", import.meta.url).pathname },
  bundle: true,
  platform: "node",
  format: "cjs",
  write: false,
}).outputFiles[0].text;
const module = { exports: {} };
new Function("module", "exports", "require", bundled)(module, module.exports, require);
const { scopeRowsForSession } = module.exports;

const driverSession = { role: "Driver", permissions: ["driver:self"], portalContext: { driverId: 17 } };
const driverRows = [
  { id: 1, driverId: 17, driverName: "Changed Name" },
  { id: 2, driverId: 18, driverName: "Changed Name" },
];
assert.deepEqual(scopeRowsForSession("jobs", driverRows, driverSession).map((row) => row.id), [1]);
assert.deepEqual(scopeRowsForSession("jobs", driverRows, { ...driverSession, portalContext: {} }), [],
  "missing authenticated driver binding must fail closed");

const customerSession = { role: "Customer", permissions: ["customer_portal:view"], portalContext: { customerId: 44 } };
const customerRows = [{ id: 44, name: "Renamed Customer" }, { id: 45, name: "Renamed Customer" }];
assert.deepEqual(scopeRowsForSession("customers", customerRows, customerSession).map((row) => row.id), [44]);
assert.deepEqual(scopeRowsForSession("customers", customerRows, { ...customerSession, portalContext: {} }), [],
  "missing authenticated customer binding must fail closed");

const platformApi = readFileSync(new URL("../src/services/platformApi.ts", import.meta.url), "utf8");
const platformAuth = readFileSync(new URL("../src/hooks/usePlatformAuth.tsx", import.meta.url), "utf8");
assert.match(platformApi, /createApiClient\(/, "platform calls must use the standard HTTP pipeline");
assert.doesNotMatch(platformApi, /localStorage\.setItem|headers\.Authorization|Bearer /,
  "platform JavaScript must not persist or attach a privileged bearer credential");
assert.doesNotMatch(platformAuth, /loadPlatformSession|storePlatformSession|StorageEvent|PLATFORM_STORAGE_KEY/,
  "platform auth must restore authority from the HttpOnly-cookie /me path");

assert.equal(existsSync(new URL("../src/components/WorkspaceExperience.tsx", import.meta.url)), false,
  "the unused guidance helper and its icon import must remain removed");

console.log("Platform session and authenticated portal scope contracts passed.");
