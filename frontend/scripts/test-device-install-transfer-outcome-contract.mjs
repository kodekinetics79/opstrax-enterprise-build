import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { createRequire } from "node:module";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";

const require = createRequire(import.meta.url);
const esbuild = require("esbuild");
const ts = require("typescript");
const { QueryClient, QueryObserver, MutationObserver, onlineManager } = require("@tanstack/react-query");
const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const source = readFileSync(resolve(root, "src/services/telematicsService.ts"), "utf8");
const pageSource = readFileSync(resolve(root, "src/pages/IotDevicesPage.tsx"), "utf8");
const page = ts.createSourceFile("page.tsx", pageSource, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
const built = await esbuild.build({ stdin: { contents: source
  .replace('import { apiClient, unwrap } from "@/services/apiClient";', 'import { unwrap } from "@/services/apiClient"; const apiClient = __api;')
  .replace('import { readRawSession } from "@/auth/sessionStorage";', 'const readRawSession = () => JSON.stringify(__session);'), loader: "ts", resolveDir: root },
  bundle: true, platform: "node", format: "cjs", write: false, alias: { "@": resolve(root, "src") }, define: { "import.meta.env": "{}" }, logLevel: "silent" });
const effectiveAt = "2026-01-02T00:00:00.000Z";
const prior = { id: 902, deviceId: 741, vehicleId: 41, status: "Installed", rowVersion: 2, effectiveFrom: "2026-01-01T00:00:00.000Z" };
const createIntent = { kind: "create" };
const transferIntent = { kind: "transfer", currentInstallationId: "902", expectedRowVersion: 2, priorVehicleId: "41" };
const input = { intent: createIntent, vehicleId: "42", deviceRole: "GPS", isPrimary: true, effectiveAt,
  installationLocation: "fixture bay", odometerAtInstallation: 123, commissioningMethod: "fixture method", assignmentReason: "fixture assignment", removalReason: "fixture transfer" };
const feeds = ["/api/telemetry/devices/741", "/api/maintenance/fault-codes", "/api/telemetry/alerts", "/api/telemetry/positions"];
const session = { role: "Tenant Admin", permissions: ["maintenance:view", "telemetry.alerts.read", "telemetry.live_state.read"] };
const receipts = {
  createFresh: { id: 903, deviceId: 741, vehicleId: 42, status: "Installed", effectiveFrom: effectiveAt, rowVersion: 1 },
  createExisting: { id: 903, deviceId: 741, vehicleId: 42, status: "Removed", rowVersion: 3, deviceRole: "GPS", isPrimary: true },
  transferFresh: { id: 903, priorInstallationId: 902, vehicleId: 42, status: "Installed", effectiveFrom: effectiveAt },
  transferExisting: { id: 903, replacedInstallationId: 902, deviceId: 741, vehicleId: 42, status: "Removed", rowVersion: 3, effectiveFrom: effectiveAt },
};
const variants = Object.keys(receipts);
const isTransfer = variant => variant.startsWith("transfer");
const invocation = (variant, changes = {}) => ({ ...input, intent: isTransfer(variant) ? { ...transferIntent } : { ...createIntent }, ...changes });
const expected = variant => ({ operation: isTransfer(variant) ? "transfer" : "create", acknowledgement: variant.endsWith("Fresh") ? "recorded" : "already-recorded",
  installationId: "903", vehicleId: "42", priorInstallationId: isTransfer(variant) ? "902" : null, recordedStatus: receipts[variant].status,
  effectiveFrom: variant === "createExisting" ? null : effectiveAt });

function loadService(options = {}) {
  const variant = options.variant ?? "createFresh";
  const calls = [];
  let acknowledged = false;
  const current = Object.hasOwn(options, "current") ? options.current : isTransfer(variant) ? prior : null;
  const api = {
    get: async (url, params) => {
      const stage = acknowledged ? "after" : "before";
      calls.push(["GET", stage, url, params]);
      await options.beforeGet?.(url, stage);
      if (stage === options.failStage && (!options.failUrl || url === options.failUrl)) throw new Error("fixture private detail failure");
      return { data: { success: true, data: /\/api\/telemetry\/devices\//.test(url)
        ? { id: options.deviceId ?? 741, status: "Active", ...options.device, currentInstallation: current, installationHistory: options.history ?? [] } : [] } };
    },
    post: async (url, body) => {
      calls.push(["POST", url, body]);
      await options.beforePost?.(url, body);
      if (options.failure) throw options.failure;
      acknowledged = true;
      const data = Object.hasOwn(options, "data") ? options.data : receipts[variant];
      return { status: Object.hasOwn(options, "status") ? options.status : variant === "createFresh" ? 201 : 200,
        data: Object.hasOwn(options, "envelope") ? options.envelope : { success: true, data } };
    },
  };
  const holder = { exports: {} };
  new Function("require", "module", "exports", "__api", "__session", "window", built.outputFiles[0].text)(require, holder, holder.exports, api, options.session ?? session,
    { location: { hostname: "example.invalid" }, localStorage: { getItem: () => null }, sessionStorage: { getItem: () => null } });
  return { ...holder.exports, calls };
}
const posts = fixture => fixture.calls.filter(call => call[0] === "POST");
const tick = () => new Promise(resolve => setImmediate(resolve));
function declaration(name) {
  let result;
  function visit(node) { if (ts.isVariableDeclaration(node) && node.name.getText(page) === name) result = node.initializer?.getText(page); ts.forEachChild(node, visit); }
  visit(page); return result;
}
function evaluate(expression, scope, component = false) {
  assert.ok(expression, "actual page expression must exist");
  const holder = { exports: {} };
  const code = esbuild.transformSync(component ? "export " + expression : "export const value = " + expression + ";", { loader: "tsx", format: "cjs", jsxFactory: "h" }).code;
  new Function("module", "exports", ...Object.keys(scope), code)(holder, holder.exports, ...Object.values(scope));
  return component ? holder.exports : holder.exports.value;
}
function functionSource(name) { return page.statements.find(node => ts.isFunctionDeclaration(node) && node.name?.text === name).getText(page); }
const assignmentRecordedMessage = evaluate(functionSource("assignmentRecordedMessage"), {}, true).assignmentRecordedMessage;

function singleFlight() {
  const source = readFileSync(resolve(root, "src/hooks/useSingleFlight.ts"), "utf8").replace('import { useCallback, useRef } from "react";', "");
  const holder = { exports: {} };
  const code = esbuild.transformSync(source, { loader: "ts", format: "cjs" }).code;
  new Function("module", "exports", "useCallback", "useRef", code)(holder, holder.exports, callback => callback, initial => ({ current: initial }));
  return holder.exports.useSingleFlight();
}
const actualToUtcIso = evaluate(functionSource("toUtcIso"), {}, true).toUtcIso;
const target = { id: "741", assignedVehicleId: "", currentInstallationId: null, currentInstallationRowVersion: undefined };
const transferTarget = { ...target, assignedVehicleId: "41", currentInstallationId: "902", currentInstallationRowVersion: 2 };
const formInput = { ...input, primaryDesignation: "primary", odometerAtInstallation: "123" };
const record = { ...expected("createFresh"), deviceId: "741" };

function loadRefresh({ client, invalidate, fail = false } = {}) {
  const states = { warning: false, pending: false, subscriptions: 0 };
  const context = { current: { record, generation: 0 } };
  const permission = { current: true };
  const calls = [];
  const fallback = { queryHash: "fixture", state: { fetchStatus: "idle", status: "success" }, isActive: () => true };
  const backingCache = client?.getQueryCache();
  const cache = {
    findAll: filters => backingCache ? backingCache.findAll(filters) : [fallback],
    get: hash => backingCache ? backingCache.get(hash) : fallback,
    subscribe: callback => { states.subscriptions++; const cleanup = backingCache?.subscribe(callback); return () => { states.subscriptions--; cleanup?.(); }; },
  };
  const queryClient = { getQueryCache: () => cache, invalidateQueries: async (...args) => {
    calls.push(args);
    if (invalidate) return invalidate(...args);
    if (client) return client.invalidateQueries(...args);
    if (fail) throw new Error("fixture display unavailable");
  } };
  const refreshReceiptQueries = evaluate(declaration("refreshReceiptQueries"), { queryClient });
  const refresh = evaluate(declaration("refreshAssignmentDisplay"), {
    assignmentRefreshContext: context, lifecyclePermissionRef: permission,
    setAssignmentRefreshWarning: value => { states.warning = value; },
    setAssignmentRefreshPending: value => { states.pending = value; },
    queryClient, refreshReceiptQueries,
  });
  return { states, context, calls, refresh, permission };
}

function loadUi({ operation = async () => expected("createFresh"), refresh = loadRefresh(), allowed = true } = {}) {
  const defaults = evaluate(declaration("defaultInstallationForm"), {});
  const states = { target: null, form: defaults, error: null, formError: null, record: null, notice: null };
  const session = { current: { deviceId: null, intent: null, generation: 0, pending: false } };
  const snapshot = { current: defaults };
  const lifecyclePermissionRef = { current: allowed };
  const assignmentTargetRef = { current: null };
  const scope = {
    assignmentSession: session, assignmentFormSnapshot: snapshot, defaultInstallationForm: defaults,
    assignmentTargetRef,
    setAssignTarget: value => { states.target = value; assignmentTargetRef.current = value; }, setInstallationForm: value => { states.form = value; },
    setFormError: value => { states.formError = value; }, setAssignmentError: value => { states.error = value; },
    setNotice: value => { states.notice = value; }, setAssignmentRecord: value => { states.record = value; },
    setAssignmentRefreshWarning: value => { refresh.states.warning = value; }, setAssignmentRefreshPending: value => { refresh.states.pending = value; },
    assignmentRefreshContext: refresh.context, refreshAssignmentDisplay: refresh.refresh,
    canGovernInstallations: allowed, lifecyclePermissionRef, assignmentSingleFlight: singleFlight(), toUtcIso: actualToUtcIso,
    assignmentRecordedMessage, getInstallationIntent: loadService().getInstallationIntent,
  };
  scope.ownsAssignmentSession = evaluate(declaration("ownsAssignmentSession"), scope);
  const options = evaluate(declaration("assignMut"), { ...scope, useMutation: value => value, telematicsService: { assignDeviceToVehicle: operation } });
  const client = new QueryClient({ defaultOptions: { mutations: { retry: 3 } } });
  const observer = new MutationObserver(client, options);
  const submitted = [];
  const counters = { resets: 0 };
  const assignMut = { mutateAsync: variables => { submitted.push(variables); return observer.mutate(variables); }, reset: () => { counters.resets++; observer.reset(); } };
  const controls = () => {
    const rendered = { ...scope, assignMut, assignTarget: states.target, installationForm: states.form, assignmentRenderGeneration: session.current.generation };
    return Object.fromEntries(["openInstallation", "closeInstallation", "submitInstallation", "updateInstallationForm"].map(name => [name, evaluate(declaration(name), rendered)]));
  };
  const dismiss = evaluate(declaration("clearLifecycleError"), { ...scope, assignMut,
    removalSession: { current: { pending: false } }, commissioningSession: { current: { pending: false } },
    suspensionSession: { current: { pending: false } }, activationSession: { current: { pending: false } },
    unassignMut: { reset() {} }, installMut: { reset() {} }, suspendMut: { reset() {} }, activateMut: { reset() {} }, rotateSecretMut: { reset() {} },
    setRemovalError() {}, setCommissioningError() {}, setSuspensionError() {}, setActivationError() {},
  });
  return { states, session, snapshot, lifecyclePermissionRef, scope, options, controls, refresh, submitted, observer, client, counters, dismiss };
}

test("all four actual receipt variants survive failed readback with one POST and no reconstructed device", async t => {
  for (const variant of variants) await t.test(variant, async () => {
    const fixture = loadService({ variant, failStage: "after" });
    assert.deepEqual(await fixture.telematicsService.assignDeviceToVehicle(741, invocation(variant)), expected(variant));
    assert.equal(fixture.calls.filter(c => c[0] === "GET").length, 4);
    assert.equal(posts(fixture).length, 1);
    const [, route, body] = posts(fixture)[0];
    assert.equal(route, "/api/telemetry/devices/741/installations" + (isTransfer(variant) ? "/transfer" : ""));
    assert.equal(body.vehicleId, 42);
    assert.equal(Object.hasOwn(body, "intent"), false);
    assert.equal(Object.hasOwn(body, "priorVehicleId"), false);
    assert.equal(typeof body.idempotencyKey, "string");
    assert.equal(body.assignmentReason, input.assignmentReason);
  });
});

test("actual assign mutation cannot turn a matching POST into failure after its later service read", async () => {
  const fixture = loadService({ failStage: "after" });
  const states = { target: { id: "741" }, form: { ...input }, notice: null, record: null };
  const assignmentSession = { current: { deviceId: "741", generation: 1, pending: true, intent: createIntent } };
  const scope = { useMutation: value => value, telematicsService: fixture.telematicsService,
    setAssignTarget: value => { states.target = value; }, setInstallationForm: value => { states.form = value; },
    setFormError() {}, setAssignmentError() {}, setNotice: value => { states.notice = value; },
    setAssignmentRecord: value => { states.record = value; }, setAssignmentRefreshWarning() {}, setAssignmentRefreshPending() {},
    assignmentSession, assignmentRecordedMessage, assignmentFormSnapshot: { current: states.form }, ownsAssignmentSession: () => true, assignmentRefreshContext: { current: { record: null, generation: 0 } },
    refreshAll: async () => {}, refreshAssignmentDisplay: async () => {}, defaultInstallationForm: {},
  };
  const options = evaluate(declaration("assignMut"), scope);
  const client = new QueryClient({ defaultOptions: { mutations: { retry: false } } });
  const observer = new MutationObserver(client, options);
  try {
    await observer.mutate({ deviceId: "741", input: invocation("createFresh"), sessionGeneration: 1 }).catch(() => {});
    assert.equal(observer.getCurrentResult().status, "success");
    assert.equal(states.target, null);
    assert.equal(posts(fixture).length, 1);
  } finally { client.clear(); }
});

test("existing status is required but unknown nonblank state remains a neutral historical acknowledgement", async () => {
  for (const variant of ["createExisting", "transferExisting"]) {
    for (const status of ["Provisioned", "Installed", "Verified", "Removed", "Failed", "Quarantined", "UNRECOGNIZED <script>"]) {
      const fixture = loadService({ variant, data: { ...receipts[variant], status } });
      assert.deepEqual(await fixture.telematicsService.assignDeviceToVehicle(741, invocation(variant)), { ...expected(variant), recordedStatus: status.startsWith("UNRECOGNIZED") ? null : status });
    }
    for (const status of [undefined, null, "", "  ", [], {}, true, 1]) {
      const fixture = loadService({ variant, data: { ...receipts[variant], status } });
      await assert.rejects(fixture.telematicsService.assignDeviceToVehicle(741, invocation(variant)), e => e.outcome === "unconfirmed");
    }
  }
});

test("HTTP envelope and own field discriminants are strict and messages cannot turn malformed shapes into replay", async () => {
  for (const variant of variants) {
    for (const status of [undefined, null, "200", "201", 202, 204, 500, variant === "createFresh" ? 200 : 201]) {
      const fixture = loadService({ variant, status });
      await assert.rejects(fixture.telematicsService.assignDeviceToVehicle(741, invocation(variant)), e => e.outcome === "unconfirmed");
    }
    for (const envelope of [null, [], {}, 1, { success: "true", data: receipts[variant] }, { success: 1, data: receipts[variant] }, { success: true, data: null }, { success: true, data: [] },
      { success: true, data: receipts[variant], Success: false }, { success: true, data: receipts[variant], Data: null },
      { success: true, data: receipts[variant], s_u_c_c_e_s_s: false }, { success: true, data: receipts[variant], d_a_t_a: null }]) {
      const fixture = loadService({ variant, envelope });
      await assert.rejects(fixture.telematicsService.assignDeviceToVehicle(741, invocation(variant)), e => e.outcome === "unconfirmed");
    }
    const forbidden = variant === "createFresh" ? ["deviceRole", "isPrimary", "priorInstallationId", "replacedInstallationId"]
      : variant === "createExisting" ? ["effectiveFrom", "priorInstallationId", "replacedInstallationId"]
        : variant === "transferFresh" ? ["replacedInstallationId", "deviceId", "rowVersion", "deviceRole", "isPrimary"] : ["priorInstallationId", "deviceRole", "isPrimary"];
    for (const key of forbidden) for (const value of [undefined, null, 902]) {
      const fixture = loadService({ variant, data: { ...receipts[variant], [key]: value } });
      await assert.rejects(fixture.telematicsService.assignDeviceToVehicle(741, invocation(variant)), e => e.outcome === "unconfirmed", `${variant}:${key}`);
    }
  }
});

test("core identity version and aliases cannot mismatch or lose numeric precision", async () => {
  for (const variant of variants) {
    const fields = { id: [0, -1, "0903", Number.MAX_SAFE_INTEGER + 1, {}, null], vehicleId: [43, "042", null] };
    if (variant !== "transferFresh") fields.deviceId = [742, null, "0741"];
    if (variant !== "transferFresh") fields.rowVersion = [0, -1, 2147483648, true, "1", {}, null, variant === "createFresh" ? 2 : 1.2];
    if (isTransfer(variant)) fields[variant === "transferFresh" ? "priorInstallationId" : "replacedInstallationId"] = [904, null, Number.MAX_SAFE_INTEGER + 1];
    if (variant === "createExisting") { fields.deviceRole = ["ELD", null, {}]; fields.isPrimary = [false, "true", 1, null]; }
    if (variant.endsWith("Fresh")) fields.status = ["Verified", "Unknown", null];
    for (const [key, values] of Object.entries(fields)) for (const value of values) {
      const fixture = loadService({ variant, data: { ...receipts[variant], [key]: value } });
      await assert.rejects(fixture.telematicsService.assignDeviceToVehicle(741, invocation(variant)), e => e.outcome === "unconfirmed", `${variant}:${key}`);
      assert.equal(posts(fixture).length, 1);
    }
    for (const [camel, snake] of [["deviceId", "device_id"], ["vehicleId", "vehicle_id"], ["rowVersion", "row_version"], ["effectiveFrom", "effective_from"], ["deviceRole", "device_role"], ["isPrimary", "is_primary"], ["priorInstallationId", "prior_installation_id"], ["replacedInstallationId", "replaced_installation_id"]]) {
      if (!Object.hasOwn(receipts[variant], camel)) continue;
      const fixture = loadService({ variant, data: { ...receipts[variant], [snake]: "conflicting" } });
      await assert.rejects(fixture.telematicsService.assignDeviceToVehicle(741, invocation(variant)), e => e.outcome === "unconfirmed");
    }
    if (isTransfer(variant)) {
      const fixture = loadService({ variant, data: { ...receipts[variant], id: 902 } });
      await assert.rejects(fixture.telematicsService.assignDeviceToVehicle(741, invocation(variant)), e => e.outcome === "unconfirmed");
    }
  }
});

test("preflight must match admitted mode device prior installation vehicle and version or stop before POST", async () => {
  for (const options of [{ current: prior }, { deviceId: 742 }]) {
    const fixture = loadService(options);
    await assert.rejects(fixture.telematicsService.assignDeviceToVehicle(741, invocation("createFresh")));
    assert.equal(posts(fixture).length, 0);
  }
  for (const current of [null, { ...prior, id: 904 }, { ...prior, deviceId: 742 }, { ...prior, vehicleId: 43 }, { ...prior, rowVersion: 3 },
    { ...prior, id: Number.MAX_SAFE_INTEGER + 1 }, { ...prior, rowVersion: true }, { ...prior, rowVersion: [2] }, { ...prior, rowVersion: {} }, { ...prior, rowVersion: 2147483647 }]) {
    const fixture = loadService({ variant: "transferFresh", current });
    await assert.rejects(fixture.telematicsService.assignDeviceToVehicle(741, invocation("transferFresh")));
    assert.equal(posts(fixture).length, 0);
  }
  for (const variant of variants) for (const failUrl of feeds) {
    const fixture = loadService({ variant, failStage: "before", failUrl });
    await assert.rejects(fixture.telematicsService.assignDeviceToVehicle(741, invocation(variant)));
    assert.equal(posts(fixture).length, 0);
  }
});

test("unsafe JSON body IDs and malformed primitive inputs fail before POST without changing shared mappers", async () => {
  const fields = { vehicleId: [0, "0", "042", true, [], {}, Number.MAX_SAFE_INTEGER + 1, "9007199254740992"],
    deviceRole: ["", "unsupported", {}, null], isPrimary: [null, "true", 1, {}], assignmentReason: ["abc", "x".repeat(501), {}, null],
    installationLocation: [null, {}, "x".repeat(161)], commissioningMethod: [null, {}, "x".repeat(81)], odometerAtInstallation: [-1, 10000000000, Infinity, NaN, "12", false, {}],
    intent: [undefined, null, {}, { kind: "other" }] };
  for (const [key, values] of Object.entries(fields)) for (const value of values) {
    const fixture = loadService();
    await assert.rejects(fixture.telematicsService.assignDeviceToVehicle(741, invocation("createFresh", { [key]: value })));
    assert.equal(posts(fixture).length, 0, key);
  }
  for (const key of ["currentInstallationId", "expectedRowVersion", "priorVehicleId"]) for (const value of [null, true, [], {}, 0, Number.MAX_SAFE_INTEGER + 1]) {
    const fixture = loadService({ variant: "transferFresh" });
    await assert.rejects(fixture.telematicsService.assignDeviceToVehicle(741, invocation("transferFresh", { intent: { ...transferIntent, [key]: value } })));
    assert.equal(posts(fixture).length, 0);
  }
});

test("time validation rejects impossible unknown-offset future and submillisecond instants without rounding", async () => {
  for (const effectiveAt of [null, {}, [], true, "", "2026-02-30T00:00:00Z", "1900-02-29T00:00:00Z", "2026-01-02T24:00:00Z", "2026-01-02T00:00:60Z", "2026-01-02T00:00:00", "2026-01-02T00:00Z", "2026-01-02T00:00:00-00:00", "2026-01-02T00:00:00+14:01", "0001-01-01T00:00:00+00:01", "9999-12-31T23:59:59-00:01", "2999-01-01T00:00:00Z", "2026-01-02T00:00:00.0000001Z", "2026-01-02T00:00:00.00000000Z"]) {
    const fixture = loadService();
    await assert.rejects(fixture.telematicsService.assignDeviceToVehicle(741, invocation("createFresh", { effectiveAt })));
    assert.equal(posts(fixture).length, 0, String(effectiveAt));
  }
  for (const variant of ["createFresh", "transferFresh", "transferExisting"]) for (const effectiveFrom of [null, "2026-01-02T00:00:00.0000001Z", "2026-01-02T00:00:00.001Z", "2026-02-30T00:00:00Z"]) {
    const fixture = loadService({ variant, data: { ...receipts[variant], effectiveFrom } });
    await assert.rejects(fixture.telematicsService.assignDeviceToVehicle(741, invocation(variant)), e => e.outcome === "unconfirmed");
  }
});

test("exact offset and zero padding preserve returned time while existing create has no recorded request time", async () => {
  for (const variant of ["createFresh", "transferFresh", "transferExisting"]) for (const [request, response] of [["2026-01-02T14:00:00+14:00", effectiveAt], [effectiveAt, "2026-01-01T10:00:00.0000000-14:00"], ["2000-02-29T00:00:00Z", "2000-02-29T00:00:00.0000000Z"], ["0001-01-01T14:00:00+14:00", "0001-01-01T00:00:00Z"]]) {
    const fixture = loadService({ variant, data: { ...receipts[variant], effectiveFrom: response } });
    assert.equal((await fixture.telematicsService.assignDeviceToVehicle(741, invocation(variant, { effectiveAt: request }))).effectiveFrom, response);
  }
  const fixture = loadService({ variant: "createExisting" });
  assert.equal((await fixture.telematicsService.assignDeviceToVehicle(741, invocation("createExisting"))).effectiveFrom, null);
});

test("known rejection and unknown receipt or transport are distinct with no automatic resubmission", async () => {
  const inheritedFailure = Object.create({ success: false });
  const exoticFailure = Object.assign(new (class ErrorCarrier {})(), { success: false });
  const arrayFailure = Object.assign([], { success: false });
  const nullPrototypeFailure = Object.assign(Object.create(null), { success: false });
  for (const variant of variants) for (const [options, outcome] of [
    [{ envelope: { success: false, message: "private raw" } }, "rejected"],
    [{ failure: { response: { status: 409, data: { success: false } } } }, "rejected"],
    [{ failure: { response: { status: 422, data: nullPrototypeFailure } } }, "rejected"],
    [{ failure: { response: { status: 409, data: inheritedFailure } } }, "unconfirmed"],
    [{ failure: { response: { status: 409, data: exoticFailure } } }, "unconfirmed"],
    [{ failure: { response: { status: 409, data: arrayFailure } } }, "unconfirmed"],
    [{ failure: { response: { status: 409, data: { success: false, Success: true } } } }, "unconfirmed"],
    [{ failure: { response: { status: 409, data: { success: false, Data: null } } } }, "unconfirmed"],
    [{ failure: new Error("private timeout") }, "unconfirmed"],
    [{ failure: { response: { status: 503 } } }, "unconfirmed"],
  ]) {
    const fixture = loadService({ variant, ...options });
    await assert.rejects(fixture.telematicsService.assignDeviceToVehicle(741, invocation(variant)), error => error.outcome === outcome && /history/i.test(error.message) && !/private/.test(error.message));
    assert.equal(posts(fixture).length, 1);
  }
  for (const role of ["Driver", "Customer"]) {
    const fixture = loadService({ session: { role } });
    await assert.rejects(fixture.telematicsService.assignDeviceToVehicle(741, invocation("createFresh")), /Permission denied/);
    assert.deepEqual(fixture.calls, []);
  }
});

test("receipt spelling collisions cannot be hidden by the shared additive normalizer", async () => {
  for (const variant of variants) {
    for (const key of ["Id", "ID", "Status", "DeviceId", "deviceID", "DEVICE_ID", "VehicleId", "rowVERSION", "EffectiveFrom", "PriorInstallationId", "ReplacedInstallationId", "IsPrimary", "DeviceRole"]) {
      const fixture = loadService({ variant, data: { ...receipts[variant], [key]: "conflicting fixture" } });
      await assert.rejects(fixture.telematicsService.assignDeviceToVehicle(741, invocation(variant)), e => e.outcome === "unconfirmed", `${variant}:${key}`);
    }
    const snake = Object.fromEntries(Object.entries(receipts[variant]).map(([k, v]) => [k.replace(/[A-Z]/g, c => "_" + c.toLowerCase()), v]));
    assert.deepEqual(await loadService({ variant, data: snake }).telematicsService.assignDeviceToVehicle(741, invocation(variant)), expected(variant));
  }
});

test("every request fact and nested intent are captured before deferred preflight and POST", async () => {
  const changes = {
    vehicleId: "43", deviceRole: "ELD", isPrimary: false, effectiveAt: "2026-01-03T00:00:00Z", installationLocation: "changed bay",
    odometerAtInstallation: 456, commissioningMethod: "changed method", assignmentReason: "changed reason", removalReason: "changed removal",
    intent: { kind: "create" }, "intent.kind": "create", "intent.currentInstallationId": "905", "intent.expectedRowVersion": 4, "intent.priorVehicleId": "44",
  };
  for (const variant of variants) for (const stage of ["beforeGet", "beforePost"]) for (const [key, value] of Object.entries(changes)) {
    const submitted = invocation(variant);
    const changedValue = key === "intent" ? isTransfer(variant) ? { ...createIntent } : { ...transferIntent }
      : key === "intent.kind" ? isTransfer(variant) ? "create" : "transfer" : value;
    let changed = false;
    const fixture = loadService({ variant, [stage]: async () => {
      await Promise.resolve();
      if (changed) return;
      changed = true;
      if (key.startsWith("intent.")) submitted.intent[key.slice(7)] = changedValue; else submitted[key] = changedValue;
    } });
    assert.deepEqual(await fixture.telematicsService.assignDeviceToVehicle(741, submitted), expected(variant), `${variant}:${stage}:${key}`);
    const body = posts(fixture)[0][2];
    assert.equal(body.vehicleId, 42); assert.equal(body.deviceRole, "GPS"); assert.equal(body.isPrimary, true);
    for (const property of ["installationLocation", "odometerAtInstallation", "commissioningMethod", "assignmentReason"]) assert.equal(body[property], input[property]);
    assert.equal(body[isTransfer(variant) ? "effectiveAt" : "effectiveFrom"], effectiveAt);
    if (isTransfer(variant)) { assert.equal(body.currentInstallationId, 902); assert.equal(body.expectedRowVersion, 2); assert.equal(body.removalReason, input.removalReason); }
    assert.equal(Object.hasOwn(body, "intent"), false); assert.equal(Object.hasOwn(body, "priorVehicleId"), false);
  }
});

test("safe numeric body boundaries preserve Int64 path and receipt IDs and explicit secondary metadata", async () => {
  const longId = "9223372036854775807";
  for (const variant of variants) {
    const data = { ...receipts[variant], id: longId, vehicleId: Number.MAX_SAFE_INTEGER };
    if (Object.hasOwn(data, "deviceId")) data.deviceId = longId;
    if (Object.hasOwn(data, "isPrimary")) data.isPrimary = false;
    if (variant.endsWith("Existing")) data.rowVersion = 2147483647;
    if (variant === "transferFresh") data.priorInstallationId = Number.MAX_SAFE_INTEGER;
    if (variant === "transferExisting") data.replacedInstallationId = Number.MAX_SAFE_INTEGER;
    const current = isTransfer(variant) ? { ...prior, id: Number.MAX_SAFE_INTEGER, deviceId: longId, rowVersion: "2147483646" } : null;
    const fixture = loadService({ variant, deviceId: longId, data, current });
    const call = invocation(variant, { vehicleId: String(Number.MAX_SAFE_INTEGER), isPrimary: false, installationLocation: " ", commissioningMethod: " ", odometerAtInstallation: null });
    if (isTransfer(variant)) call.intent = { ...transferIntent, currentInstallationId: String(Number.MAX_SAFE_INTEGER), expectedRowVersion: 2147483646 };
    const result = await fixture.telematicsService.assignDeviceToVehicle(longId, call);
    assert.equal(result.installationId, longId); assert.equal(result.vehicleId, String(Number.MAX_SAFE_INTEGER));
    assert.match(posts(fixture)[0][1], new RegExp(`/devices/${longId}/installations`));
    assert.equal(posts(fixture)[0][2].isPrimary, false); assert.equal(posts(fixture)[0][2].installationLocation, null); assert.equal(posts(fixture)[0][2].commissioningMethod, null);
    assert.equal(posts(fixture)[0][2].odometerAtInstallation, null);
  }
});

test("opened target intent uses installation identity and version rather than a vehicle label", () => {
  const { getInstallationIntent } = loadService();
  assert.deepEqual(getInstallationIntent(target), createIntent);
  assert.deepEqual(getInstallationIntent(transferTarget), transferIntent);
  assert.deepEqual(getInstallationIntent({ ...transferTarget, id: "9223372036854775807", assignedVehicleId: "9223372036854775807" }), { ...transferIntent, priorVehicleId: "9223372036854775807" });
  for (const value of [
    { ...target, currentInstallationId: "" }, { ...target, assignedVehicleId: "41" }, { ...target, currentInstallationRowVersion: 2 },
    { ...transferTarget, currentInstallationId: "9007199254740992" }, { ...transferTarget, currentInstallationId: null },
    { ...transferTarget, currentInstallationRowVersion: 2147483647 }, { ...transferTarget, assignedVehicleId: "" },
    { ...transferTarget, currentInstallationRowVersion: true }, { ...target, id: "01" },
  ]) assert.equal(getInstallationIntent(value), null);
});

test("synchronous form ownership denies duplicate submit and pending close open input dismiss resets", async () => {
  const pending = [];
  const ui = loadUi({ operation: () => new Promise((resolve, reject) => pending.push({ resolve, reject })) });
  try {
    ui.controls().openInstallation(transferTarget);
    ui.controls().updateInstallationForm(() => ({ ...formInput }));
    const controls = ui.controls(); const resets = ui.counters.resets;
    let updaterCalls = 0;
    controls.submitInstallation({ preventDefault() {} }); controls.submitInstallation({ preventDefault() {} });
    controls.closeInstallation(); controls.openInstallation(target); controls.updateInstallationForm(() => { updaterCalls++; return {}; }); ui.dismiss();
    assert.equal(ui.submitted.length, 1); assert.equal(ui.states.target.currentInstallationId, "902");
    assert.deepEqual(ui.states.form, formInput); assert.equal(updaterCalls, 0); assert.equal(ui.counters.resets, resets);
    assert.deepEqual(ui.submitted[0].input.intent, transferIntent);
    await tick(); pending[0].reject(new Error("fixture rejected")); await tick(); await tick();
    assert.equal(ui.session.current.pending, false); assert.equal(ui.states.error.message, "fixture rejected");
    assert.equal(ui.options.retry, false); assert.equal(pending.length, 1, "global mutation retries must not resubmit this command");
    ui.controls().submitInstallation({ preventDefault() {} }); await tick();
    assert.equal(ui.submitted.length, 2); pending[1].resolve(expected("transferFresh")); await tick(); await tick();
    assert.equal(ui.session.current.pending, false);
  } finally { ui.client.clear(); }
});

test("stale draft and closed same-device session callbacks cannot submit or change a newer vehicle or prior", () => {
  const ui = loadUi();
  try {
    ui.controls().openInstallation(transferTarget); ui.controls().updateInstallationForm(() => ({ ...formInput }));
    const oldDraft = ui.controls();
    ui.controls().updateInstallationForm(form => ({ ...form, vehicleId: "43" }));
    let changed = 0;
    oldDraft.updateInstallationForm(() => { changed++; return formInput; }); oldDraft.submitInstallation({ preventDefault() {} }); oldDraft.closeInstallation();
    assert.equal(ui.states.form.vehicleId, "43"); assert.equal(ui.submitted.length, 0); assert.equal(changed, 0);
    const closed = ui.controls(); closed.closeInstallation();
    ui.controls().openInstallation({ ...transferTarget, currentInstallationId: "904", currentInstallationRowVersion: 3, assignedVehicleId: "44" });
    ui.controls().updateInstallationForm(() => ({ ...formInput, vehicleId: "45" }));
    closed.closeInstallation(); closed.updateInstallationForm(() => formInput); closed.submitInstallation({ preventDefault() {} });
    assert.equal(ui.states.form.vehicleId, "45"); assert.equal(ui.session.current.intent.currentInstallationId, "904"); assert.equal(ui.submitted.length, 0);
  } finally { ui.client.clear(); }
});

test("success notification observations are asserted externally and admission remains locked until shared settlement", async () => {
  const ui = loadUi(); let stale;
  const observed = [];
  const unsubscribe = ui.observer.subscribe(state => {
    if (state.status === "success") {
      const pending = ui.session.current.pending; const resets = ui.counters.resets;
      let updaterCalls = 0;
      stale.submitInstallation({ preventDefault() {} }); stale.closeInstallation();
      stale.updateInstallationForm(() => { updaterCalls++; return formInput; });
      ui.controls().openInstallation({ ...target, id: "742" }); ui.dismiss();
      observed.push({ pending, target: ui.states.target, dispatches: ui.submitted.length, resets: ui.counters.resets - resets, updaterCalls });
    }
  });
  try {
    ui.controls().openInstallation(target); ui.controls().updateInstallationForm(() => ({ ...formInput })); stale = ui.controls();
    stale.submitInstallation({ preventDefault() {} }); await tick(); await tick();
    assert.deepEqual(observed, [{ pending: true, target: null, dispatches: 1, resets: 0, updaterCalls: 0 }]);
    assert.equal(ui.observer.getCurrentResult().status, "success"); assert.equal(ui.session.current.pending, false);
    stale.submitInstallation({ preventDefault() {} }); stale.updateInstallationForm(() => formInput); stale.closeInstallation();
    assert.equal(ui.submitted.length, 1); assert.equal(ui.states.target, null);
    ui.controls().openInstallation({ ...target, id: "742" }); assert.equal(ui.states.target.id, "742");
  } finally { unsubscribe(); ui.client.clear(); }
});

test("precise rejected handoff regression detects the obsolete inner-finally pending gap", async () => {
  const actual = declaration("submitInstallation");
  const obsolete = actual.replace(/void assignmentSingleFlight\(\(\) => assignMut\.mutateAsync\(variables\)\)\.finally\(\(\) => \{[\s\S]*?\n    \}\);/, `void assignmentSingleFlight(async () => {
    try { await assignMut.mutateAsync(variables); }
    finally { if (ownsAssignmentSession(variables)) assignmentSession.current.pending = false; }
  });`);
  assert.notEqual(obsolete, actual);
  for (const [expression, vulnerable] of [[actual, false], [obsolete, true]]) {
    let reject; const promise = new Promise((_, failure) => { reject = failure; }); let dispatches = 0;
    const session = { current: { deviceId: "741", intent: createIntent, generation: 0, pending: false } };
    const form = { ...formInput };
    const scope = { canGovernInstallations: true, assignTarget: target, installationForm: form, assignmentFormSnapshot: { current: form },
      assignmentSession: session, assignmentRenderGeneration: 0, setFormError() {}, toUtcIso: actualToUtcIso,
      lifecyclePermissionRef: { current: true }, assignmentTargetRef: { current: target },
      assignmentSingleFlight: singleFlight(), assignMut: { mutateAsync: () => { dispatches++; return promise; } } };
    scope.ownsAssignmentSession = evaluate(declaration("ownsAssignmentSession"), scope);
    const submit = evaluate(expression, scope); submit({ preventDefault() {} });
    const observations = [];
    const handoff = promise.catch(() => { observations.push(session.current.pending); submit({ preventDefault() {} }); });
    reject(new Error("fixture rejection")); await handoff; await tick();
    assert.deepEqual(observations, [!vulnerable]); assert.equal(dispatches, 1); assert.equal(session.current.pending, vulnerable);
  }
});

test("late mutation callbacks cannot overwrite a superseding same-device prior and vehicle draft", async () => {
  for (const fail of [false, true]) {
    let settle; const ui = loadUi({ operation: () => new Promise((resolve, reject) => { settle = fail ? reject : resolve; }) });
    try {
      ui.controls().openInstallation(transferTarget); ui.controls().updateInstallationForm(() => ({ ...formInput }));
      ui.controls().submitInstallation({ preventDefault() {} }); await tick();
      // Adversarial supersession seam; normal pending close/reopen is denied.
      ui.session.current = { deviceId: "741", intent: { ...transferIntent, currentInstallationId: "904" }, generation: ui.session.current.generation + 1, pending: false };
      ui.states.target = { ...transferTarget, currentInstallationId: "904" }; ui.states.form = { ...formInput, vehicleId: "45" };
      ui.snapshot.current = ui.states.form; ui.states.notice = "newer notice";
      settle(fail ? new Error("old failure") : expected("transferFresh")); await tick(); await tick();
      assert.equal(ui.states.target.currentInstallationId, "904"); assert.equal(ui.states.form.vehicleId, "45");
      assert.equal(ui.states.notice, "newer notice"); assert.equal(ui.states.record, null); assert.equal(ui.states.error, null); assert.equal(ui.session.current.pending, false);
    } finally { ui.client.clear(); }
  }
  for (const mode of ["permission", "target", "form"]) for (const fail of [false, true]) {
    let settle; const ui = loadUi({ operation: () => new Promise((resolve, reject) => { settle = fail ? reject : resolve; }) });
    try {
      ui.controls().openInstallation(transferTarget); ui.controls().updateInstallationForm(() => ({ ...formInput }));
      ui.controls().submitInstallation({ preventDefault() {} }); await tick();
      if (mode === "permission") ui.lifecyclePermissionRef.current = false;
      if (mode === "target") {
        const replacement = { ...transferTarget };
        ui.scope.assignmentTargetRef.current = replacement; ui.states.target = replacement;
      }
      if (mode === "form") {
        const replacement = { ...formInput };
        ui.snapshot.current = replacement; ui.states.form = replacement;
      }
      ui.states.notice = `${mode} revoked`;
      settle(fail ? new Error("old failure") : expected("transferFresh")); await tick(); await tick();
      assert.equal(ui.states.target.currentInstallationId, "902"); assert.equal(ui.states.notice, `${mode} revoked`);
      assert.equal(ui.states.record, null); assert.equal(ui.states.error, null);
    } finally { ui.client.clear(); }
  }
});

test("input conversion permission and contradictory opened target failures dispatch no mutation", () => {
  for (const allowed of [false, true]) {
    const ui = loadUi({ allowed });
    try {
      ui.controls().openInstallation({ ...target, assignedVehicleId: "41" }); assert.equal(ui.states.target, null);
      ui.controls().openInstallation(target);
      if (!allowed) { assert.equal(ui.states.target, null); continue; }
      for (const change of [{ effectiveAt: "" }, { effectiveAt: "2999-01-01T00:00" }, { odometerAtInstallation: "NaN" }, { odometerAtInstallation: "10000000000" }, { primaryDesignation: "" }]) {
        ui.controls().updateInstallationForm(() => ({ ...formInput, ...change })); ui.controls().submitInstallation({ preventDefault() {} });
        assert.equal(ui.submitted.length, 0); assert.ok(ui.states.formError); assert.equal(ui.session.current.pending, false);
      }
    } finally { ui.client.clear(); }
  }
});

test("actual detail and each optional post-ack query failure preserves receipt and marks cached display stale", async () => {
  for (const variant of variants) for (const failUrl of feeds) {
    const fixture = loadService({ variant, failStage: "after", failUrl });
    const client = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: Infinity, gcTime: Infinity } } });
    const key = ["telematics", "device", "741"]; client.setQueryData(key, { earlierDisplay: true });
    const observer = new QueryObserver(client, { queryKey: key, queryFn: () => fixture.telematicsService.getDeviceById(741), retry: false, staleTime: Infinity });
    const unsubscribe = observer.subscribe(() => {});
    const ui = loadUi({ operation: (device, call) => fixture.telematicsService.assignDeviceToVehicle(device, call), refresh: loadRefresh({ client }) });
    try {
      ui.controls().openInstallation(isTransfer(variant) ? transferTarget : target); ui.controls().updateInstallationForm(() => ({ ...formInput }));
      ui.controls().submitInstallation({ preventDefault() {} }); await tick(); await tick(); await tick();
      assert.equal(ui.observer.getCurrentResult().status, "success"); assert.deepEqual(ui.states.record, { ...expected(variant), deviceId: "741" });
      assert.equal(ui.refresh.states.warning, true); assert.equal(client.getQueryState(key).status, "error");
      assert.deepEqual(client.getQueryData(key), { earlierDisplay: true }); assert.equal(posts(fixture).length, 1);
      await ui.refresh.refresh(ui.states.record); assert.equal(posts(fixture).length, 1, "display retry must not submit another installation");
    } finally { unsubscribe(); client.clear(); ui.client.clear(); }
  }
});

