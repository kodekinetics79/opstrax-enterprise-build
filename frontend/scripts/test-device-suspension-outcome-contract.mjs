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

const receipt = { id: 741, status: "Suspended", deviceState: "Suspended", rowVersion: 2, updatedAt: "2026-01-01T00:00:00Z" };
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
      return { data: { success: true, data: args[0].endsWith("/741") ? { device: { id: 741, status: "Suspended" } } : [] } };
    },
    put: () => assert.fail("suspension must not issue PUT"),
    delete: () => assert.fail("suspension must not issue DELETE"),
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
  const expression = declaration("refreshSuspensionDisplay");
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
    setSuspensionRefreshWarning: value => { states.warning = value; },
    setSuspensionRefreshPending: value => { states.pending = value; },
    suspensionRefreshContext: context,
  });
  return { refresh, states, calls, context };
}

function loadMutation(refresh, context = { current: { deviceId: "741", generation: 0 } }) {
  const states = { notice: null, confirmation: "open", warning: false, receiptId: null, pending: false };
  const calls = [];
  const options = evaluate(declaration("suspendMut"), {
    useMutation: options => options,
    telematicsService: { suspendDevice: async id => { calls.push(["POST", id]); return receipt; } },
    setConfirmTarget: value => { states.confirmation = value; },
    setNotice: value => { states.notice = value; },
    setSuspensionRefreshWarning: value => { states.warning = value; },
    setSuspensionRefreshPending: value => { states.pending = value; },
    setSuspensionReceiptId: value => { states.receiptId = value; },
    suspensionRefreshContext: context,
    refreshSuspensionDisplay: refresh,
    refreshAll: refresh,
  });
  return { options, states, calls };
}

test("known receipt resolves independently of unavailable follow-up reads and never constructs a device record", async () => {
  const fixture = loadService();
  const result = await fixture.telematicsService.suspendDevice(741);
  assert.deepEqual(result, { id: "741", status: "Suspended", deviceState: "Suspended", rowVersion: 2 });
  assert.deepEqual(fixture.calls, [["POST", "/api/telemetry/devices/741/suspend", {}]]);
  assert.equal(Object.hasOwn(result, "device"), false);
  assert.equal(Object.hasOwn(result, "connectionStatus"), false);
});

test("idempotent receipt without timestamp and actual zero row version remain valid", async () => {
  const fixture = loadService({ data: { id: "741", status: "Suspended", device_state: "Suspended", row_version: 0, idempotentReplay: true } });
  const result = await fixture.telematicsService.suspendDevice("741");
  assert.equal(result.rowVersion, 0);
  assert.equal(Object.hasOwn(result, "updatedAt"), false);
  assert.equal(fixture.calls.length, 1);
});

test("canonical long string IDs are compared without lossy numeric conversion", async () => {
  const fixture = loadService({ data: { ...receipt, id: "9223372036854775807" } });
  assert.equal((await fixture.telematicsService.suspendDevice("9223372036854775807")).id, "9223372036854775807");
});

test("invalid requested IDs never dispatch a command", async () => {
  for (const id of [0, -1, 1.5, Number.MAX_SAFE_INTEGER + 1, "0", "01", "+741", "741.0", "1e3", " 741", "741/activate", "9223372036854775808"]) {
    const fixture = loadService();
    await assert.rejects(fixture.telematicsService.suspendDevice(id));
    assert.deepEqual(fixture.calls, [], String(id));
  }
});

test("missing, mismatched or malformed receipts remain unconfirmed without subsequent GET or repeated POST", async () => {
  class ReceiptCarrier { constructor() { Object.assign(this, receipt); } }
  for (const data of [null, {}, [], new Date(), new ReceiptCarrier(), Object.create(receipt),
    { ...receipt, ID: 742 }, { ...receipt, Status: "Active" }, { ...receipt, Device_State: "Registered" }, { ...receipt, RowVersion: 3 },
    { ...receipt, id: 742 }, { ...receipt, id: "0741" }, { ...receipt, id: Number.MAX_SAFE_INTEGER + 1 }, { ...receipt, status: "Active" }, { ...receipt, deviceState: "Registered" }, { ...receipt, deviceState: "Active", device_state: "Suspended" }, { ...receipt, row_version: 3 }, { ...receipt, rowVersion: -1 }, { ...receipt, rowVersion: 1.1 }, { ...receipt, rowVersion: "2" }, { ...receipt, rowVersion: Number.MAX_SAFE_INTEGER + 1 }]) {
    const fixture = loadService({ data });
    await assert.rejects(fixture.telematicsService.suspendDevice(741), error => error.outcome === "unconfirmed");
    assert.deepEqual(fixture.calls, [["POST", "/api/telemetry/devices/741/suspend", {}]]);
  }
});

