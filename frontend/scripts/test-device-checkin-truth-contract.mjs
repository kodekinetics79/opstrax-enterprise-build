import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { createRequire } from "node:module";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";

const require = createRequire(import.meta.url);
const esbuild = require("esbuild");
const ts = require("typescript");
const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const service = readFileSync(resolve(root, "src/services/telematicsService.ts"), "utf8");
const page = readFileSync(resolve(root, "src/pages/IotDevicesPage.tsx"), "utf8");

// Execute the shipped service with only its HTTP transport substituted. Other
// production dependencies stay bundled; no real network or credentials are used.
const built = await esbuild.build({
  stdin: {
    contents: service.replace(
      'import { apiClient, unwrap } from "@/services/apiClient";',
      'const apiClient = __apiClient; const unwrap = async (request) => { const response = await request; if (!response.data.success) throw new Error("fixture request failed"); return response.data.data; };',
    ),
    resolveDir: root,
    loader: "ts",
  },
  bundle: true,
  platform: "node",
  format: "cjs",
  write: false,
  alias: { "@": resolve(root, "src") },
  define: { "import.meta.env": "{}" },
  logLevel: "silent",
});

async function inspect(row, { failure = false, observedAt, payload, responseData } = {}) {
  const calls = [];
  const module = { exports: {} };
  const api = {
    get: async (...args) => {
      calls.push(["GET", ...args]);
      if (failure) throw new Error("fixture unavailable");
      return { data: responseData === undefined ? { success: true, data: payload === undefined ? { device: row } : payload } : responseData };
    },
    post: () => assert.fail("check-in inspection must not mutate"),
    put: () => assert.fail("check-in inspection must not mutate"),
    delete: () => assert.fail("check-in inspection must not mutate"),
  };
  const ObservedDate = class extends Date { static now() { return observedAt ?? Date.now(); } };
  new Function("require", "module", "exports", "__apiClient", "Date", built.outputFiles[0].text)(require, module, module.exports, api, ObservedDate);
  const result = await module.exports.telematicsService.getDeviceConnectionState(741);
  assert.deepEqual(calls, [["GET", "/api/telemetry/devices/741"]]);
  return result;
}