test("record identity and refresh generation protect newer vehicle prior acknowledgement and same-device replacement", async () => {
  const deferred = [];
  const refresh = loadRefresh({ invalidate: () => new Promise((resolve, reject) => deferred.push({ resolve, reject })) });
  const older = refresh.refresh(record);
  const newerRecord = { ...record, installationId: "904", vehicleId: "43", priorInstallationId: "903", operation: "transfer" };
  refresh.context.current.record = newerRecord; const newer = refresh.refresh(newerRecord);
  deferred[2].reject(new Error("newer read failure")); deferred[3].resolve(); await newer;
  deferred[0].resolve(); deferred[1].resolve(); await older;
  assert.equal(refresh.states.warning, true); assert.equal(refresh.states.pending, false);
  await refresh.refresh(record); assert.equal(refresh.calls.length, 4);
  refresh.context.current = { record: { ...newerRecord }, generation: refresh.context.current.generation + 1 };
  await refresh.refresh(newerRecord); assert.equal(refresh.calls.length, 4);
  let release;
  const gate = new Promise(resolve => { release = resolve; });
  const gated = loadRefresh({ invalidate: () => gate }); gated.states.warning = true;
  const attempt = gated.refresh(record); await tick(); gated.permission.current = false; release(); await attempt;
  assert.equal(gated.states.warning, true); assert.equal(gated.states.pending, false);
});

