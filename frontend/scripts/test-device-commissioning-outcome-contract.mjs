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
const input = { result: "Failed", verificationReference: "fixture-observed-failure" };
const storedInstallation = { id: 902, deviceId: 741, status: "Installed", rowVersion: 2, activationVerifiedAt: null };
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
        ? { id: options.deviceId ?? 741, status: "Active", currentInstallation: current, installationHistory: history }
        : [];
      return { data: { success: true, data } };
    },
    post: async (url, body) => {
      calls.push(["POST", url, body]);
      await options.beforePost?.(url, body);
      if (failure) throw failure;
      acknowledged = true;
      const data = Object.hasOwn(options, "data") ? options.data : {
        id: current?.id, status: body.result === "Passed" ? "Verified" : "Failed",
        commissioningResult: body.result, rowVersion: Number(current?.rowVersion) + 1,
        verificationReference: "fixture-only-metadata", activationVerifiedAt: current?.activationVerifiedAt,
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
const record = { deviceId: "741", installationId: "902", result: "Failed", rowVersion: 3 };

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
  const refresh = evaluate(declaration("refreshCommissioningDisplay"), {
    commissioningRefreshContext: context,
    setCommissioningRefreshWarning: value => { states.warning = value; },
    setCommissioningRefreshPending: value => { states.pending = value; },
    queryClient, refreshReceiptQueries,
  });
  return { refresh, context, states, calls };
}

function loadUi({ operation = async () => ({ id: "902", commissioningResult: "Failed", status: "Failed", rowVersion: 3 }), refresh = loadRefresh(), allowed = true } = {}) {
  const states = { target: null, form: { result: "", verificationReference: "" }, formError: null, error: null, notice: null, record: null };
  const session = { current: { deviceId: null, generation: 0, pending: false } };
  const defaults = { result: "", verificationReference: "" };
  const setters = {
    setCommissionTarget: value => { states.target = value; },
    setCommissioningForm: value => { states.form = typeof value === "function" ? value(states.form) : value; },
    setFormError: value => { states.formError = value; },
    setCommissioningError: value => { states.error = value; },
    setNotice: value => { states.notice = value; },
    setCommissioningRecord: value => { states.record = value; },
    setCommissioningRefreshWarning: value => { refresh.states.warning = value; },
    setCommissioningRefreshPending: value => { refresh.states.pending = value; },
  };
  const scope = {
    ...setters, commissioningSession: session, defaultCommissioningForm: defaults,
    commissioningRefreshContext: refresh.context, refreshCommissioningDisplay: refresh.refresh,
    canGovernInstallations: allowed, commissionSingleFlight: singleFlight(),
  };
  scope.ownsCommissioningSession = evaluate(declaration("ownsCommissioningSession"), scope);
  const options = evaluate(declaration("installMut"), {
    ...scope, useMutation: value => value, telematicsService: { markInstalled: operation },
  });
  const client = new QueryClient({ defaultOptions: { mutations: { retry: false } } });
  const observer = new MutationObserver(client, options);
  const submitted = [];
  const installMut = {
    mutateAsync: variables => { submitted.push(variables); return observer.mutate(variables); },
    reset: () => observer.reset(),
  };
  const controls = () => {
    const renderedScope = { ...scope, installMut, commissionTarget: states.target, commissioningForm: states.form, commissioningRenderGeneration: session.current.generation };
    return Object.fromEntries(["openCommissioning","closeCommissioning","submitCommissioning","updateCommissioningForm"].map(name => [name, evaluate(declaration(name), renderedScope)]));
  };
  return { states, session, options, controls, refresh, submitted, observer, client };
}

test("Passed and Failed receipts preserve recording independently of unavailable post-ack reads", async () => {
  for (const result of ["Passed","Failed"]) {
    const current = { ...storedInstallation, activationVerifiedAt: result === "Passed" ? "2026-01-01T00:00:00Z" : null };
    const fixture = loadService({ current, failStage: "after" });
    const receipt = await fixture.telematicsService.markInstalled(741, { ...input, result });
    assert.deepEqual(receipt, { id: "902", commissioningResult: result, status: result === "Passed" ? "Verified" : "Failed", rowVersion: 3 });
    assert.equal(fixture.calls.filter(call => call[0] === "GET" && call[1] === "before").length, 4);
    assert.equal(fixture.calls.filter(call => call[0] === "GET" && call[1] === "after").length, 0);
    assert.deepEqual(posts(fixture), [["POST","/api/telemetry/devices/741/installations/902/commission",{ result, verificationReference: input.verificationReference, expectedRowVersion: 2 }]]);
  }
});

test("all preflight feed failures and missing installation prevent POST", async () => {
  for (const failUrl of feeds) {
    const fixture = loadService({ failStage: "before", failUrl });
    await assert.rejects(fixture.telematicsService.markInstalled(741,input));
    assert.equal(posts(fixture).length, 0);
  }
  const missing = loadService({ current: null });
  await assert.rejects(missing.telematicsService.markInstalled(741,input));
  assert.equal(posts(missing).length, 0);
});

test("validated result and reference remain captured across deferred preflight and POST", async () => {
  for (const phase of ["preflight", "post"]) {
    const callerInput = { ...input };
    let release;
    const deferred = new Promise(resolve => { release = resolve; });
    const fixture = loadService({
      beforeGet: phase === "preflight" ? async url => { if (url === feeds[0]) await deferred; } : undefined,
      beforePost: phase === "post" ? async () => { await deferred; } : undefined,
    });
    const request = fixture.telematicsService.markInstalled(741, callerInput);
    await tick();
    callerInput.result = "Passed";
    callerInput.verificationReference = "changed while pending";
    release();
    assert.deepEqual(await request, { id: "902", commissioningResult: "Failed", status: "Failed", rowVersion: 3 });
    assert.equal(posts(fixture).length, 1);
    assert.deepEqual(posts(fixture)[0][2], { ...input, expectedRowVersion: 2 });
  }
});

test("Passed heartbeat, reference, result and permission gates remain before submission", async () => {
  const noHeartbeat = loadService();
  await assert.rejects(noHeartbeat.telematicsService.markInstalled(741,{ ...input, result: "Passed" }), /heartbeat/);
  assert.equal(posts(noHeartbeat).length, 0);
  for (const value of [{ ...input, result: "" }, { ...input, result: "Other" }, { ...input, verificationReference: " " }, { ...input, verificationReference: "short" }, { ...input, verificationReference: "x".repeat(501) }]) {
    const fixture = loadService();
    await assert.rejects(fixture.telematicsService.markInstalled(741,value));
    assert.equal(posts(fixture).length, 0);
  }
  for (const role of ["Driver","Customer"]) {
    const fixture = loadService({ session: { role } });
    await assert.rejects(fixture.telematicsService.markInstalled(741,input), /Permission denied/);
    assert.deepEqual(fixture.calls, []);
  }
});

test("device and raw installation IDs reject invalid identities before any commissioning POST", async () => {
  for (const id of [0,-1,1.5,Number.MAX_SAFE_INTEGER + 1,"01"," 741","741/remove","9223372036854775808"]) {
    const fixture = loadService();
    await assert.rejects(fixture.telematicsService.markInstalled(id,input));
    assert.deepEqual(fixture.calls, []);
  }
  for (const id of [0,-1,1.5,Number.MAX_SAFE_INTEGER + 1,{},true,"01","902/remove","9223372036854775808"]) {
    const fixture = loadService({ current: { ...storedInstallation,id } });
    await assert.rejects(fixture.telematicsService.markInstalled(741,input));
    assert.equal(posts(fixture).length, 0);
  }
});

test("current and history mapper preserve canonical safe IDs, withhold invalid primary IDs and never guess alias", async () => {
  for (const id of [902, "9223372036854775807"]) {
    const fixture = loadService({ current: { ...storedInstallation,id }, history: [{ ...storedInstallation,id }] });
    const detail = await fixture.telematicsService.getDeviceById(741);
    assert.equal(detail.currentInstallation.id, String(id));
    assert.equal(detail.installations[0].id, String(id));
    const receipt = await fixture.telematicsService.markInstalled(741,input);
    assert.equal(receipt.id, String(id));
    assert.ok(posts(fixture)[0][1].includes("/installations/" + id + "/commission"));
  }
  for (const id of [Number.MAX_SAFE_INTEGER + 1,0,-1,"01",{},true]) {
    const fixture = loadService({ current: { ...storedInstallation,id,installation_id: 902 }, history: [{ ...storedInstallation,id }] });
    const detail = await fixture.telematicsService.getDeviceById(741);
    assert.equal(detail.currentInstallation.id, "");
    assert.equal(detail.installations[0].id, "");
  }
});

test("version preflight is bounded positive Int32 with exact next receipt version", async () => {
  for (const rowVersion of [undefined,null,0,-1,1.5,2147483647,2147483648]) {
    const fixture = loadService({ current: { ...storedInstallation,rowVersion } });
    await assert.rejects(fixture.telematicsService.markInstalled(741,input));
    assert.equal(posts(fixture).length, 0);
  }
  for (const rowVersion of [1,2147483646]) {
    const fixture = loadService({ current: { ...storedInstallation,rowVersion } });
    assert.equal((await fixture.telematicsService.markInstalled(741,input)).rowVersion, rowVersion + 1);
  }
});

test("raw boolean array and object versions cannot be coerced into commissioning concurrency tokens", async () => {
  const counts=[];
  for (const rowVersion of [true,false,[1],[],{}]) {
    const fixture=loadService({current:{...storedInstallation,rowVersion},failStage:"after"});
    await assert.rejects(fixture.telematicsService.markInstalled(741,input));
    counts.push(posts(fixture).length);
  }
  assert.deepEqual(counts,[0,0,0,0,0]);
  for (const rowVersion of [2, "2"]) {
    const current = { ...storedInstallation, rowVersion };
    const fixture = loadService({ current, history: [current] });
    const detail = await fixture.telematicsService.getDeviceById(741);
    assert.equal(detail.currentInstallation.rowVersion, 2);
    assert.equal(detail.installations[0].rowVersion, 2);
    assert.equal((await fixture.telematicsService.markInstalled(741, input)).rowVersion, 3);
  }
});

test("strict core receipt checks installation identity, result/status pairing and exact version increment", async () => {
  const valid = { id: 902, commissioningResult: "Failed", status: "Failed", rowVersion: 3 };
  class ReceiptCarrier { constructor() { Object.assign(this, valid); } }
  for (const data of [null,[],{}, new Date(), new ReceiptCarrier(), Object.create(valid),
    { ...valid, ID: 902 }, { ...valid, Status: "Failed" }, { ...valid, Commissioning_Result: "Failed" }, { ...valid, RowVersion: 3 },
    { ...valid,id: 741 }, { ...valid,id: Number.MAX_SAFE_INTEGER + 1 }, { ...valid,id: "0902" },
    { ...valid,commissioningResult: "Passed" }, { ...valid,status: "Verified" }, { ...valid,rowVersion: 2 }, { ...valid,rowVersion: 4 },
    { ...valid,rowVersion: 3.5 }, { ...valid,rowVersion: "3" }, { ...valid,rowVersion: 2147483648 },
    { ...valid,commissioning_result: "Passed" }, { ...valid,row_version: 4 }]) {
    const fixture = loadService({ data });
    await assert.rejects(fixture.telematicsService.markInstalled(741,input), error => error.outcome === "unconfirmed");
    assert.equal(posts(fixture).length, 1);
    assert.equal(fixture.calls.filter(call => call[1] === "after").length, 0);
  }
  const snake = loadService({ data: { id: "902", commissioning_result: "Failed", status: "Failed", row_version: 3 } });
  assert.equal((await snake.telematicsService.markInstalled(741,input)).id,"902");
  const nullReceipt = Object.assign(Object.create(null), valid);
  const nullEnvelope = Object.assign(Object.create(null), { success: true, data: nullReceipt });
  assert.equal((await loadService({ envelope: nullEnvelope }).telematicsService.markInstalled(741, input)).id, "902");
});

test("unused proof metadata is never reconstructed or emitted by a valid minimal receipt", async () => {
  for (const metadata of [{}, { verificationReference: { private: "untrusted" }, activationVerifiedAt: ["not","evidence"], deviceId: 999, idempotentReplay: true }]) {
    const fixture = loadService({ data: { id: 902, commissioningResult: "Failed", status: "Failed", rowVersion: 3, ...metadata } });
    assert.deepEqual(await fixture.telematicsService.markInstalled(741,input), { id: "902", commissioningResult: "Failed", status: "Failed", rowVersion: 3 });
  }
});

test("rejection and unconfirmed receipt or transport are distinct with static safe errors and no repeated POST", async () => {
  for (const envelope of [null,[],1,"invalid",{}, new Date(), Object.create({ success: true, data: { id: 902, commissioningResult: "Failed", status: "Failed", rowVersion: 3 } }),
    { Success: true, data: { id: 902, commissioningResult: "Failed", status: "Failed", rowVersion: 3 } }, { success: "true",data:{} }, { success: 1,data:{} }]) {
    const fixture = loadService({ envelope });
    await assert.rejects(fixture.telematicsService.markInstalled(741,input), error => error.outcome === "unconfirmed");
    assert.equal(posts(fixture).length, 1);
  }
  const rejected = loadService({ failure: { response: { status: 409,data:{success:false,message:"raw private detail"} } } });
  await assert.rejects(rejected.telematicsService.markInstalled(741,input), error => error.outcome === "rejected" && !error.message.includes("raw private detail"));
  for (const failure of [new Error("raw private detail"), { response:{status:500,data:{success:false}} }]) {
    const fixture = loadService({ failure });
    await assert.rejects(fixture.telematicsService.markInstalled(741,input), error => error.outcome === "unconfirmed" && !error.message.includes("raw private detail"));
    assert.equal(posts(fixture).length,1);
  }
});

test("actual form and installed mutation retain recorded Failed versus Passed and clear only acknowledged draft", async () => {
  for (const result of ["Passed","Failed"]) {
    const refresh = loadRefresh({ fail: true });
    const ui = loadUi({ refresh, operation: async () => ({ id:"902",commissioningResult:result,status:result === "Passed" ? "Verified":"Failed",rowVersion:3 }) });
    try {
      assert.equal(ui.options.retry,false);
      ui.controls().openCommissioning({id:"741",deviceName:"Fixture"});
      ui.controls().updateCommissioningForm(() => ({ ...input,result }));
      ui.controls().submitCommissioning({preventDefault(){}});
      await tick(); await tick();
      assert.equal(ui.states.target,null);
      assert.deepEqual(ui.states.form,{result:"",verificationReference:""});
      assert.equal(ui.states.error,null);
      assert.equal(ui.states.notice,"Commissioning result " + result + " recorded for device 741, installation 902.");
      assert.equal(ui.states.record.result,result);
      assert.equal(ui.observer.getCurrentResult().status,"success");
      assert.equal(refresh.states.warning,true);
      assert.equal(ui.submitted.length,1);
    } finally { ui.client.clear(); }
  }
});

test("synchronous submit guards duplicate, pending close/open/input, then releases after rejection", async () => {
  const pending = [];
  const ui = loadUi({ operation: () => new Promise((resolve,reject) => pending.push({resolve,reject})) });
  try {
    ui.controls().openCommissioning({id:"741"});
    ui.controls().updateCommissioningForm(() => ({...input}));
    const controls = ui.controls();
    controls.submitCommissioning({preventDefault(){}});
    controls.submitCommissioning({preventDefault(){}});
    controls.closeCommissioning();
    controls.openCommissioning({id:"742"});
    controls.updateCommissioningForm(() => ({...input,verificationReference:"changed"}));
    assert.equal(ui.submitted.length,1);
    assert.equal(ui.states.target.id,"741");
    assert.deepEqual(ui.states.form,input);
    await tick();
    pending[0].reject(new Error("fixture rejected submission"));
    await tick(); await tick();
    assert.equal(ui.session.current.pending,false);
    assert.equal(ui.states.target.id,"741");
    assert.equal(ui.states.error.message,"fixture rejected submission");
    ui.controls().submitCommissioning({preventDefault(){}});
    await tick();
    assert.equal(ui.submitted.length,2);
    pending[1].resolve({id:"902",commissioningResult:"Failed",status:"Failed",rowVersion:3});
    await tick(); await tick();
    assert.equal(ui.session.current.pending,false);
  } finally { ui.client.clear(); }
});

test("stale closed-session callbacks cannot submit or alter a later same-device draft", () => {
  const ui = loadUi();
  try {
    ui.controls().openCommissioning({id:"741"});
    ui.controls().updateCommissioningForm(() => ({...input}));
    const stale = ui.controls();
    stale.closeCommissioning();
    ui.controls().openCommissioning({id:"741"});
    ui.controls().updateCommissioningForm(() => ({...input,verificationReference:"new-session-reference"}));
    stale.closeCommissioning(); stale.updateCommissioningForm(() => ({...input}));
    stale.submitCommissioning({preventDefault(){}});
    assert.equal(ui.states.target.id,"741");
    assert.equal(ui.states.form.verificationReference,"new-session-reference");
    assert.equal(ui.submitted.length,0);
  } finally { ui.client.clear(); }
});

test("acknowledged form callbacks are revoked before any later form opens and completion handoff stays locked", async () => {
  const ui = loadUi();
  let stale;
  const release = ui.observer.subscribe(state => {
    if (state.status === "success") {
      // Installed mutation notifications occur before mutateAsync and shared-flight settlement.
      assert.equal(ui.session.current.pending, true);
      stale.submitCommissioning({ preventDefault() {} });
      ui.controls().openCommissioning({ id: "742" });
      assert.equal(ui.states.target, null);
    }
  });
  try {
    ui.controls().openCommissioning({ id: "741" });
    ui.controls().updateCommissioningForm(() => ({ ...input }));
    stale = ui.controls();
    stale.submitCommissioning({ preventDefault() {} });
    await tick(); await tick();
    assert.equal(ui.session.current.pending, false);
    stale.submitCommissioning({ preventDefault() {} });
    stale.updateCommissioningForm(() => ({ ...input }));
    stale.closeCommissioning();
    assert.equal(ui.submitted.length, 1);
    assert.equal(ui.states.target, null);
    assert.deepEqual(ui.states.form, { result: "", verificationReference: "" });
    assert.equal(ui.states.record.installationId, "902");
    ui.controls().openCommissioning({ id: "742" });
    assert.equal(ui.states.target.id, "742");
  } finally { release(); ui.client.clear(); }
});

test("rejected submission handoff rejects queued admission without stranding the pending guard", async () => {
  const actual = declaration("submitCommissioning");
  // Explicit negative control recreates only the discarded inner-finally handoff.
  // It is not a second production path or a real device command.
  const obsolete = actual.replace(
    /void commissionSingleFlight\(\(\) => installMut\.mutateAsync\(variables\)\)\.finally\(\(\) => \{[\s\S]*?\n    \}\);/,
    `void commissionSingleFlight(async () => {
      try { await installMut.mutateAsync(variables); }
      finally { if (ownsCommissioningSession(variables)) commissioningSession.current.pending = false; }
    });`,
  );
  assert.notEqual(obsolete, actual, "negative-control replacement must touch the exact handoff");
  for (const [source, vulnerable] of [[actual, false], [obsolete, true]]) {
    let reject;
    const pending = new Promise((_, fail) => { reject = fail; });
    const session = { current: { deviceId: "741", generation: 0, pending: false } };
    let dispatches = 0;
    const scope = {
      canGovernInstallations: true, commissionTarget: { id: "741" }, commissioningForm: { ...input },
      commissioningSession: session, commissioningRenderGeneration: 0, setFormError() {},
      commissionSingleFlight: singleFlight(), installMut: { mutateAsync: () => { dispatches++; return pending; } },
    };
    scope.ownsCommissioningSession = evaluate(declaration("ownsCommissioningSession"), scope);
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
      ui.controls().openCommissioning({id:"741"});
      ui.controls().updateCommissioningForm(() => ({...input}));
      ui.controls().submitCommissioning({preventDefault(){}});
      await tick();
      // Adversarial harness seam, not an allowed normal pending close/reopen journey.
      ui.session.current = { deviceId:"742",generation:ui.session.current.generation+1,pending:false };
      ui.states.target={id:"742"}; ui.states.form={...input,verificationReference:"later-form"}; ui.states.notice="later notice";
      settle(fail ? new Error("old failure") : {id:"902",commissioningResult:"Failed",status:"Failed",rowVersion:3});
      await tick(); await tick();
      assert.equal(ui.states.target.id,"742");
      assert.equal(ui.states.form.verificationReference,"later-form");
      assert.equal(ui.states.notice,"later notice");
      assert.equal(ui.states.error,null);
      assert.equal(ui.states.record,null);
    } finally { ui.client.clear(); }
  }
});

