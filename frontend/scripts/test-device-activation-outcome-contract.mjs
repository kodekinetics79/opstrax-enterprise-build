import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { createRequire } from "node:module";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";

const require = createRequire(import.meta.url);
const esbuild = require("esbuild");
const ts = require("typescript");
const { QueryClient, QueryObserver, onlineManager } = require("@tanstack/react-query");
const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const serviceSource = readFileSync(resolve(root, "src/services/telematicsService.ts"), "utf8");
const pageSource = readFileSync(resolve(root, "src/pages/IotDevicesPage.tsx"), "utf8");
const page = ts.createSourceFile("IotDevicesPage.tsx", pageSource, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
const built = await esbuild.build({
  stdin: {
    contents: serviceSource
      .replace('import { apiClient, unwrap } from "@/services/apiClient";', 'import { unwrap } from "@/services/apiClient"; const apiClient = __apiClient;')
      .replace('import { readRawSession } from "@/auth/sessionStorage";', 'const readRawSession = () => JSON.stringify(__session);'),
    loader: "ts", resolveDir: root,
  },
  bundle: true, platform: "node", format: "cjs", write: false,
  alias: { "@": resolve(root, "src") }, define: { "import.meta.env": "{}" }, logLevel: "silent",
});

const receipt = { id: 741, status: "Active", deviceState: "Registered", rowVersion: 2, updatedAt: "2026-01-01T00:00:00Z" };
const tick = () => new Promise(resolve => setImmediate(resolve));
function loadService({ data = receipt, envelope, failure, getFailureUrl, session = { role: "Tenant Admin" } } = {}) {
  const calls = [];
  const holder = { exports: {} };
  const api = {
    post: async (...args) => {
      calls.push(["POST", ...args]);
      if (failure) throw failure;
      return { status: 200, data: envelope === undefined ? { success: true, data } : envelope };
    },
    get: async (...args) => {
      calls.push(["GET", ...args]);
      if (!getFailureUrl || args[0] === getFailureUrl) throw new Error("fixture read unavailable");
      return { data: { success: true, data: args[0].endsWith("/741") ? { device: { id: 741, status: "Active" } } : [] } };
    },
    put: () => assert.fail("activation must not issue PUT"),
    delete: () => assert.fail("activation must not issue DELETE"),
  };
  const browserPresence = { location: { hostname: "example.invalid" }, localStorage: { getItem: () => null }, sessionStorage: { getItem: () => null } };
  new Function("require", "module", "exports", "__apiClient", "__session", "window", built.outputFiles[0].text)(require, holder, holder.exports, api, session, browserPresence);
  return { ...holder.exports, calls };
}

function declaration(name) {
  let result;
  function visit(node) {
    if (ts.isVariableDeclaration(node) && node.name.getText(page) === name) result = node.initializer?.getText(page);
    ts.forEachChild(node, visit);
  }
  visit(page);
  return result;
}

function evaluate(expression, scope, { component = false } = {}) {
  const holder = { exports: {} };
  const code = esbuild.transformSync(component ? `export ${expression}` : `export const value = ${expression};`, { loader: "tsx", format: "cjs", jsxFactory: "h" }).code;
  new Function("module", "exports", ...Object.keys(scope), code)(holder, holder.exports, ...Object.values(scope));
  return component ? holder.exports : holder.exports.value;
}

function loadRefresh({ fails = false, client, invalidate } = {}) {
  const states = { warning: false, pending: false, subscriptions: 0 };
  const context = { current: { deviceId: "741", generation: 0 } };
  const calls = [];
  const expression = declaration("refreshActivationDisplay");
  assert.ok(expression, "the shipped page must separate display refresh from the command");
  const fallback = { queryHash: "fixture", state: { fetchStatus: "idle", status: "success" }, isActive: () => true };
  const backingCache = client?.getQueryCache();
  const cache = { findAll: filters => backingCache ? backingCache.findAll(filters) : [fallback], get: hash => backingCache ? backingCache.get(hash) : fallback,
    subscribe: callback => { states.subscriptions++; const cleanup = backingCache?.subscribe(callback); return () => { states.subscriptions--; cleanup?.(); }; } };
  const queryClient = { getQueryCache: () => cache, invalidateQueries: async (...args) => {
    calls.push(args);
    if (invalidate) return invalidate(...args);
    if (client) return client.invalidateQueries(...args);
    if (fails) throw new Error("fixture refresh unavailable");
  } };
  const refreshReceiptQueries = evaluate(declaration("refreshReceiptQueries"), { queryClient });
  const refresh = evaluate(expression, {
    queryClient, refreshReceiptQueries,
    setActivationRefreshWarning: value => { states.warning = value; },
    setActivationRefreshPending: value => { states.pending = value; },
    activationRefreshContext: context,
  });
  return { refresh, states, calls, context };
}

function loadMutation(refresh, context = { current: { deviceId: "741", generation: 0 } }) {
  const states = { notice: null, confirmation: "open", warning: false, receiptId: null, pending: false };
  const calls = [];
  const options = evaluate(declaration("activateMut"), {
    useMutation: options => options,
    telematicsService: { activateDevice: async id => { calls.push(["POST", id]); return receipt; } },
    setConfirmTarget: value => { states.confirmation = value; },
    setNotice: value => { states.notice = value; },
    setActivationRefreshWarning: value => { states.warning = value; },
    setActivationRefreshPending: value => { states.pending = value; },
    setActivationReceiptId: value => { states.receiptId = value; },
    activationRefreshContext: context,
    refreshActivationDisplay: refresh,
    refreshAll: refresh,
  });
  return { options, states, calls };
}

test("fresh server acknowledgement survives unavailable readback and returns only minimal receipt", async () => {
  for (const deviceState of ["Registered", "Installed", "Verified"]) {
    for (const replayFields of [{}, { idempotentReplay: false }]) {
      const fixture = loadService({ data: { ...receipt, deviceState, ...replayFields } });
      const result = await fixture.telematicsService.activateDevice(741);
      assert.deepEqual(result, { id: "741", status: "Active", deviceState, rowVersion: 2, idempotentReplay: false });
      assert.deepEqual(fixture.calls, [["POST", "/api/telemetry/devices/741/activate", {}]]);
      assert.equal(Object.hasOwn(result, "updatedAt"), false);
      assert.equal(Object.hasOwn(result, "device"), false);
    }
  }
});

test("idempotent core acknowledgement without timestamp retains only known software state", async () => {
  const unknownStates = [undefined, null, {}, [], true, false, 1, "Suspended", "Revoked", "Quarantined", "Unexpected", "verified", ""];
  for (const deviceState of ["Registered", "Installed", "Verified", ...unknownStates]) {
    const data = { id: "741", status: "Active", rowVersion: 0, idempotentReplay: true };
    if (deviceState !== undefined) data.deviceState = deviceState;
    const fixture = loadService({ data });
    const result = await fixture.telematicsService.activateDevice("741");
    assert.deepEqual(result, { id: "741", status: "Active", rowVersion: 0, idempotentReplay: true,
      deviceState: ["Registered", "Installed", "Verified"].includes(deviceState) ? deviceState : null });
    assert.equal(Object.hasOwn(result, "updatedAt"), false);
    assert.equal(fixture.calls.length, 1);
  }
});

test("fresh malformed or unknown installation state never becomes an acknowledged activation", async () => {
  for (const deviceState of [undefined, null, {}, [], true, false, 1, "Active", "Suspended", "verified", ""]) {
    const fixture = loadService({ data: { ...receipt, deviceState } });
    await assert.rejects(fixture.telematicsService.activateDevice(741), error => error.outcome === "unconfirmed");
    assert.equal(fixture.calls.length, 1);
  }
});

test("malformed replay flags and conflicting aliases are unconfirmed; matching snake aliases are valid", async () => {
  for (const idempotentReplay of [undefined, null, "true", "false", 1, 0, {}, []]) {
    const fixture = loadService({ data: { ...receipt, idempotentReplay } });
    await assert.rejects(fixture.telematicsService.activateDevice(741), error => error.outcome === "unconfirmed");
  }
  const undefinedAlias = loadService({ data: { ...receipt, idempotent_replay: undefined } });
  await assert.rejects(undefinedAlias.telematicsService.activateDevice(741), error => error.outcome === "unconfirmed");
  for (const fields of [
    { idempotentReplay: true, idempotent_replay: false },
    { device_state: "Installed" }, { row_version: 3 },
    { idempotentReplay: true, device_state: "Unexpected" },
  ]) {
    const fixture = loadService({ data: { ...receipt, ...fields } });
    await assert.rejects(fixture.telematicsService.activateDevice(741), error => error.outcome === "unconfirmed");
    assert.equal(fixture.calls.length, 1);
  }
  const fixture = loadService({ data: { id: 741, status: "Active", device_state: "Registered", row_version: 0, idempotent_replay: true } });
  assert.equal((await fixture.telematicsService.activateDevice(741)).idempotentReplay, true);
});

test("strict core acknowledgement rejects malformed envelope, IDs, status and row versions", async () => {
  class ReceiptCarrier { constructor() { Object.assign(this, receipt); } }
  for (const data of [null, {}, [], new Date(), new ReceiptCarrier(), Object.create(receipt),
    { ...receipt, ID: 742 }, { ...receipt, Status: "Active" }, { ...receipt, Device_State: "Registered" }, { ...receipt, RowVersion: 2 },
    { ...receipt, id: 742 }, { ...receipt, id: "0741" }, { ...receipt, id: Number.MAX_SAFE_INTEGER + 1 },
    { ...receipt, status: "Suspended" }, { ...receipt, status: "active" }, { ...receipt, rowVersion: -1 },
    { ...receipt, rowVersion: 1.1 }, { ...receipt, rowVersion: "2" }, { ...receipt, rowVersion: Number.MAX_SAFE_INTEGER + 1 }]) {
    const fixture = loadService({ data });
    await assert.rejects(fixture.telematicsService.activateDevice(741), error => error.outcome === "unconfirmed");
    assert.deepEqual(fixture.calls, [["POST", "/api/telemetry/devices/741/activate", {}]]);
  }
  for (const envelope of [null, [], "invalid", 1, {}, new Date(), Object.create({ success: true, data: receipt }), { Success: true, data: receipt }, { success: "true", data: receipt }, { success: 1, data: receipt }, { success: 0, data: receipt }]) {
    const fixture = loadService({ envelope });
    await assert.rejects(fixture.telematicsService.activateDevice(741), error => error.outcome === "unconfirmed");
    assert.equal(fixture.calls.length, 1);
  }
  const nullReceipt = Object.assign(Object.create(null), receipt);
  const nullEnvelope = Object.assign(Object.create(null), { success: true, data: nullReceipt });
  assert.equal((await loadService({ envelope: nullEnvelope }).telematicsService.activateDevice(741)).id, "741");
});

test("canonical long IDs retain precision and invalid request IDs make zero transport calls", async () => {
  const fixture = loadService({ data: { ...receipt, id: "9223372036854775807" } });
  assert.equal((await fixture.telematicsService.activateDevice("9223372036854775807")).id, "9223372036854775807");
  for (const id of [0, -1, 1.5, Number.MAX_SAFE_INTEGER + 1, "0", "01", "+741", "741.0", "1e3", " 741", "741/suspend", "9223372036854775808"]) {
    const invalid = loadService();
    await assert.rejects(invalid.telematicsService.activateDevice(id));
    assert.deepEqual(invalid.calls, []);
  }
});

test("authorization remains before transport; explicit rejection differs from timeout or server ambiguity", async () => {
  for (const role of ["Driver", "Customer"]) {
    const fixture = loadService({ session: { role } });
    await assert.rejects(fixture.telematicsService.activateDevice(741), /Permission denied/);
    assert.deepEqual(fixture.calls, []);
  }
  for (const fixture of [loadService({ envelope: { success: false, error: "untrusted raw detail" } }),
    loadService({ failure: { response: { status: 409, data: { success: false, message: "untrusted raw detail" } } } })]) {
    await assert.rejects(fixture.telematicsService.activateDevice(741), error => error.outcome === "rejected" && !error.message.includes("untrusted raw detail"));
    assert.equal(fixture.calls.length, 1);
  }
  for (const failure of [new Error("untrusted raw detail"), { response: { status: 500, data: { success: false } } }]) {
    const fixture = loadService({ failure });
    await assert.rejects(fixture.telematicsService.activateDevice(741), error => error.outcome === "unconfirmed" && !error.message.includes("untrusted raw detail"));
    assert.equal(fixture.calls.length, 1);
  }
});

test("actual mutation disables retries and uses receipt ID with distinct fresh versus replay acknowledgement", async () => {
  for (const idempotentReplay of [false, true]) {
    const refresh = loadRefresh({ fails: true });
    const mutation = loadMutation(refresh.refresh, refresh.context);
    assert.equal(mutation.options.retry, false);
    await mutation.options.onSuccess({ id: "741", status: "Active", deviceState: null, rowVersion: 2, idempotentReplay });
    assert.equal(mutation.states.receiptId, "741");
    assert.equal(mutation.states.notice, idempotentReplay ? "Activation already recorded for device 741." : "Activation recorded for device 741.");
    assert.equal(refresh.states.warning, true);
    assert.equal(refresh.states.pending, false);
    assert.deepEqual(mutation.calls, []);
  }
});

test("installed QueryClient separates acknowledged action from every detail and optional feed read failure", async () => {
  for (const getFailureUrl of ["/api/telemetry/devices/741", "/api/maintenance/fault-codes", "/api/telemetry/alerts", "/api/telemetry/positions"]) {
    const fixture = loadService({ getFailureUrl, session: { role: "Tenant Admin", permissions: ["maintenance:view", "telemetry.alerts.read", "telemetry.live_state.read"] } });
    const ack = await fixture.telematicsService.activateDevice(741);
    const client = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: Infinity, gcTime: Infinity } } });
    const key = ["telematics", "device", "741"];
    client.setQueryData(key, { priorDisplayRecord: true });
    const observer = new QueryObserver(client, { queryKey: key, queryFn: () => fixture.telematicsService.getDeviceById(741), staleTime: Infinity, retry: false });
    const unsubscribe = observer.subscribe(() => {});
    try {
      const refresh = loadRefresh({ client });
      const mutation = loadMutation(refresh.refresh, refresh.context);
      await mutation.options.onSuccess(ack);
      assert.equal(mutation.states.notice, "Activation recorded for device 741.");
      assert.equal(refresh.states.warning, true, getFailureUrl);
      assert.equal(client.getQueryState(key).status, "error");
      assert.deepEqual(client.getQueryData(key), { priorDisplayRecord: true });
      assert.equal(fixture.calls.filter(call => call[0] === "POST").length, 1);
      assert.ok(fixture.calls.some(call => call[0] === "GET" && call[1] === getFailureUrl));
    } finally { unsubscribe(); client.clear(); }
  }
  for (const scenario of ["initially-offline", "retry-paused"]) {
    onlineManager.setOnline(true);
    let fail = false;
    const client = new QueryClient({ defaultOptions: { queries: { retry: 1, retryDelay: 0, staleTime: Infinity } } });
    client.mount();
    const key = ["telematics", "device", "741"];
    client.setQueryData(key, { priorDisplayRecord: true });
    const observer = new QueryObserver(client, { queryKey: key, queryFn: async () => {
      if (fail) { if (scenario === "retry-paused") onlineManager.setOnline(false); throw new Error("fixture read unavailable"); }
      return { fresh: true };
    }, retry: 1, retryDelay: 0, staleTime: Infinity });
    const unsubscribe = observer.subscribe(() => {});
    const refresh = loadRefresh({ client });
    try {
      if (scenario === "initially-offline") onlineManager.setOnline(false); else fail = true;
      await refresh.refresh("741");
      assert.equal(refresh.states.warning, true, scenario);
      assert.equal(refresh.states.pending, false, scenario);
      assert.equal(refresh.states.subscriptions, 0, scenario);
    } finally {
      onlineManager.setOnline(true); await client.cancelQueries(undefined, { silent: true }); unsubscribe(); client.unmount(); client.clear(); await tick();
    }
  }
});