// Execute the actual TSX function with a query-state seam and an element factory.
// This is component logic evidence, not a browser/rendered journey claim.
const source = ts.createSourceFile("IotDevicesPage.tsx", page, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
const dialog = source.statements.find((node) => ts.isFunctionDeclaration(node) && node.name?.text === "DeviceCredentialsDialog");
assert.ok(dialog, "the actual credential dialog must exist");
const dialogCode = esbuild.transformSync(`export ${dialog.getText(source)}`, {
  loader: "tsx", format: "cjs", jsxFactory: "h", jsxFragment: "Fragment",
}).code;
const h = (type, props, ...children) => ({ type, props: props ?? {}, children: children.flat(Infinity) });
function render(query) {
  const module = { exports: {} };
  let options;
  const queryHook = (value) => { options = value; return query; };
  const api = { getDeviceConnectionState: () => assert.fail("query factory must not perform transport during component evaluation") };
  new Function("module", "exports", "h", "useQuery", "useDialogFocus", "telematicsService", "CheckCircle2", "AlertTriangle", "CopyField", "Terminal", "RadioTower", dialogCode)(
    module, module.exports, h, queryHook, () => null, api, "check-icon", "alert-icon", "copy-field", "terminal-icon", "radio-icon",
  );
  const tree = module.exports.DeviceCredentialsDialog({
    result: { credentials: { deviceId: "741", deviceSerial: "FIXTURE", apiKey: "", hmacSecret: "", note: "Fixture only" }, ingestUrl: "https://example.invalid/api/telemetry/ingest", device: { deviceName: "Fixture", serialNumber: "FIXTURE" } },
    onDone() {},
  });
  const nodes = [];
  function visit(node) {
    if (node && typeof node === "object") { nodes.push(node); node.children.forEach(visit); }
  }
  visit(tree);
  function text(node) {
    if (node == null || typeof node === "boolean") return "";
    if (typeof node !== "object") return String(node);
    return node.children.map(text).join(" ");
  }
  return { tree, nodes, text: text(tree), options };
}

const valid = "2024-02-29T12:34:56.123456Z";
const observed = { hasRecordedCheckIn: true, lastSeenAt: valid, status: "Active", deviceState: "Registered", lifecycleBlocked: false, connected: true };

test("a stored historical check-in yields a record fact, never connected or streaming evidence", async () => {
  const result = await inspect({ lastSeenAt: valid, status: "Active" });
  assert.equal(result.hasRecordedCheckIn, true);
  assert.equal(result.lastSeenAt, valid);
  assert.equal(result.status, "Active");
  assert.equal(result.lifecycleBlocked, false);
  assert.equal(Object.hasOwn(result, "connected"), false);
});

test("valid explicit offsets, leap years and offset rollover retain the stored timestamp", async () => {
  for (const timestamp of ["2000-02-29T00:00:00Z", "2024-03-01T00:01:00+14:00", "2024-02-29T23:59:59-14:00", "2024-01-01T00:00:00+00:00", "2024-02-29T12:34:56.1234567-05:30"]) {
    const result = await inspect({ last_seen_at: timestamp, status: "Active" });
    assert.equal(result.hasRecordedCheckIn, true, timestamp);
    assert.equal(result.lastSeenAt, timestamp);
  }
});

test("observation boundary respects offset rollover and sub-millisecond timestamp precision", async () => {
  const observedAt = Date.parse("2024-01-01T00:00:00.000Z");
  for (const timestamp of ["2024-01-01T00:00:00.0000000Z", "2024-01-01T14:00:00+14:00", "2023-12-31T10:00:00-14:00"]) {
    assert.equal((await inspect({ last_seen_at: timestamp }, { observedAt })).hasRecordedCheckIn, true);
  }
  for (const timestamp of ["2024-01-01T00:00:00.0000001Z", "2024-01-01T00:00:00.001Z", "2024-01-01T14:00:01+14:00"]) {
    assert.equal((await inspect({ last_seen_at: timestamp }, { observedAt })).hasRecordedCheckIn, false);
  }
});

test("empty, malformed, impossible, unknown-offset and future timestamps cannot become record evidence", async () => {
  for (const timestamp of [null, undefined, "", " ", true, 123, "not-a-date", "2024-01-01", "2024-01-01T00:00:00", "2023-02-29T00:00:00Z", "1900-02-29T00:00:00Z", "2024-02-30T00:00:00Z", "2024-04-31T00:00:00Z", "2024-13-01T00:00:00Z", "2024-01-00T00:00:00Z", "2024-01-01T24:00:00Z", "2024-01-01T00:60:00Z", "2024-01-01T00:00:60Z", "2024-01-01T00:00:00-00:00", "2024-01-01T00:00:00+14:01", "2024-01-01T00:00:00+15:00", "0000-01-01T00:00:00Z", "9999-01-01T00:00:00Z"]) {
    const result = await inspect({ last_seen_at: timestamp, status: "Active" });
    assert.equal(result.hasRecordedCheckIn, false, String(timestamp));
    assert.equal(result.lastSeenAt, null, String(timestamp));
  }
});

test("revocation and blocked status or device_state outrank the historical fact without erasing it", async () => {
  for (const patch of [{ revoked_at: valid }, { status: "Suspended" }, { status: "Revoked" }, { status: "Retired" }, { device_state: "Quarantined" }, { deviceState: "Decommissioned" }, { device_state: "Suspended" }]) {
    const result = await inspect({ status: "Active", last_seen_at: valid, ...patch });
    assert.equal(result.hasRecordedCheckIn, true);
    assert.equal(result.lastSeenAt, valid);
    assert.equal(result.lifecycleBlocked, true, JSON.stringify(patch));
  }
});

test("missing and malformed lifecycle tokens remain unknown instead of defaulting active", async () => {
  for (const status of [null, undefined, "", "  ", 1, true, { active: true }, ["Active"]]) {
    const result = await inspect({ status, last_seen_at: valid });
    assert.equal(result.status, "Unknown");
    assert.equal(result.hasRecordedCheckIn, true);
    const stateResult = await inspect({ status: "Active", device_state: status, last_seen_at: valid });
    assert.equal(stateResult.deviceState, "Unknown");
    assert.equal(stateResult.hasRecordedCheckIn, true);
  }
  assert.equal((await inspect({ status: "Unrecognized", last_seen_at: valid })).status, "Unrecognized");

  const expectedUnknown = { hasRecordedCheckIn: false, lastSeenAt: null, status: "Unknown", deviceState: "Unknown", lifecycleBlocked: false };
  const polluted = { last_seen_at: valid, status: "Suspended", device_state: "Quarantined", revoked_at: valid };
  const originalDescriptors = Object.fromEntries(Object.keys(polluted).map(key => [key, Object.getOwnPropertyDescriptor(Object.prototype, key)]));
  for (const [key, value] of Object.entries(polluted)) Object.defineProperty(Object.prototype, key, { configurable: true, value });
  try {
    assert.deepEqual(await inspect({}), expectedUnknown, "Object.prototype fields must not become a device check-in or lifecycle fact");
  } finally {
    for (const key of Object.keys(polluted)) {
      const descriptor = originalDescriptors[key];
      if (descriptor) Object.defineProperty(Object.prototype, key, descriptor);
      else delete Object.prototype[key];
    }
  }

  class ExoticRecord {
    constructor() {
      this.last_seen_at = valid;
      this.status = "Suspended";
      this.device_state = "Quarantined";
      this.revoked_at = valid;
    }
  }
  for (const row of [new ExoticRecord(), Object.assign(new Date(0), polluted), Object.assign([], polluted)]) {
    assert.deepEqual(await inspect(row), expectedUnknown, `${row.constructor.name} device carriers must fail closed`);
  }
  const inheritedDetail = Object.create({ device: { last_seen_at: valid, status: "Active", device_state: "Registered" } });
  const exoticDetail = Object.assign(new (class DetailCarrier {})(), { device: { last_seen_at: valid, status: "Active", device_state: "Registered" } });
  for (const payload of [inheritedDetail, exoticDetail, Object.assign([], { device: { last_seen_at: valid, status: "Active" } })]) {
    assert.deepEqual(await inspect(null, { payload }), expectedUnknown, "inherited, exotic and array detail carriers must fail closed");
  }

  const nullPrototypeRow = Object.assign(Object.create(null), { lastSeenAt: valid, status: "Active", deviceState: "Registered" });
  const nullPrototypeDetail = Object.assign(Object.create(null), { device: nullPrototypeRow });
  assert.deepEqual(await inspect(null, { payload: nullPrototypeDetail }), {
    hasRecordedCheckIn: true, lastSeenAt: valid, status: "Active", deviceState: "Registered", lifecycleBlocked: false,
  });

  for (const row of [
    { lastSeenAt: valid, last_seen_at: undefined, status: "Active", deviceState: "Registered" },
    { lastSeenAt: valid, last_seen_at: "2024-02-29T12:34:55Z", status: "Active", deviceState: "Registered" },
    { Last_Seen_At: valid, status: "Active", deviceState: "Registered" },
  ]) {
    const result = await inspect(row);
    assert.equal(result.hasRecordedCheckIn, false);
    assert.equal(result.lastSeenAt, null);
  }
  for (const row of [
    { lastSeenAt: valid, status: "Active", deviceState: "Registered", device_state: "Suspended" },
    { lastSeenAt: valid, status: "Active", Device_State: "Registered" },
    { lastSeenAt: valid, status: "Active", revokedAt: null, revoked_at: valid },
    { lastSeenAt: valid, status: "Active", Revoked_At: valid },
  ]) {
    const result = await inspect(row);
    assert.equal(result.hasRecordedCheckIn, true);
    assert.equal(result.lifecycleBlocked, true, JSON.stringify(row));
  }
  assert.equal((await inspect({ lastSeenAt: valid, status: "Active", revokedAt: valid })).lifecycleBlocked, true);

  const admittedRow = { last_seen_at: valid, status: "Active", device_state: "Registered" };
  for (const responseData of [
    Object.create({ success: true, data: { device: admittedRow } }),
    { success: true, data: { device: admittedRow }, Success: false },
    { success: true, data: { device: admittedRow }, Data: { device: admittedRow } },
  ]) {
    await assert.rejects(inspect(null, { responseData }), /check-in response/i);
  }
  const envelopePrototypeDescriptors = Object.fromEntries(["success", "data"].map(key => [key, Object.getOwnPropertyDescriptor(Object.prototype, key)]));
  Object.defineProperty(Object.prototype, "success", { configurable: true, value: true });
  Object.defineProperty(Object.prototype, "data", { configurable: true, value: { device: admittedRow } });
  try {
    await assert.rejects(inspect(null, { responseData: {} }), /check-in response/i);
  } finally {
    for (const key of ["success", "data"]) {
      const descriptor = envelopePrototypeDescriptors[key];
      if (descriptor) Object.defineProperty(Object.prototype, key, descriptor);
      else delete Object.prototype[key];
    }
  }
  for (const payload of [
    { device: admittedRow, record: { ...admittedRow, status: "Suspended" } },
    { device: admittedRow, Device: { ...admittedRow, status: "Suspended" } },
    { device: admittedRow, d_e_v_i_c_e: { ...admittedRow, status: "Suspended" } },
  ]) {
    assert.deepEqual(await inspect(null, { payload }), expectedUnknown, "ambiguous detail wrappers must fail closed");
  }
  const nullPrototypeEnvelope = Object.assign(Object.create(null), {
    success: true,
    data: Object.assign(Object.create(null), { record: Object.assign(Object.create(null), admittedRow) }),
  });
  assert.deepEqual(await inspect(null, { responseData: nullPrototypeEnvelope }), {
    hasRecordedCheckIn: true, lastSeenAt: valid, status: "Active", deviceState: "Registered", lifecycleBlocked: false,
  }, "null-prototype envelope and retained record wrapper must remain accepted");
});

test("service transport failures reject without inventing a record", async () => {
  await assert.rejects(inspect({}, { failure: true }), /fixture unavailable/);
});

test("actual dialog communicates historical evidence only and keeps polling after a recorded check-in", () => {
  const result = render({ data: observed, isError: false });
  assert.match(result.text, /Recorded check-in found/);
  assert.match(result.text, /Last recorded check-in/);
  assert.match(result.text, /does not establish current connectivity or device verification/);
  assert.doesNotMatch(result.text, /Connection established|Connected — device is streaming|first accepted POST completes pairing|first authenticated telemetry POST/);
  assert.equal(result.options.refetchInterval, 5000);
  assert.equal(result.nodes.some((node) => /live-dot|animate-pulse/.test(node.props.className ?? "")), false);
});

test("query errors override cached check-in data in the actual dialog", () => {
  const result = render({ data: observed, isError: true });
  assert.match(result.text, /Unable to check device record/);
  assert.doesNotMatch(result.text, /Recorded check-in found|Last recorded check-in|2024-02-29|Connection established|device is streaming/);
  assert.equal(result.options.refetchInterval, 5000);
});

test("blocked lifecycle overrides the cached record in the actual dialog", () => {
  const result = render({ data: { ...observed, lifecycleBlocked: true }, isError: false });
  assert.match(result.text, /Device lifecycle is restricted/);
  assert.doesNotMatch(result.text, /Recorded check-in found|Connection established|device is streaming/);
  assert.equal(result.options.refetchInterval, 5000);
});

test("unknown lifecycle, missing record and loading states never imply current connection", () => {
  const unknown = render({ data: { ...observed, status: "Unknown" }, isError: false });
  assert.match(unknown.text, /Device lifecycle status is unknown/);
  assert.match(unknown.text, /Last recorded check-in/);
  const unrecognized = render({ data: { ...observed, status: "Unrecognized" }, isError: false });
  assert.match(unrecognized.text, /Device lifecycle status: Unrecognized/);
  assert.doesNotMatch(unrecognized.text, /Recorded check-in found/);
  for (const deviceState of ["Unknown", "Unrecognized"]) {
    const stateResult = render({ data: { ...observed, deviceState }, isError: false });
    assert.match(stateResult.text, /Device lifecycle state/);
    assert.match(stateResult.text, /Last recorded check-in/);
    assert.doesNotMatch(stateResult.text, /Recorded check-in found/);
  }
  const missing = render({ data: { ...observed, hasRecordedCheckIn: false, lastSeenAt: null, connected: false }, isError: false });
  assert.match(missing.text, /No valid recorded check-in/);
  const loading = render({ data: undefined, isError: false, isPending: true });
  assert.match(loading.text, /Checking device record/);
  for (const result of [unknown, missing, loading]) assert.doesNotMatch(result.text, /Connection established|device is streaming/);
});