test("post-ack detail and each optional-feed failure remains separate under installed query behavior", async () => {
  for (const failUrl of feeds) {
    const fixture = loadService({failStage:"after",failUrl});
    const receipt = await fixture.telematicsService.markInstalled(741,input);
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
      assert.equal(receipt.commissioningResult,"Failed");
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
  const sameDeviceNewRecord={...replacement,result:"Passed",rowVersion:4};
  refresh.context.current.record=sameDeviceNewRecord;
  await refresh.refresh(replacement);
  assert.equal(refresh.calls.length,4);
});

test("read-only retry clears warning; actual warning labels device, installation and Failed without readiness claims", async () => {
  const refresh=loadRefresh();
  refresh.states.warning=true;
  await refresh.refresh(record);
  assert.equal(refresh.states.warning,false);
  assert.deepEqual(refresh.calls,[[{queryKey:["telematics"]},{throwOnError:true}],[{queryKey:["iot-devices"]},{throwOnError:true}]]);
  const fn=page.statements.find(node=>ts.isFunctionDeclaration(node)&&node.name?.text==="CommissioningRefreshNotice");
  assert.ok(fn);
  const h=(type,props,...children)=>({type,props:props??{},children:children.flat(Infinity)});
  const component=evaluate(fn.getText(page),{h},true).CommissioningRefreshNotice;
  const nodes=[];
  const text=node=>{if(node==null||typeof node==="boolean")return "";if(typeof node!=="object")return String(node);nodes.push(node);return node.children.map(text).join(" ");};
  let reads=0;
  const message=text(component({record,busy:false,onRetry:()=>{reads++;}}));
  for(const fragment of [/741/,/902/,/Failed/,/recorded/,/may be out of date/])assert.match(message,fragment);
  assert.doesNotMatch(message,/physically verified|certified|streaming|telemetry proof/);
  nodes.find(node=>node.type==="button").props.onClick();
  assert.equal(reads,1);
  assert.equal((pageSource.match(/<CommissioningRefreshNotice record=\{commissioningRecord\}/g)??[]).length,2);
  assert.match(pageSource,/onClose=\{closeCommissioning\}/);
  assert.match(pageSource,/onSubmit=\{submitCommissioning\}/);
  assert.match(pageSource,/error=\{formError \?\? commissioningError\?\.message \?\? null\}/);
  assert.match(pageSource,/const lifecycleError = [^\n]*commissioningError/);
});