test("read-only retry can clear display warning without dispatching activation", async () => {
  const refresh = loadRefresh();
  refresh.states.warning = true;
  await refresh.refresh("741");
  assert.equal(refresh.states.warning, false);
  assert.equal(refresh.states.pending, false);
  assert.deepEqual(refresh.calls, [[{ queryKey: ["telematics"] }, { throwOnError: true }], [{ queryKey: ["iot-devices"] }, { throwOnError: true }]]);
});

test("actual activation callback guards same-turn duplicate calls and releases after success and rejection", async () => {
  const hook = readFileSync(resolve(root, "src/hooks/useSingleFlight.ts"), "utf8").replace('import { useCallback, useRef } from "react";', "");
  const holder = { exports: {} };
  const code = esbuild.transformSync(hook, { loader: "ts", format: "cjs" }).code;
  new Function("module", "exports", "useCallback", "useRef", code)(holder, holder.exports, callback => callback, initial => ({ current: initial }));
  const pending = [];
  let posts = 0;
  const issue = () => { posts++; return new Promise((resolve, reject) => pending.push({ resolve, reject })); };
  const expression = declaration("runActivation");
  assert.ok(expression, "actual action must route through synchronous admission");
  const run = evaluate(expression, { canManageDeviceLifecycle: true, activateSingleFlight: holder.exports.useSingleFlight(), activateMut: { mutateAsync: issue } });
  run(741); run(741);
  assert.equal(posts, 1);
  pending[0].resolve();
  await new Promise(resolve => setImmediate(resolve));
  run(741); run(741);
  assert.equal(posts, 2);
  pending[1].reject(new Error("fixture rejected"));
  await new Promise(resolve => setImmediate(resolve));
  run(741);
  assert.equal(posts, 3);
  pending[2].resolve();
  await new Promise(resolve => setImmediate(resolve));
  const denied = evaluate(expression, { canManageDeviceLifecycle: false, activateSingleFlight: holder.exports.useSingleFlight(), activateMut: { mutateAsync: issue } });
  denied(741);
  assert.equal(posts, 3);
  assert.match(pageSource, /onActivate:\s*\(\) => runActivation\(selectedRecord\.id\)/);
});