test("explicit rejection stays distinct from missing, malformed or lost responses", async () => {
  for (const fixture of [loadService({ envelope: { success: false, data: null } }), loadService({ failure: { response: { status: 409, data: { success: false, data: null } } } })]) {
    await assert.rejects(fixture.telematicsService.suspendDevice(741), error => error.outcome === "rejected");
    assert.equal(fixture.calls.length, 1);
  }
  for (const fixture of [
    ...[null, [], "invalid", 1, {}, new Date(), Object.create({ success: true, data: receipt }),
      { Success: true, data: receipt }, { success: "true", data: receipt }, { success: 1, data: receipt }, { success: 0, data: receipt }].map(envelope => loadService({ envelope })),
    loadService({ failure: new Error("fixture timeout") }),
    loadService({ failure: { response: { status: 500, data: { success: false } } } }),
  ]) {
    await assert.rejects(fixture.telematicsService.suspendDevice(741), error => error.outcome === "unconfirmed");
    assert.equal(fixture.calls.length, 1);
  }
  const nullReceipt = Object.assign(Object.create(null), receipt);
  const nullEnvelope = Object.assign(Object.create(null), { success: true, data: nullReceipt });
  assert.equal((await loadService({ envelope: nullEnvelope }).telematicsService.suspendDevice(741)).id, "741");
});

test("existing portal authorization refuses before transport", async () => {
  for (const role of ["Driver", "Customer"]) {
    const fixture = loadService({ session: { role } });
    await assert.rejects(fixture.telematicsService.suspendDevice(741), /Permission denied/);
    assert.deepEqual(fixture.calls, []);
  }
});

test("the actual suspension mutation disables automatic retries", () => {
  assert.equal(loadMutation(async () => {}).options.retry, false);
});

test("actual acknowledged callback and refresh helper keep success when display invalidation fails", async () => {
  const refresh = loadRefresh({ fails: true });
  const mutation = loadMutation(refresh.refresh, refresh.context);
  await mutation.options.onSuccess({ ...receipt, id: "741" });
  assert.equal(mutation.states.confirmation, null);
  assert.equal(mutation.states.notice, "Suspension recorded for device 741.");
  assert.equal(mutation.states.receiptId, "741");
  assert.equal(refresh.states.warning, true);
  assert.equal(refresh.states.pending, false);
  assert.deepEqual(mutation.calls, [], "acknowledgement callback must never redispatch POST");
  assert.deepEqual(refresh.calls, [[{ queryKey: ["telematics"] }, { throwOnError: true }], [{ queryKey: ["iot-devices"] }, { throwOnError: true }]]);
});

test("successful read-only retry clears display warning and does not repeat suspension", async () => {
  const refresh = loadRefresh();
  refresh.states.warning = true;
  await refresh.refresh("741");
  assert.equal(refresh.states.warning, false);
  assert.equal(refresh.states.pending, false);
  assert.equal(refresh.calls.length, 2);
});