test("notice distinguishes existing recording absent time and unknown status; retry callback only refreshes display", async () => {
  const h = (type, props, ...children) => ({ type, props: props ?? {}, children: children.flat(Infinity) });
  const component = evaluate(functionSource("AssignmentRefreshNotice"), { h, assignmentRecordedMessage }, true).AssignmentRefreshNotice;
  for (const variant of variants) {
    const refresh = loadRefresh(); const bound = { ...expected(variant), deviceId: "741", recordedStatus: variant.endsWith("Existing") ? null : "Installed" };
    refresh.context.current.record = bound; refresh.states.warning = true;
    const nodes = [];
    const text = node => { if (node == null || typeof node === "boolean") return ""; if (typeof node !== "object") return String(node); nodes.push(node); return node.children.map(text).join(" "); };
    const message = text(component({ record: bound, busy: false, onRetry: () => refresh.refresh(bound) }));
    for (const pattern of [/741/, /903/, /42/, /recorded/, /may be out of date/, /does not establish current installation state or physical readiness/]) assert.match(message, pattern);
    if (isTransfer(variant)) assert.match(message, /prior installation 902/);
    if (variant.endsWith("Existing")) { assert.match(message, /already recorded/); assert.match(message, /status: unavailable/); }
    if (variant === "createExisting") { assert.doesNotMatch(message, /2026/); assert.match(message, /contains no effective time/); }
    await nodes.find(node => node.type === "button").props.onClick(); assert.equal(refresh.states.warning, false);
    assert.deepEqual(refresh.calls, [[{ queryKey: ["telematics"] }, { throwOnError: true }], [{ queryKey: ["iot-devices"] }, { throwOnError: true }]]);
  }
  assert.equal((pageSource.match(/<AssignmentRefreshNotice record=\{assignmentRecord\}/g) ?? []).length, 2);
  assert.match(pageSource, /onClose=\{closeInstallation\}/); assert.match(pageSource, /onSubmit=\{submitInstallation\}/);
  assert.match(pageSource, /error=\{formError \?\? assignmentError\?\.message \?\? null\}/);
});