test("actual warning expression qualifies cache, labels receipt ID, and exposes only read retry", () => {
  const fn = page.statements.find(node => ts.isFunctionDeclaration(node) && node.name?.text === "ActivationRefreshNotice");
  assert.ok(fn);
  const h = (type, props, ...children) => ({ type, props: props ?? {}, children: children.flat(Infinity) });
  const component = evaluate(fn.getText(page), { h }, { component: true }).ActivationRefreshNotice;
  let reads = 0;
  const nodes = [];
  const text = node => {
    if (node == null || typeof node === "boolean") return "";
    if (typeof node !== "object") return String(node);
    nodes.push(node);
    return node.children.map(text).join(" ");
  };
  const message = text(component({ deviceId: "741", busy: false, onRetry: () => { reads++; } }));
  assert.match(message, /Activation for device\s+741\s+is recorded/);
  assert.match(message, /may be out of date/);
  assert.doesNotMatch(message, /physically verified|streaming|certified|ingestion is enabled/);
  nodes.find(node => node.type === "button").props.onClick();
  assert.equal(reads, 1);
  nodes.length = 0;
  text(component({ deviceId: "741", busy: true, onRetry: () => { reads++; } }));
  assert.equal(nodes.find(node => node.type === "button").props.disabled, true);
  assert.equal((pageSource.match(/<ActivationRefreshNotice deviceId=\{activationReceiptId\}/g) ?? []).length, 2);
});

