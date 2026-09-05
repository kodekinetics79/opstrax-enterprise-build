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
const serviceSource = readFileSync(resolve(root, "src/services/telematicsService.ts"), "utf8");
const pageSource = readFileSync(resolve(root, "src/pages/IotDevicesPage.tsx"), "utf8");
const page = ts.createSourceFile("IotDevicesPage.tsx", pageSource, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
const built = await esbuild.build({
  stdin: { contents: serviceSource
    .replace('import { apiClient, unwrap } from "@/services/apiClient";', 'import { unwrap } from "@/services/apiClient"; const apiClient = __apiClient;')
    .replace('import { readRawSession } from "@/auth/sessionStorage";', 'const readRawSession = () => JSON.stringify(__session);'),
    loader: "ts", resolveDir: root },
  bundle: true, platform: "node", format: "cjs", write: false,
  alias: { "@": resolve(root, "src") }, define: { "import.meta.env": "{}" }, logLevel: "silent",
});
const deviceId = "741";
const installationId = "902";
const effectiveTo = "2026-01-02T00:00:00.000Z";
const input = { effectiveTo, removalReason: "fixture-observed-removal" };
const storedInstallation = { id: 902, deviceId: 741, status: "Installed", rowVersion: 2, effectiveFrom: "2026-01-01T00:00:00.000Z" };
const defaultSession = { role: "Tenant Admin", permissions: ["maintenance:view", "telemetry.alerts.read", "telemetry.live_state.read"] };
const feeds = ["/api/telemetry/devices/741", "/api/maintenance/fault-codes", "/api/telemetry/alerts", "/api/telemetry/positions"];

function loadService(options = {}) {
  const calls = [];
  let acknowledged = false;
  const holder = { exports: {} };
  const { session = defaultSession, current = storedInstallation, history = [], failStage, failUrl, failure } = options;
  const api = {
    get: async (url, params) => {
      const stage = acknowledged ? "after" : "before";
      calls.push(["GET", stage, url, params]);
      await options.beforeGet?.(url, stage);
      if (stage === failStage && (!failUrl || url === failUrl)) throw new Error("fixture private raw read failure");
      const data = /\/api\/telemetry\/devices\//.test(url)
        ? { id: options.deviceId ?? 741, status: "Active", ...options.device, currentInstallation: current, installationHistory: history }
        : [];
      return { data: { success: true, data } };
    },
    post: async (url, body) => {
      calls.push(["POST", url, body]);
      await options.beforePost?.(url, body);
      if (failure) throw failure;
      acknowledged = true;
      const data = Object.hasOwn(options, "data") ? options.data : {
        id: current?.id, status: "Removed", effectiveTo: body.effectiveTo,
      };
      return { status: 200, data: Object.hasOwn(options, "envelope") ? options.envelope : { success: true, data } };
    },
  };
  const browserPresence = { location: { hostname: "example.invalid" }, localStorage: { getItem: () => null }, sessionStorage: { getItem: () => null } };
  new Function("require","module","exports","__apiClient","__session","window",built.outputFiles[0].text)(require,holder,holder.exports,api,session,browserPresence);
  return { ...holder.exports, calls };
}
const posts = fixture => fixture.calls.filter(call => call[0] === "POST");
function declaration(name) {
  let result;
  function visit(node) {
    if (ts.isVariableDeclaration(node) && node.name.getText(page) === name) result = node.initializer?.getText(page);
    ts.forEachChild(node, visit);
  }
  visit(page);
  return result;
}
function evaluate(expression, scope, component = false) {
  assert.ok(expression, "actual production expression must exist");
  const holder = { exports: {} };
  const code = esbuild.transformSync(component ? "export " + expression : "export const value = " + expression + ";", { loader: "tsx", format: "cjs", jsxFactory: "h" }).code;
  new Function("module","exports",...Object.keys(scope),code)(holder,holder.exports,...Object.values(scope));
  return component ? holder.exports : holder.exports.value;
}
function singleFlight() {
  const source = readFileSync(resolve(root, "src/hooks/useSingleFlight.ts"), "utf8").replace('import { useCallback, useRef } from "react";', "");
  const holder = { exports: {} };
  const code = esbuild.transformSync(source, { loader: "ts", format: "cjs" }).code;
  new Function("module","exports","useCallback","useRef",code)(holder,holder.exports,callback => callback,initial => ({ current: initial }));
  return holder.exports.useSingleFlight();
}
const tick = () => new Promise(resolve => setImmediate(resolve));
const actualToUtcIso = evaluate(page.statements.find(node => ts.isFunctionDeclaration(node) && node.name?.text === "toUtcIso").getText(page), {}, true).toUtcIso;
const record = { deviceId: "741", installationId: "902", effectiveTo };

function loadRefresh({ client, invalidate, fail = false } = {}) {
  const states = { warning: false, pending: false, subscriptions: 0 };
  const context = { current: { record, generation: 0 } };
  const calls = [];
  const fallback = { queryHash: "fixture", state: { fetchStatus: "idle", status: "success" }, isActive: () => true };
  const backingCache = client?.getQueryCache();
  const cache = { findAll: filters => backingCache ? backingCache.findAll(filters) : [fallback], get: hash => backingCache ? backingCache.get(hash) : fallback,
    subscribe: callback => { states.subscriptions++; const cleanup = backingCache?.subscribe(callback); return () => { states.subscriptions--; cleanup?.(); }; } };
  const queryClient = { getQueryCache: () => cache, invalidateQueries: async (...args) => {
    calls.push(args);
    if (invalidate) return invalidate(...args);
    if (client) return client.invalidateQueries(...args);
    if (fail) throw new Error("fixture display refresh unavailable");
  } };
  const refreshReceiptQueries = evaluate(declaration("refreshReceiptQueries"), { queryClient });
  const refresh = evaluate(declaration("refreshRemovalDisplay"), {
    removalRefreshContext: context,
    setRemovalRefreshWarning: value => { states.warning = value; },
    setRemovalRefreshPending: value => { states.pending = value; },
    queryClient, refreshReceiptQueries,
  });
  return { refresh, context, states, calls };
}

function loadUi({ operation = async () => ({ id: "902", status: "Removed", effectiveTo }), refresh = loadRefresh(), allowed = true } = {}) {
  const states = { target: null, form: { effectiveTo: "", removalReason: "" }, formError: null, error: null, notice: null, record: null };
  const session = { current: { deviceId: null, generation: 0, pending: false } };
  const defaults = { effectiveTo: "", removalReason: "" };
  const setters = {
    setRemovalTarget: value => { states.target = value; },
    setRemovalForm: value => { states.form = typeof value === "function" ? value(states.form) : value; },
    setFormError: value => { states.formError = value; },
    setRemovalError: value => { states.error = value; },
    setNotice: value => { states.notice = value; },
    setRemovalRecord: value => { states.record = value; },
    setRemovalRefreshWarning: value => { refresh.states.warning = value; },
    setRemovalRefreshPending: value => { refresh.states.pending = value; },
  };
  const scope = {
    ...setters, removalSession: session, defaultRemovalForm: defaults,
    removalRefreshContext: refresh.context, refreshRemovalDisplay: refresh.refresh,
    canGovernInstallations: allowed, removalSingleFlight: singleFlight(),
    toUtcIso: actualToUtcIso,
  };
  scope.ownsRemovalSession = evaluate(declaration("ownsRemovalSession"), scope);
  const options = evaluate(declaration("unassignMut"), {
    ...scope, useMutation: value => value, telematicsService: { unassignDevice: operation },
  });
  const client = new QueryClient({ defaultOptions: { mutations: { retry: false } } });
  const observer = new MutationObserver(client, options);
  const submitted = [];
  const counters = { resets: 0 };
  const unassignMut = {
    mutateAsync: variables => { submitted.push(variables); return observer.mutate(variables); },
    reset: () => { counters.resets++; observer.reset(); },
  };
  const controls = () => {
    const renderedScope = { ...scope, unassignMut, removalTarget: states.target, removalForm: states.form, removalRenderGeneration: session.current.generation };
    return Object.fromEntries(["openRemoval","closeRemoval","submitRemoval","updateRemovalForm"].map(name => [name, evaluate(declaration(name), renderedScope)]));
  };
  const dismissLifecycleError = evaluate(declaration("clearLifecycleError"), {
    ...scope, unassignMut,
    assignMut: { reset() {} }, installMut: { reset() {} }, suspendMut: { reset() {} }, activateMut: { reset() {} }, rotateSecretMut: { reset() {} },
    commissioningSession: { current: { pending: false } }, setCommissioningError() {},
    assignmentSession: { current: { pending: false } }, setAssignmentError() {},
  });
  return { states, session, options, controls, refresh, submitted, observer, client, counters, dismissLifecycleError };
}

test("matching removal receipt survives unavailable readback without a reconstructed device or repeated POST", async () => {
  const fixture = loadService({ failStage: "after" });
  assert.deepEqual(await fixture.telematicsService.unassignDevice(741, input), { id: "902", status: "Removed", effectiveTo });
  assert.equal(fixture.calls.filter(call => call[0] === "GET").length, 4);
  assert.deepEqual(posts(fixture), [["POST", "/api/telemetry/devices/741/installations/902/remove", { ...input, expectedRowVersion: 2 }]]);
});

test("synchronous submit guards duplicate, pending close/open/input, then releases after rejection", async () => {
  const pending = [];
  const ui = loadUi({ operation: () => new Promise((resolve,reject) => pending.push({resolve,reject})) });
  try {
    ui.controls().openRemoval({id:"741"});
    ui.controls().updateRemovalForm(() => ({...input}));
    const controls = ui.controls();
    const resetsBeforeSubmit = ui.counters.resets;
    let pendingUpdaterCalls = 0;
    controls.submitRemoval({preventDefault(){}});
    controls.submitRemoval({preventDefault(){}});
    controls.closeRemoval();
    controls.openRemoval({id:"742"});
    controls.updateRemovalForm(() => { pendingUpdaterCalls++; return {...input,removalReason:"changed"}; });
    ui.dismissLifecycleError();
    assert.equal(ui.submitted.length,1);
    assert.equal(ui.states.target.id,"741");
    assert.deepEqual(ui.states.form,input);
    assert.equal(pendingUpdaterCalls, 0);
    assert.equal(ui.counters.resets, resetsBeforeSubmit, "pending close/open/input/dismiss must not reset the active removal mutation");
    await tick();
    pending[0].reject(new Error("fixture rejected submission"));
    for (let attempt = 0; attempt < 20 && ui.observer.getCurrentResult().status === "pending"; attempt++) await tick();
    assert.equal(ui.session.current.pending,false);
    assert.equal(ui.states.target.id,"741");
    assert.equal(ui.states.error.message,"fixture rejected submission");
    ui.controls().submitRemoval({preventDefault(){}});
    await tick();
    assert.equal(ui.submitted.length,2);
    pending[1].resolve({id:"902",status:"Removed",effectiveTo});
    await tick(); await tick();
    assert.equal(ui.session.current.pending,false);
  } finally { ui.client.clear(); }
});

test("stale closed-session callbacks cannot submit or alter a later same-device draft", () => {
  const ui = loadUi();
  try {
    ui.controls().openRemoval({id:"741"});
    ui.controls().updateRemovalForm(() => ({...input}));
    const stale = ui.controls();
    stale.closeRemoval();
    ui.controls().openRemoval({id:"741"});
    ui.controls().updateRemovalForm(() => ({...input,removalReason:"new-session-reference"}));
    stale.closeRemoval(); stale.updateRemovalForm(() => ({...input}));
    stale.submitRemoval({preventDefault(){}});
    assert.equal(ui.states.target.id,"741");
    assert.equal(ui.states.form.removalReason,"new-session-reference");
    assert.equal(ui.submitted.length,0);
  } finally { ui.client.clear(); }
});

test("acknowledged form callbacks are revoked before any later form opens and completion handoff stays locked", async () => {
  const ui = loadUi();
  let stale;
  const successObservations = [];
  const release = ui.observer.subscribe(state => {
    if (state.status === "success") {
      // Installed mutation notifications occur before mutateAsync and shared-flight settlement.
      // Collect only: exceptions in this subscriber can be caught by the mutation.
      // All acceptance assertions must run outside the callback after settlement.
      const pendingAtEntry = ui.session.current.pending;
      const resetsAtEntry = ui.counters.resets;
      stale.submitRemoval({ preventDefault() {} });
      ui.controls().openRemoval({ id: "742" });
      successObservations.push({ pendingAtEntry, targetAfterOpen: ui.states.target, dispatches: ui.submitted.length,
        resets: ui.counters.resets - resetsAtEntry });
    }
  });
  try {
    ui.controls().openRemoval({ id: "741" });
    ui.controls().updateRemovalForm(() => ({ ...input }));
    stale = ui.controls();
    stale.submitRemoval({ preventDefault() {} });
    await tick(); await tick();
    assert.deepEqual(successObservations, [{ pendingAtEntry: true, targetAfterOpen: null, dispatches: 1, resets: 0 }]);
    assert.equal(ui.observer.getCurrentResult().status, "success");
    assert.equal(ui.session.current.pending, false);
    stale.submitRemoval({ preventDefault() {} });
    stale.updateRemovalForm(() => ({ ...input }));
    stale.closeRemoval();
    assert.equal(ui.submitted.length, 1);
    assert.equal(ui.states.target, null);
    assert.deepEqual(ui.states.form, { effectiveTo: "", removalReason: "" });
    assert.equal(ui.states.record.installationId, "902");
    ui.controls().openRemoval({ id: "742" });
    assert.equal(ui.states.target.id, "742");
  } finally { release(); ui.client.clear(); }
});

test("rejected submission handoff rejects queued admission without stranding the pending guard", async () => {
  const actual = declaration("submitRemoval");
  // Explicit negative control recreates only the discarded inner-finally handoff.
  // It is not a second production path or a real device command.
  const obsolete = actual.replace(
    /void removalSingleFlight\(\(\) => unassignMut\.mutateAsync\(variables\)\)\.finally\(\(\) => \{[\s\S]*?\n    \}\);/,
    `void removalSingleFlight(async () => {
      try { await unassignMut.mutateAsync(variables); }
      finally { if (ownsRemovalSession(variables)) removalSession.current.pending = false; }
    });`,
  );
  assert.notEqual(obsolete, actual, "negative-control replacement must touch the exact handoff");
  for (const [source, vulnerable] of [[actual, false], [obsolete, true]]) {
    let reject;
    const pending = new Promise((_, fail) => { reject = fail; });
    const session = { current: { deviceId: "741", generation: 0, pending: false } };
    let dispatches = 0;
    const scope = {
      canGovernInstallations: true, removalTarget: { id: "741" }, removalForm: { ...input },
      removalSession: session, removalRenderGeneration: 0, setFormError() {}, toUtcIso: actualToUtcIso,
      removalSingleFlight: singleFlight(), unassignMut: { mutateAsync: () => { dispatches++; return pending; } },
    };
    scope.ownsRemovalSession = evaluate(declaration("ownsRemovalSession"), scope);
    const staleSubmit = evaluate(source, scope);
    staleSubmit({ preventDefault() {} });
    const observed = [];
    // Register after the first submit's await continuation, exactly at settlement.
    // The callback is unconditional: it must expose the old false-pending gap.
    const handoff = pending.catch(() => {
      observed.push(session.current.pending);
      staleSubmit({ preventDefault() {} });
    });
    reject(new Error("fixture rejected submission"));
    await handoff; await tick();
    assert.deepEqual(observed, [!vulnerable]);
    assert.equal(dispatches, 1);
    assert.equal(session.current.pending, vulnerable, vulnerable ? "obsolete handoff strands admission" : "actual handoff releases admission");
  }
});

test("forced session supersession seam ignores delayed prior mutation success and error; reset is not cancellation", async () => {
  for (const fail of [false,true]) {
    let settle;
    const ui = loadUi({ operation: () => new Promise((resolve,reject) => { settle = fail ? reject : resolve; }) });
    try {
      ui.controls().openRemoval({id:"741"});
      ui.controls().updateRemovalForm(() => ({...input}));
      ui.controls().submitRemoval({preventDefault(){}});
      await tick();
      // Adversarial harness seam, not an allowed normal pending close/reopen journey.
      ui.session.current = { deviceId:"742",generation:ui.session.current.generation+1,pending:false };
      ui.states.target={id:"742"}; ui.states.form={...input,removalReason:"later-form"}; ui.states.notice="later notice";
      settle(fail ? new Error("old failure") : {id:"902",status:"Removed",effectiveTo});
      await tick(); await tick();
      assert.equal(ui.states.target.id,"742");
      assert.equal(ui.states.form.removalReason,"later-form");
      assert.equal(ui.states.notice,"later notice");
      assert.equal(ui.states.error,null);
      assert.equal(ui.states.record,null);
    } finally { ui.client.clear(); }
  }
});

test("post-ack detail and each optional-feed failure remains separate under installed query behavior", async () => {
  for (const failUrl of feeds) {
    const fixture = loadService({failStage:"after",failUrl});
    const receipt = await fixture.telematicsService.unassignDevice(741,input);
    const client = new QueryClient({defaultOptions:{queries:{retry:false,staleTime:Infinity,gcTime:Infinity}}});
    const key=["telematics","device","741"];
    client.setQueryData(key,{priorDisplayRecord:true});
    const observer = new QueryObserver(client,{queryKey:key,queryFn:()=>fixture.telematicsService.getDeviceById(741),retry:false,staleTime:Infinity});
    const unsubscribe=observer.subscribe(()=>{});
    try {
      const refresh=loadRefresh({client});
      await refresh.refresh(record);
      assert.equal(refresh.states.warning,true);
      assert.equal(client.getQueryState(key).status,"error");
      assert.deepEqual(client.getQueryData(key),{priorDisplayRecord:true});
      assert.equal(receipt.status,"Removed");
      assert.equal(posts(fixture).length,1);
    } finally {unsubscribe();client.clear();}
  }
  for (const scenario of ["initially-offline", "retry-paused"]) {
    onlineManager.setOnline(true);
    let fail = false;
    const client = new QueryClient({defaultOptions:{queries:{retry:1,retryDelay:0,staleTime:Infinity}}});
    client.mount(); const key=["telematics","device","741"]; client.setQueryData(key,{priorDisplayRecord:true});
    const observer=new QueryObserver(client,{queryKey:key,queryFn:async()=>{ if(fail){ if(scenario==="retry-paused") onlineManager.setOnline(false); throw new Error("fixture read unavailable"); } return {fresh:true}; },retry:1,retryDelay:0,staleTime:Infinity});
    const unsubscribe=observer.subscribe(()=>{}); const refresh=loadRefresh({client});
    try {
      if(scenario==="initially-offline") onlineManager.setOnline(false); else fail=true;
      await refresh.refresh(record);
      assert.equal(refresh.states.warning,true,scenario); assert.equal(refresh.states.pending,false,scenario); assert.equal(refresh.states.subscriptions,0,scenario);
    } finally { onlineManager.setOnline(true); await client.cancelQueries(undefined,{silent:true}); unsubscribe(); client.unmount(); client.clear(); await tick(); }
  }
});

test("receipt identity and generation prevent same-device replacement or stale refresh from changing warning", async () => {
  const pending=[];
  const refresh=loadRefresh({invalidate:()=>new Promise((resolve,reject)=>pending.push({resolve,reject}))});
  const older=refresh.refresh(record);
  const replacement={...record,installationId:"903"};
  refresh.context.current.record=replacement;
  const newer=refresh.refresh(replacement);
  pending[2].reject(new Error("new read failed"));pending[3].resolve();
  await newer;
  pending[0].resolve();pending[1].resolve();
  await older;
  assert.equal(refresh.states.warning,true);
  await refresh.refresh(record);
  assert.equal(refresh.calls.length,4);
  const sameDeviceNewRecord={...replacement,effectiveTo:"2026-01-03T00:00:00Z"};
  refresh.context.current.record=sameDeviceNewRecord;
  await refresh.refresh(replacement);
  assert.equal(refresh.calls.length,4);
});

test("read-only retry clears warning; actual warning labels device, installation and effective time without readiness claims", async () => {
  const refresh=loadRefresh();
  refresh.states.warning=true;
  await refresh.refresh(record);
  assert.equal(refresh.states.warning,false);
  assert.deepEqual(refresh.calls,[[{queryKey:["telematics"]},{throwOnError:true}],[{queryKey:["iot-devices"]},{throwOnError:true}]]);
  const fn=page.statements.find(node=>ts.isFunctionDeclaration(node)&&node.name?.text==="RemovalRefreshNotice");
  assert.ok(fn);
  const h=(type,props,...children)=>({type,props:props??{},children:children.flat(Infinity)});
  const component=evaluate(fn.getText(page),{h},true).RemovalRefreshNotice;
  const nodes=[];
  const text=node=>{if(node==null||typeof node==="boolean")return "";if(typeof node!=="object")return String(node);nodes.push(node);return node.children.map(text).join(" ");};
  let reads=0;
  const message=text(component({record,busy:false,onRetry:()=>{reads++;}}));
  assert.ok(message.includes(effectiveTo));
  for(const fragment of [/741/,/902/,/Removal/,/recorded/,/may be out of date/])assert.match(message,fragment);
  assert.doesNotMatch(message,/physically verified|certified|streaming|telemetry proof/);
  nodes.find(node=>node.type==="button").props.onClick();
  assert.equal(reads,1);
  assert.equal((pageSource.match(/<RemovalRefreshNotice record=\{removalRecord\}/g)??[]).length,2);
  assert.match(pageSource,/onClose=\{closeRemoval\}/);
  assert.match(pageSource,/onSubmit=\{submitRemoval\}/);
  assert.match(pageSource,/error=\{formError \?\? removalError\?\.message \?\? null\}/);
  assert.match(pageSource,/const lifecycleError = [^\n]*removalError/);
});

test("every preflight failure, absent installation and invalid reason stop before POST", async () => {
  for (const failUrl of feeds) {
    const fixture = loadService({ failStage: "before", failUrl });
    await assert.rejects(fixture.telematicsService.unassignDevice(741, input));
    assert.equal(posts(fixture).length, 0);
  }
  for (const options of [{ current: null }]) {
    const fixture = loadService(options);
    await assert.rejects(fixture.telematicsService.unassignDevice(741, input));
    assert.equal(posts(fixture).length, 0);
  }
  for (const removalReason of ["", " ", "abc", "x".repeat(501), null, true, {}, ["reason"]]) {
    const fixture = loadService();
    await assert.rejects(fixture.telematicsService.unassignDevice(741, { ...input, removalReason }));
    assert.equal(posts(fixture).length, 0);
  }
});

test("safe canonical identities and versions are captured without loosening the accepted raw mapper", async () => {
  for (const id of [0, -1, 1.5, Number.MAX_SAFE_INTEGER + 1, "01", "741/remove", "9223372036854775808", true, {}]) {
    const fixture = loadService();
    await assert.rejects(fixture.telematicsService.unassignDevice(id, input));
    assert.deepEqual(fixture.calls, []);
    const installation = loadService({ current: { ...storedInstallation, id } });
    await assert.rejects(installation.telematicsService.unassignDevice(741, input));
    assert.equal(posts(installation).length, 0);
  }
  for (const rowVersion of [undefined, null, 0, -1, 1.5, 2147483647, 2147483648, true, [1], {}]) {
    const fixture = loadService({ current: { ...storedInstallation, rowVersion } });
    await assert.rejects(fixture.telematicsService.unassignDevice(741, input));
    assert.equal(posts(fixture).length, 0);
  }
  for (const [id, rowVersion] of [[902, 1], ["9223372036854775807", "2"], ["9007199254740992", 2147483646]]) {
    const fixture = loadService({ current: { ...storedInstallation, id, rowVersion } });
    const receipt = await fixture.telematicsService.unassignDevice("9223372036854775807", input);
    assert.deepEqual(receipt, { id: String(id), status: "Removed", effectiveTo });
    assert.equal(posts(fixture)[0][1], `/api/telemetry/devices/9223372036854775807/installations/${id}/remove`);
    assert.equal(posts(fixture)[0][2].expectedRowVersion, Number(rowVersion));
  }
});

test("invalid calendar offset future and nonmillisecond request instants are refused before POST", async () => {
  const invalid = [null, 0, true, {}, [], "", "2026-01-02", "2026-01-02T00:00Z", "2026-01-02T00:00:00",
    "2026-02-29T00:00:00Z", "1900-02-29T00:00:00Z", "2024-04-31T00:00:00Z", "2026-13-01T00:00:00Z",
    "2026-01-02T24:00:00Z", "2026-01-02T00:00:60Z", "2026-01-02T00:60:00Z",
    "2026-01-02T00:00:00-00:00", "2026-01-02T00:00:00+14:01", "2026-01-02T00:00:00-15:00",
    "2026-01-02T00:00:00+01:60", "0000-01-01T00:00:00Z", "0001-01-01T00:00:00+00:01",
    "9999-12-31T23:59:59.999-00:01", "10000-01-01T00:00:00Z", "2999-01-01T00:00:00Z",
    "2026-01-02T00:00:00.0000001Z", "2026-01-02T00:00:00.1234Z", "2026-01-02T00:00:00.00000000Z"];
  for (const value of invalid) {
    const fixture = loadService();
    await assert.rejects(fixture.telematicsService.unassignDevice(741, { ...input, effectiveTo: value }), undefined, String(value));
    assert.equal(posts(fixture).length, 0, String(value));
  }
});

test("equivalent offsets fraction zero padding Gregorian leap dates and UTC range edges retain exact receipt text", async () => {
  for (const [requestTime, receiptTime] of [
    ["2026-01-02T00:00:00Z", "2026-01-02T00:00:00.0000000+00:00"],
    ["2026-01-02T14:00:00+14:00", effectiveTo],
    ["2026-01-01T10:00:00-14:00", effectiveTo],
    ["2026-01-01T19:00:00-05:00", effectiveTo],
    ["2026-01-02T00:00:00.1Z", "2026-01-02T00:00:00.1000000Z"],
    ["2026-01-02T00:00:00.1230000Z", "2026-01-02T00:00:00.123+00:00"],
    ["2000-02-29T00:00:00Z", "2000-02-29T00:00:00+00:00"],
    ["0001-01-01T14:00:00+14:00", "0001-01-01T00:00:00Z"],
    ["0099-01-01T00:00:00Z", "0098-12-31T23:00:00-01:00"],
  ]) {
    const fixture = loadService({ data: { id: 902, status: "Removed", effectiveTo: receiptTime } });
    assert.deepEqual(await fixture.telematicsService.unassignDevice(741, { ...input, effectiveTo: requestTime }), { id: "902", status: "Removed", effectiveTo: receiptTime });
    assert.equal(posts(fixture)[0][2].effectiveTo, requestTime);
  }
  // Controlled clock only for the upper DateTimeOffset range, not a real removal.
  const originalNow = Date.now;
  Date.now = () => Date.parse("9999-12-31T23:59:59.999Z");
  try {
    const value = "9999-12-31T09:59:59.999-14:00";
    const fixture = loadService();
    assert.equal((await fixture.telematicsService.unassignDevice(741, { ...input, effectiveTo: value })).effectiveTo, value);
  } finally { Date.now = originalNow; }
});

test("malformed receipt identity status time aliases and submillisecond mismatches remain unconfirmed", async () => {
  const valid = { id: 902, status: "Removed", effectiveTo };
  class ReceiptCarrier { constructor() { Object.assign(this, valid); } }
  for (const data of [null, [], {}, new Date(), new ReceiptCarrier(), Object.create(valid),
    { ...valid, ID: 902 }, { ...valid, Status: "Removed" }, { ...valid, Effective_To: effectiveTo },
    { ...valid, id: 741 }, { ...valid, id: Number.MAX_SAFE_INTEGER + 1 },
    { ...valid, status: "Registered" }, { ...valid, effectiveTo: "2026-01-02T00:00:00.0000001Z" },
    { ...valid, effectiveTo: "2026-01-02T00:00:00.001Z" }, { ...valid, effectiveTo: "2026-02-30T00:00:00Z" },
    { ...valid, effectiveTo: "2026-01-02T00:00:00" }, { ...valid, effectiveTo: null },
    { ...valid, effectiveTo: [] }, { ...valid, effective_to: "2026-01-02T00:00:01Z" }]) {
    const fixture = loadService({ data });
    await assert.rejects(fixture.telematicsService.unassignDevice(741, input), error => error.outcome === "unconfirmed");
    assert.equal(posts(fixture).length, 1);
    assert.equal(fixture.calls.filter(call => call[1] === "after").length, 0);
  }
  const snake = loadService({ data: { id: "902", status: "Removed", effective_to: effectiveTo, rowVersion: 999, deviceState: "Registered", idempotentReplay: true } });
  assert.deepEqual(await snake.telematicsService.unassignDevice(741, input), { id: "902", status: "Removed", effectiveTo });
  const nullReceipt = Object.assign(Object.create(null), valid);
  const nullEnvelope = Object.assign(Object.create(null), { success: true, data: nullReceipt });
  assert.equal((await loadService({ envelope: nullEnvelope }).telematicsService.unassignDevice(741, input)).id, "902");
});

test("rejection and unknown outcomes stay distinct and removal does not invent terminal-state eligibility", async () => {
  for (const role of ["Driver", "Customer"]) {
    const fixture = loadService({ session: { role } });
    await assert.rejects(fixture.telematicsService.unassignDevice(741, input), /Permission denied/);
    assert.deepEqual(fixture.calls, []);
  }
  for (const device of [{ status: "Revoked", revokedAt: "2026-01-01T00:00:00Z" }, { status: "Suspended", deviceState: "Suspended" }, { status: "Active", deviceState: "Quarantined" }, { status: "Retired", deviceState: "Retired" }]) {
    const fixture = loadService({ device });
    assert.deepEqual(await fixture.telematicsService.unassignDevice(741, input), { id: "902", status: "Removed", effectiveTo });
  }
  for (const envelope of [null, [], 1, "invalid", {}, new Date(), Object.create({ success: true, data: { id: 902, status: "Removed", effectiveTo } }),
    { Success: true, data: {} }, { success: "true", data: {} }, { success: 1, data: {} }]) {
    const fixture = loadService({ envelope });
    await assert.rejects(fixture.telematicsService.unassignDevice(741, input), error => error.outcome === "unconfirmed");
    assert.equal(posts(fixture).length, 1);
  }
  for (const message of ["Installation not found, closed, or changed", "Removal cannot predate attributed telemetry"]) {
    const fixture = loadService({ failure: { response: { status: 409, data: { success: false, message } } } });
    await assert.rejects(fixture.telematicsService.unassignDevice(741, input), error => error.outcome === "rejected" && !error.message.includes(message));
    assert.equal(posts(fixture).length, 1);
  }
  for (const failure of [new Error("private timeout"), { response: { status: 500, data: { success: false } } }]) {
    const fixture = loadService({ failure });
    await assert.rejects(fixture.telematicsService.unassignDevice(741, input), error => error.outcome === "unconfirmed" && /history/i.test(error.message) && !error.message.includes("private"));
    assert.equal(posts(fixture).length, 1);
  }
});

test("captured removal facts survive mutable caller input during preflight and POST", async () => {
  for (const phase of ["preflight", "post"]) {
    const callerInput = { ...input };
    let release;
    const wait = new Promise(resolve => { release = resolve; });
    const fixture = loadService({ beforeGet: phase === "preflight" ? async url => { if (url === feeds[0]) await wait; } : undefined,
      beforePost: phase === "post" ? async () => { await wait; } : undefined });
    const request = fixture.telematicsService.unassignDevice(741, callerInput);
    await tick(); callerInput.effectiveTo = "2026-01-03T00:00:00Z"; callerInput.removalReason = "changed while pending"; release();
    assert.deepEqual(await request, { id: "902", status: "Removed", effectiveTo });
    assert.deepEqual(posts(fixture)[0][2], { ...input, expectedRowVersion: 2 });
  }
});

test("actual mutation observer keeps acknowledged removal success when callback invalidation fails", async () => {
  onlineManager.setOnline(true);
  const fixture = loadService();
  const refresh = loadRefresh({ fail: true });
  const ui = loadUi({ refresh, operation: (id, payload) => fixture.telematicsService.unassignDevice(id, payload) });
  try {
    ui.controls().openRemoval({ id: "741" });
    ui.controls().updateRemovalForm(() => ({ ...input }));
    assert.equal(ui.options.retry, false);
    ui.controls().submitRemoval({ preventDefault() {} });
    for (let attempt = 0; attempt < 20 && ui.observer.getCurrentResult().status === "pending"; attempt++) await tick();
    assert.equal(ui.observer.getCurrentResult().status, "success");
    assert.equal(ui.states.target, null);
    assert.deepEqual(ui.states.form, { effectiveTo: "", removalReason: "" });
    assert.equal(ui.states.error, null);
    assert.equal(ui.states.notice, `Removal recorded for device 741, installation 902, effective ${effectiveTo}.`);
    assert.deepEqual(ui.states.record, record);
    assert.equal(refresh.states.warning, true);
    assert.equal(posts(fixture).length, 1);
    assert.doesNotMatch(ui.states.notice, /physically|reactivat|telemetry stopped|Registered/);
  } finally { ui.client.clear(); }
});

test("actual form validation and error headings preserve uncertainty and no-submit boundaries", async () => {
  const ui = loadUi();
  try {
    ui.controls().openRemoval({ id: "741" });
    ui.controls().submitRemoval({ preventDefault() {} });
    assert.match(ui.states.formError, /removal effective time/);
    assert.equal(ui.submitted.length, 0);
    assert.equal(ui.session.current.pending, false);
  } finally { ui.client.clear(); }
  const denied = loadUi({ allowed: false });
  try { denied.controls().openRemoval({ id: "741" }); assert.equal(denied.states.target, null); } finally { denied.client.clear(); }
  const fixture = loadService({ envelope: { success: false, message: "private rejection" } });
  await assert.rejects(fixture.telematicsService.unassignDevice(741, input), error => error.outcome === "rejected" && !error.message.includes("private"));
  const fn = page.statements.find(node => ts.isFunctionDeclaration(node) && node.name?.text === "lifecycleFailureHeading");
  const scope = Object.fromEntries(["DeviceInstallationOutcomeError", "DeviceRemovalOutcomeError", "DeviceCommissioningOutcomeError", "DeviceActivationOutcomeError", "DeviceSuspensionOutcomeError"].map(name => [name, fixture[name]]));
  const heading = evaluate(fn.getText(page), scope, true).lifecycleFailureHeading;
  assert.equal(heading(new fixture.DeviceRemovalOutcomeError("unconfirmed")), "Removal outcome unconfirmed");
  assert.equal(heading(new fixture.DeviceRemovalOutcomeError("rejected")), "Removal request rejected");
  assert.equal(heading(new Error("other")), "Device lifecycle action failed");
});