test("initially offline and mid-read paused retries release acknowledged mutation ownership with a stale-display warning", async () => {
  for (const scenario of ["initially-offline", "retry-paused"]) {
    onlineManager.setOnline(true);
    const options = { beforePost: async () => { if (scenario === "initially-offline") onlineManager.setOnline(false); },
      beforeGet: async (url, stage) => { if (scenario === "retry-paused" && stage === "after" && options.failStage && url === feeds[0]) onlineManager.setOnline(false); },
      failStage: scenario === "retry-paused" ? "after" : undefined, failUrl: feeds[0] };
    const fixture = loadService(options);
    const client = new QueryClient({ defaultOptions: { queries: { retry: 1, retryDelay: 0, staleTime: Infinity } } });
    client.mount();
    const key = ["telematics", "device", "741"]; client.setQueryData(key, { earlierDisplay: true });
    const query = new QueryObserver(client, { queryKey: key, queryFn: () => fixture.telematicsService.getDeviceById(741), retry: 1, retryDelay: 0, staleTime: Infinity });
    const unsubscribe = query.subscribe(() => {});
    const refresh = loadRefresh({ client });
    const ui = loadUi({ operation: (id, call) => fixture.telematicsService.assignDeviceToVehicle(id, call), refresh });
    try {
      ui.controls().openInstallation(target); ui.controls().updateInstallationForm(() => ({ ...formInput })); ui.controls().submitInstallation({ preventDefault() {} });
      for (let attempt = 0; attempt < 20 && client.getQueryState(key).fetchStatus !== "paused"; attempt++) await new Promise(resolve => setTimeout(resolve, 2));
      await tick(); await tick();
      assert.equal(client.getQueryState(key).fetchStatus, "paused", scenario);
      assert.equal(ui.observer.getCurrentResult().status, "success", scenario); assert.equal(ui.session.current.pending, false);
      assert.deepEqual(ui.states.record, record); assert.equal(refresh.states.warning, true); assert.equal(refresh.states.pending, false); assert.equal(refresh.states.subscriptions, 0);
      assert.equal(posts(fixture).length, 1); assert.deepEqual(client.getQueryData(key), { earlierDisplay: true });
      await refresh.refresh(ui.states.record); assert.equal(refresh.states.warning, true); assert.equal(refresh.states.subscriptions, 0);
      options.failStage = undefined; onlineManager.setOnline(true);
      for (let attempt = 0; attempt < 20 && client.getQueryState(key).fetchStatus !== "idle"; attempt++) await new Promise(resolve => setTimeout(resolve, 2));
      assert.equal(refresh.states.warning, true, "a late resumed read cannot erase the paused result warning");
      await refresh.refresh(ui.states.record);
      assert.equal(refresh.states.warning, false); assert.equal(refresh.states.pending, false); assert.equal(refresh.states.subscriptions, 0); assert.equal(posts(fixture).length, 1);
    } finally {
      options.failStage = undefined; onlineManager.setOnline(true);
      await client.cancelQueries(undefined, { silent: true }); unsubscribe(); client.unmount(); client.clear(); ui.client.clear();
    }
  }
});