test("actual page and drawer distinguish unconfirmed activation, rejected activation and existing suspension errors", () => {
  const fixture = loadService();
  const fn = page.statements.find(node => ts.isFunctionDeclaration(node) && node.name?.text === "lifecycleFailureHeading");
  const heading = evaluate(fn.getText(page), { DeviceInstallationOutcomeError: fixture.DeviceInstallationOutcomeError, DeviceRemovalOutcomeError: fixture.DeviceRemovalOutcomeError, DeviceCommissioningOutcomeError: fixture.DeviceCommissioningOutcomeError, DeviceActivationOutcomeError: fixture.DeviceActivationOutcomeError, DeviceSuspensionOutcomeError: fixture.DeviceSuspensionOutcomeError }, { component: true }).lifecycleFailureHeading;
  assert.equal(heading(new fixture.DeviceActivationOutcomeError("unconfirmed")), "Activation outcome unconfirmed");
  assert.equal(heading(new fixture.DeviceActivationOutcomeError("rejected")), "Activation request rejected");
  assert.equal(heading(new fixture.DeviceSuspensionOutcomeError("unconfirmed")), "Suspension outcome unconfirmed");
  assert.equal(heading(new Error("other")), "Device lifecycle action failed");
  assert.equal((pageSource.match(/lifecycleFailureHeading\(lifecycleError\)/g) ?? []).length, 2);
});

test("older activation refresh completion cannot clear a newer warning", async () => {
  const pending = [];
  const refresh = loadRefresh({ invalidate: () => new Promise((resolve, reject) => pending.push({ resolve, reject })) });
  const older = refresh.refresh("741");
  const newer = refresh.refresh("741");
  pending[2].reject(new Error("newer read failed")); pending[3].resolve();
  await newer;
  assert.equal(refresh.states.warning, true);
  pending[0].resolve(); pending[1].resolve();
  await older;
  assert.equal(refresh.states.warning, true);
});

test("new activation attempt invalidates stale target completion and retry closure", async () => {
  const pending = [];
  const refresh = loadRefresh({ invalidate: () => new Promise((resolve, reject) => pending.push({ resolve, reject })) });
  const older = refresh.refresh("741");
  const mutation = loadMutation(refresh.refresh, refresh.context);
  mutation.options.onMutate();
  refresh.context.current.deviceId = "742";
  pending[0].reject(new Error("old target failed")); pending[1].resolve();
  await older;
  assert.equal(refresh.states.warning, false);
  assert.equal(mutation.states.receiptId, null);
  await refresh.refresh("741");
  assert.equal(refresh.calls.length, 2);
});