test("real QueryClient propagates each actual detail or optional-feed failure without changing command acknowledgement", async () => {
  for (const getFailureUrl of ["/api/telemetry/devices/741", "/api/maintenance/fault-codes", "/api/telemetry/alerts", "/api/telemetry/positions"]) {
    const fixture = loadService({ getFailureUrl, session: { role: "Tenant Admin", permissions: ["maintenance:view", "telemetry.alerts.read", "telemetry.live_state.read"] } });
    const acknowledged = await fixture.telematicsService.suspendDevice(741);
    const client = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: Infinity, gcTime: Infinity } } });
    const key = ["telematics", "device", "741"];
    client.setQueryData(key, { priorDisplayRecord: true });
    const observer = new QueryObserver(client, { queryKey: key, queryFn: () => fixture.telematicsService.getDeviceById(741), staleTime: Infinity, retry: false });
    const unsubscribe = observer.subscribe(() => {});
    try {
      const refresh = loadRefresh({ client });
      const mutation = loadMutation(refresh.refresh, refresh.context);
      await mutation.options.onSuccess(acknowledged);
      assert.equal(mutation.states.notice, "Suspension recorded for device 741.");
      assert.equal(refresh.states.warning, true, getFailureUrl);
      assert.equal(client.getQueryState(key).status, "error");
      assert.deepEqual(client.getQueryData(key), { priorDisplayRecord: true }, "retained display data must be qualified by the warning");
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

test("same-turn suspension confirmations admit one request and release admission after settlement", async () => {
  const hook = readFileSync(resolve(root, "src/hooks/useSingleFlight.ts"), "utf8").replace('import { useCallback, useRef } from "react";', "");
  const holder = { exports: {} };
  const code = esbuild.transformSync(hook, { loader: "ts", format: "cjs" }).code;
  new Function("module", "exports", "useCallback", "useRef", code)(holder, holder.exports, callback => callback, initial => ({ current: initial }));
  const releases = [];
  let posts = 0;
  const issue = () => { posts++; return new Promise(resolve => releases.push(resolve)); };
  const run = evaluate(declaration("runConfirmedAction"), {
    confirmTarget: { action: "suspend", device: { id: 741 } }, canManageDeviceLifecycle: true, canDelete: false,
    suspendMut: { mutate: issue, mutateAsync: issue },
    suspendSingleFlight: holder.exports.useSingleFlight(),
  });
  run(); run();
  assert.equal(posts, 1, "render-lagged isPending must not admit a duplicate");
  releases[0]();
  await new Promise(resolve => setImmediate(resolve));
  run();
  assert.equal(posts, 2, "a later intentional action may enter after settlement");
  releases[1]();
  await new Promise(resolve => setImmediate(resolve));
});

test("actual warning renders cached-data qualification and dispatches only its read-refresh callback", () => {
  const fn = page.statements.find(node => ts.isFunctionDeclaration(node) && node.name?.text === "SuspensionRefreshNotice");
  assert.ok(fn, "the display warning is a distinct actual component");
  const h = (type, props, ...children) => ({ type, props: props ?? {}, children: children.flat(Infinity) });
  const component = evaluate(fn.getText(page), { h }, { component: true }).SuspensionRefreshNotice;
  let refreshes = 0;
  const tree = component({ deviceId: "741", busy: false, onRetry: () => { refreshes++; } });
  const nodes = [];
  const text = node => {
    if (node == null || typeof node === "boolean") return "";
    if (typeof node !== "object") return String(node);
    nodes.push(node);
    return node.children.map(text).join(" ");
  };
  const message = text(tree);
  assert.match(message, /Suspension for device\s+741\s+was recorded/);
  assert.match(message, /may be out of date/);
  assert.doesNotMatch(message, /action failed|device is streaming|ingestion is blocked/);
  const button = nodes.find(node => node.type === "button");
  assert.ok(button);
  button.props.onClick();
  assert.equal(refreshes, 1);
  const busyTree = component({ deviceId: "741", busy: true, onRetry: () => { refreshes++; } });
  nodes.length = 0;
  text(busyTree);
  assert.equal(nodes.find(node => node.type === "button").props.disabled, true);
});

test("actual error heading distinguishes unconfirmed suspension from known rejection in both page and drawer", () => {
  const fixture = loadService();
  const fn = page.statements.find(node => ts.isFunctionDeclaration(node) && node.name?.text === "lifecycleFailureHeading");
  assert.ok(fn);
  const heading = evaluate(fn.getText(page), { DeviceInstallationOutcomeError: fixture.DeviceInstallationOutcomeError, DeviceRemovalOutcomeError: fixture.DeviceRemovalOutcomeError, DeviceCommissioningOutcomeError: fixture.DeviceCommissioningOutcomeError, DeviceSuspensionOutcomeError: fixture.DeviceSuspensionOutcomeError, DeviceActivationOutcomeError: fixture.DeviceActivationOutcomeError }, { component: true }).lifecycleFailureHeading;
  assert.equal(heading(new fixture.DeviceSuspensionOutcomeError("unconfirmed")), "Suspension outcome unconfirmed");
  assert.equal(heading(new fixture.DeviceSuspensionOutcomeError("rejected")), "Suspension request rejected");
  assert.equal(heading(new Error("other lifecycle error")), "Device lifecycle action failed");
  assert.equal((pageSource.match(/lifecycleFailureHeading\(lifecycleError\)/g) ?? []).length, 2);
  assert.equal((pageSource.match(/<SuspensionRefreshNotice/g) ?? []).length, 2, "warning must also survive the drawer detail error branch");
  assert.equal((pageSource.match(/<SuspensionRefreshNotice deviceId=\{suspensionReceiptId\}/g) ?? []).length, 2, "neither drawer nor page may relabel acknowledgement with current selection");
});

test("an older successful refresh cannot clear a newer failed refresh warning", async () => {
  const pending = [];
  const refresh = loadRefresh({ invalidate: () => new Promise((resolve, reject) => pending.push({ resolve, reject })) });
  const older = refresh.refresh("741");
  const newer = refresh.refresh("741");
  pending[2].reject(new Error("newer refresh failed")); pending[3].resolve();
  await newer;
  assert.equal(refresh.states.warning, true);
  pending[0].resolve(); pending[1].resolve();
  await older;
  assert.equal(refresh.states.warning, true);
});

test("new target attempt invalidates older refresh completion and stale retry closures", async () => {
  const pending = [];
  const refresh = loadRefresh({ invalidate: () => new Promise((resolve, reject) => pending.push({ resolve, reject })) });
  const older = refresh.refresh("741");
  const mutation = loadMutation(refresh.refresh, refresh.context);
  mutation.options.onMutate();
  refresh.context.current.deviceId = "742";
  pending[0].reject(new Error("old target refresh failed")); pending[1].resolve();
  await older;
  assert.equal(refresh.states.warning, false);
  assert.equal(mutation.states.receiptId, null);
  await refresh.refresh("741");
  assert.equal(refresh.calls.length, 2, "stale target retry must not dispatch reads or overwrite newer target status");
});