test("empty or inactive query invalidation cannot claim a confirmed display refresh", async () => {
  const client = new QueryClient();
  try {
    const refresh = loadRefresh({ client }); await refresh.refresh(record);
    assert.equal(refresh.states.warning, true); assert.equal(refresh.states.pending, false); assert.equal(refresh.states.subscriptions, 0);
    client.setQueryData(["telematics", "device", "741"], { cached: true });
    await refresh.refresh(record); assert.equal(refresh.states.warning, true); assert.equal(refresh.states.subscriptions, 0);
  } finally { client.clear(); }
});

test("envelope and receipt fields must be own properties with plain object boundaries", async () => {
  for (const envelope of [{}, { success: undefined, data: receipts.createFresh }, { success: true }, { success: true, data: undefined },
    Object.create({ success: true, data: receipts.createFresh }), new Date(), { success: true, data: new Date() }, { success: true, data: Object.create(receipts.createFresh) }]) {
    const fixture = loadService({ envelope });
    await assert.rejects(fixture.telematicsService.assignDeviceToVehicle(741, invocation("createFresh")), e => e.outcome === "unconfirmed");
  }
  const successDescriptor = Object.getOwnPropertyDescriptor(Object.prototype, "success");
  try {
    Object.defineProperty(Object.prototype, "success", { configurable: true, value: true });
    const fixture = loadService({ envelope: { data: receipts.createFresh } });
    await assert.rejects(fixture.telematicsService.assignDeviceToVehicle(741, invocation("createFresh")), e => e.outcome === "unconfirmed");
  } finally {
    if (successDescriptor) Object.defineProperty(Object.prototype, "success", successDescriptor); else delete Object.prototype.success;
  }
  const idDescriptor = Object.getOwnPropertyDescriptor(Object.prototype, "id");
  try {
    Object.defineProperty(Object.prototype, "id", { configurable: true, writable: true, value: 903 });
    const { id: _omitted, ...data } = receipts.createFresh;
    const fixture = loadService({ data });
    await assert.rejects(fixture.telematicsService.assignDeviceToVehicle(741, invocation("createFresh")), e => e.outcome === "unconfirmed");
  } finally {
    if (idDescriptor) Object.defineProperty(Object.prototype, "id", idDescriptor); else delete Object.prototype.id;
  }
});
