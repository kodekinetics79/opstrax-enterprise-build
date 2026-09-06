import assert from "node:assert/strict";
import { createRequire } from "node:module";
import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { renderToStaticMarkup } from "react-dom/server";

// Actual flat Safety service/unwrap/hook, query/mutation observers and page/Drawer.
// Controlled React frames/transport/export; no HTTP, DB or browser.
const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const require = createRequire(import.meta.url);
const esbuild = require("esbuild");
const { QueryClient, QueryObserver, MutationObserver } = require("@tanstack/react-query");
const client = new QueryClient({ defaultOptions: { queries: { retry: 1, refetchOnWindowFocus: false, staleTime: 30_000 }, mutations: { retry: false } } });
const states = { page: [], drawer: [] }, refs = { page: [], drawer: [] }, mutations = [], queries = new Map(), subscriptions = [];
let scope = "page", stateCursor = 0, refCursor = 0, mutationCursor = 0, resets = 0, directPermission = true, exportPermission = true, cleanup, snapshotOverride, reviewTransport;
const calls = [], exports = [], reads = [], failures = new Map(), transports = new Map();
const taskA = {
  id: 41, rowVersion: 3, eventType: "harsh_braking", severity: "High", status: "open",
  eventTime: "2026-09-05T12:00:00Z", driverId: 7, driverName: "Synthetic safety driver",
  vehicleId: 9, vehicleCode: "SYN-09", notes: "Synthetic event notes",
  reviewedAt: null, reviewedByName: null, resolvedAt: null, resolvedByName: null,
  coachingTasks: [{ id: 13, status: "Open", coachingType: "Following Distance", dueDate: "2026-09-12T12:00:00Z", assignedToName: "Synthetic assignee", assignedByName: "Synthetic assigner", completedAt: null, driverAcknowledgedAt: null, notes: "PRIVATE_COACHING_NOTES", outcome: "PRIVATE_COACHING_OUTCOME" }],
  auditTrail: [{ actionName: "Synthetic audit event", actorName: "Synthetic actor", createdAt: "2026-09-05T12:00:00Z", notes: "PRIVATE_AUDIT_NOTES", details_json: "PRIVATE_AUDIT_DETAILS" }],
  driverEmail: "PRIVATE_EMAIL", metaJson: { secret: "PRIVATE_META" }, evidenceHash: "PRIVATE_HASH",
  sourceAlert: { secret: "PRIVATE_SOURCE" }, systemInsight: "PRIVATE_INSIGHT", unknownNested: { secret: "PRIVATE_UNKNOWN" },
};
const taskB = { ...taskA, id: 99, driverName: "Synthetic other driver" };
const details = new Map([["41", taskA], ["99", taskB]]);
const originalDocument = globalThis.document;
const fakeDialog = { isConnected: true, hidden: false, querySelectorAll: () => [], querySelector: () => null, contains: () => false };
globalThis.document = { activeElement: null, querySelectorAll: () => [fakeDialog], addEventListener() {}, removeEventListener() {} };
const apiClient = {
  get: async url => { const id = url.split("/").at(-1); reads.push(id); if (transports.has(id)) return { data: { success: true, data: await transports.get(id)() } }; if (failures.has(id)) throw failures.get(id); return { data: { success: true, data: details.get(id) } }; },
  post: async (url, payload) => { calls.push({ url, payload }); return { data: { success: true, data: reviewTransport ? await reviewTransport() : { id: url.split("/").at(-2) } } }; },
};
const clientSource = readFileSync(resolve(root, "src/services/apiClient.ts"), "utf8");
const actualUnwrap = clientSource.slice(clientSource.indexOf("export async function unwrap<T>"));
const serviceSource = readFileSync(resolve(root, "src/services/safetyApi.ts"), "utf8").replace(/^import .*;$/gm, "");
const serviceBuilt = await esbuild.transform(actualUnwrap + "\n" + serviceSource, { loader: "ts", format: "cjs" });
const serviceModule = { exports: {} };
new Function("module", "exports", "apiClient", serviceBuilt.code)(serviceModule, serviceModule.exports, apiClient);
const api = serviceModule.exports.safetyApi;
const hookSource = readFileSync(resolve(root, "src/hooks/useBatch4.ts"), "utf8");
const hookBuilt = await esbuild.transform(hookSource.replace(/^import .*;$/gm, ""), { loader: "ts", format: "cjs" });
const hookModule = { exports: {} };
new Function("module", "exports", "useQuery", "safetyApi", hookBuilt.code)(hookModule, hookModule.exports, options => options, api);
function observer(id) {
  if (!queries.has(id)) {
    const query = new QueryObserver(client, hookModule.exports.useSafetyEventDetail(id));
    queries.set(id, query); subscriptions.push(query.subscribe(() => {}));
  }
  return queries.get(id);
}
const hooks = {
  useState(initial) { const index = stateCursor++; const store = states[scope]; if (!(index in store)) store[index] = typeof initial === "function" ? initial() : initial; return [store[index], next => { store[index] = typeof next === "function" ? next(store[index]) : next; }]; },
  useMemo: callback => callback(), useCallback: callback => callback,
  useRef(initial) { return refs[scope][refCursor++] ??= { current: initial }; },
  useEffect(effect) { cleanup?.(); cleanup = effect(); },
  useAuth: () => ({ session: { user: {}, company: {} } }),
  useHasPermission: () => () => exportPermission,
  useHasDirectPermission: () => () => directPermission,
  useQueryClient: () => client,
  query(name, id) { return name.includes("Detail") ? snapshotOverride ?? observer(id).getCurrentResult() : { data: name.includes("Summary") ? {} : [taskA, taskB], isLoading: false, isFetching: false, isError: false }; },
  useMutation(options) { const index = mutationCursor++; const mutation = mutations[index] ??= new MutationObserver(client, options); mutation.setOptions(options); return { ...mutation.getCurrentResult(), mutateAsync: mutation.mutate.bind(mutation), reset: () => { resets++; mutation.reset(); } }; },
};
async function loadHook(file, importLine) {
  const source = readFileSync(resolve(root, file), "utf8");
  const built = await esbuild.transform(source.replace(importLine, "const {useCallback,useRef,useEffect}=__hooks;"), { loader: "ts", format: "cjs" });
  const module = { exports: {} }; new Function("module", "exports", "__hooks", built.code)(module, module.exports, hooks); return module.exports;
}
hooks.useSingleFlight = (await loadHook("src/hooks/useSingleFlight.ts", 'import { useCallback, useRef } from "react";')).useSingleFlight;
hooks.useDialogFocus = (await loadHook("src/hooks/useDialogFocus.ts", 'import { useEffect, useRef } from "react";')).useDialogFocus;
const source = readFileSync(resolve(root, "src/pages/Batch4SafetyPage.tsx"), "utf8");
const inspected = source
  .replace(/import \{ FormEvent, ReactNode, useMemo, useState, useRef \} from "react";/, 'import type { FormEvent, ReactNode } from "react"; const {useMemo,useState,useRef}=__hooks;')
  .replace('import { useMutation, useQueryClient } from "@tanstack/react-query";', 'const {useMutation,useQueryClient}=__hooks;')
  .replace('PageHeader, exportCsv, labelize', 'PageHeader, labelize')
  .replace('import { useHasDirectPermission, useHasPermission } from "@/hooks/usePermission";', 'const {useHasDirectPermission,useHasPermission}=__hooks; const exportCsv=(...args)=>__exports.push(args);')
  .replace(/import \{ ([^;]+) \} from "@\/hooks\/useBatch4";/, (_line, names) => names.split(",").map(name => `const ${name.trim()}=(id)=>__hooks.query(${JSON.stringify(name.trim())},id);`).join("\n"))
  .replace('import { useSingleFlight } from "@/hooks/useSingleFlight";', 'const {useSingleFlight}=__hooks;')
  .replace('import { useDialogFocus } from "@/hooks/useDialogFocus";', 'const {useDialogFocus}=__hooks;')
  .replace('import { useAuth } from "@/hooks/useAuth";', 'const {useAuth}=__hooks;')
  .replace('import { CameraMetadataDialog, useCameraMetadataWorkflow } from "@/components/CameraMetadataDialog";', 'const CameraMetadataDialog=()=>null; const useCameraMetadataWorkflow=()=>({ notice:null, warning:false, refreshing:false, pending:false, editor:null, error:null, canEdit:false, detailReady:false, open(){}, exportCurrent(){}, canLeave(){return true}, refresh(){}, change(){}, close(){}, submit(){} });')
  .replace('import { safetyApi } from "@/services/safetyApi";', 'const safetyApi=__api;')
  .replace("function Drawer(", "export function Drawer(");
const built = await esbuild.build({ stdin: { contents: inspected, resolveDir: root, loader: "tsx" }, bundle: true, platform: "node", format: "cjs", jsx: "automatic", write: false, nodePaths: process.env.NODE_PATH ? [process.env.NODE_PATH] : [], alias: { "@": resolve(root, "src") }, define: { "import.meta.env": "{}" }, logLevel: "silent" });
const module = { exports: {} }; new Function("require", "module", "exports", "__hooks", "__api", "__exports", built.outputFiles[0].text)(require, module, module.exports, hooks, api, exports);
const { Batch4SafetyPage } = module.exports;
function find(node, predicate) { if (!node || typeof node !== "object") return; if (Array.isArray(node)) { for (const child of node) { const hit = find(child, predicate); if (hit) return hit; } return; } return predicate(node) ? node : find(node.props?.children, predicate); }
function page() { scope = "page"; stateCursor = refCursor = mutationCursor = 0; return Batch4SafetyPage({ kind: "safety" }); }
function drawer(tree = page()) { return find(tree, node => typeof node.props?.onEdit === "function"); }
function renderDrawer(element = drawer()) { scope = "drawer"; stateCursor = refCursor = 0; return element.type(element.props); }
function button(tree, label) { return find(tree, node => node.type === "button" && (node.props["aria-label"] === label || node.props.children === label || (Array.isArray(node.props.children) && node.props.children.includes(` ${label}`)))); }
function select(row, tree = page()) { find(tree, node => typeof node.props?.onSelect === "function").props.onSelect(row); }
function loaded(row) { details.set(String(row.id), row); client.setQueryData(["safety", "detail", row.id], row); select(row); return drawer(); }
function deferred() { let resolve, reject; const promise = new Promise((yes, no) => { resolve = yes; reject = no; }); return { promise, resolve, reject }; }
function tick() { return new Promise(resolve => setImmediate(resolve)); }
function visibleModal(tree = page()) { return find(tree, node => ["Modal", "CoachingNoteModal", "CoachingCompleteModal"].includes(node.type?.name)); }
function snapshot() { return { mutations: calls.length, resets, exports: exports.length }; }

async function assertInert(old, oldTree) {
  const before = snapshot();
  old.props.onEdit(taskA); await tick();
  for (const type of ["review", "dismiss", "resolve", "createCoaching", "createIncident", "unknown"]) { old.props.onAction(type, taskA); await tick(); }
  button(oldTree, "Export Report").props.onClick(); await tick();
  assert.deepEqual(snapshot(), before, "each stale detail callback independently has zero effects");
  assert.equal(visibleModal(), undefined);
}
function unavailable(role = "alert") {
  const tree = renderDrawer(); const html = renderToStaticMarkup(tree);
  assert.match(html, new RegExp('role="' + role + '"'));
  assert.doesNotMatch(html, /Synthetic safety driver|Synthetic event notes|PRIVATE_/);
  assert.equal(button(tree, "Review"), undefined); assert.equal(button(tree, "Export Report"), undefined);
  assert.ok(button(tree, "Close detail"));
  return tree;
}
function assertSafeHtml(tree) {
  const html = renderToStaticMarkup(tree);
  assert.match(html, /Synthetic safety driver/); assert.match(html, /Synthetic event notes/);
  assert.match(html, /Synthetic audit event/); assert.match(html, /2026-09-12/);
  assert.doesNotMatch(html, /PRIVATE_|Dashcam Events|Incident Watch|Evidence \/ Legal Readiness|Event Summary \/ Action|Due At/);
}
try {
  // API/hook contract remains flat; route assertions target registered handlers.
  const endpoints = readFileSync(resolve(root, "../backend-dotnet/Controllers/EndpointMappings.cs"), "utf8");
  assert.match(endpoints, /app.MapGet\("\/api\/safety\/events\/\{id:long\}", SafetyEventGet\)/);
  assert.match(endpoints, /app.MapPost\("\/api\/safety\/events\/\{id:long\}\/review", SafetyEventReview\)/);
  assert.match(endpoints, /app.MapPost\("\/api\/safety\/events\/\{id:long\}\/coaching", CanonicalCoachingFromSafetyEvent\)/);
  const flat = await api.detail(41); assert.equal(flat.id, 41); assert.equal(flat.record, undefined);
  const actual = observer(41); await actual.refetch(); assert.equal(actual.getCurrentResult().data.id, 41);
  select(taskA);
  let old = drawer(), oldTree = renderDrawer(old);
  assert.ok(oldTree, "a successful registered flat safety response must open the actual review drawer"); // RED on 2f2d408.
  assertSafeHtml(oldTree); assert.equal(button(oldTree, "Review").props.disabled, false);

  const displayFields = ["id","rowVersion","eventType","severity","status","eventTime","driverId","driverName","vehicleId","vehicleCode","notes","reviewedAt","reviewedByName","resolvedAt","resolvedByName"];
  const exportFields = displayFields.filter(key => key !== "notes");
  assert.deepEqual(Object.keys(old.props.detail.record).sort(), [...displayFields].sort());
  button(oldTree, "Export Report").props.onClick(); await tick();
  assert.equal(exports.length, 1);
  assert.deepEqual(Object.keys(exports[0][1][0]).sort(), [...exportFields].sort());
  assert.doesNotMatch(JSON.stringify(exports), /PRIVATE_|Synthetic event notes|coachingTasks|auditTrail/);
  assert.equal(calls.length, 0, "read/render/export must not cause a write");

  // A manual permitted review crosses the actual unchanged service boundary once.
  button(oldTree, "Review").props.onClick(); await tick(); await tick();
  assert.deepEqual(calls, [{ url: "/api/safety/events/41/review", payload: { notes: undefined } }]);
  assert.equal((await api.detail(41)).record, undefined);

  for (const failure of [Object.assign(new Error("PRIVATE_ERROR403"), { response: { status: 403 } }), Object.assign(new Error("PRIVATE_ERROR404"), { response: { status: 404 } }), new Error("PRIVATE_TRANSPORT")]) {
    old = loaded(taskA); oldTree = renderDrawer(old);
    failures.set("41", failure); const beforeReads = reads.length;
    await observer(41).refetch(); assert.equal(reads.length - beforeReads, 2);
    assert.equal(observer(41).getCurrentResult().isRefetchError, true);
    await assertInert(old, oldTree); let tree = unavailable();
    const before = snapshot(); const beforeRetry = reads.length;
    button(tree, "Retry detail").props.onClick(); await tick();
    for (let wait = 0; observer(41).getCurrentResult().isFetching && wait < 200; wait++) await new Promise(resolve => setTimeout(resolve, 25));
    assert.equal(observer(41).getCurrentResult().isFetching, false);
    assert.equal(reads.length - beforeRetry, 2); assert.deepEqual(snapshot(), before);
    tree = unavailable(); failures.delete("41");
    const retry = button(tree, "Retry detail"); retry.props.onClick(); retry.props.onClick();
    await assertInert(old, oldTree); await tick(); await tick(); assertSafeHtml(renderDrawer());
  }
  old = loaded(taskA); oldTree = renderDrawer(old);
  const held = deferred(); transports.set("41", () => held.promise);
  const fetching = observer(41).refetch();
  await assertInert(old, oldTree); unavailable("status");
  transports.delete("41"); held.resolve(taskA); await fetching;

  // Malformed required fields/arrays/rows never become seemingly empty history.
  for (const change of [
    { id: 99 }, { id: undefined }, { id: Number.MAX_SAFE_INTEGER + 1 }, { id: "01" }, { id: "1e2" }, { id: "9223372036854775808" },
    { rowVersion: "3" }, { rowVersion: null }, { rowVersion: -1 }, { rowVersion: 0.1 }, { rowVersion: NaN },
    { eventType: "" }, { status: " " }, { eventType: {} }, { notes: {} }, { driverName: [] },
    { coachingTasks: undefined }, { coachingTasks: {} }, { auditTrail: null },
    { coachingTasks: [null] }, { coachingTasks: [[]] }, { coachingTasks: [{}] }, { coachingTasks: [{ ignoredOnly: "x" }] },
    { coachingTasks: [{ id: 13, dueDate: {} }] }, { auditTrail: [{ actionName: [] }] }, { auditTrail: ["malformed"] },
  ]) {
    old = loaded(taskA); oldTree = renderDrawer(old);
    client.setQueryData(["safety", "detail", 41], { ...taskA, ...change });
    await assertInert(old, oldTree); unavailable();
  }
  for (const invalid of [{}, { record: taskA }, [], null]) {
    old = loaded(taskA); oldTree = renderDrawer(old);
    client.setQueryData(["safety", "detail", 41], invalid);
    await assertInert(old, oldTree); unavailable();
  }
  old = loaded(taskA); oldTree = renderDrawer(old); select({ id: "041" });
  await assertInert(old, oldTree); unavailable();
  for (const id of [1, Number.MAX_SAFE_INTEGER, "41", "9223372036854775807"]) {
    loaded({ ...taskA, id }); assert.ok(button(renderDrawer(), "Review"));
  }
  loaded({ id: 41, rowVersion: 0, eventType: "recorded", status: "open", coachingTasks: [], auditTrail: [] });
  assert.deepEqual(Object.keys(drawer().props.detail.record).sort(), ["id","rowVersion","eventType","status"].sort());
  assert.doesNotMatch(renderToStaticMarkup(renderDrawer()), /Synthetic|PRIVATE_/);
  loaded({ ...taskA, driverId: 0, notes: false, coachingTasks: [{ id: 0, status: false }], auditTrail: [{ actionName: null, actorName: false, createdAt: 0 }] });
  assert.equal(drawer().props.detail.record.driverId, 0); assert.equal(drawer().props.detail.record.notes, false);
  assert.deepEqual(drawer().props.detail.coachingTasks, [{ id: 0, status: false }]);
  assert.deepEqual(drawer().props.detail.auditTrail, [{ actionName: null, actorName: false, createdAt: 0 }]);
  loaded(taskA);

  // Same-turn selection/Close/retry revoke old callbacks and cannot later revive them.
  old = drawer(); oldTree = renderDrawer(old); client.setQueryData(["safety", "detail", 99], taskB); select(taskB);
  await assertInert(old, oldTree); assert.equal(drawer().props.detail.record.id, 99);
  old = drawer(); oldTree = renderDrawer(old); old.props.onClose(); await assertInert(old, oldTree); assert.equal(renderDrawer(), null);
  loaded(taskB); await assertInert(old, oldTree);
  const cold = deferred(); transports.set("77", () => cold.promise); select({ id: 77 });
  unavailable("status"); const oldCold = drawer(); select(taskB);
  cold.resolve({ ...taskA, id: 77 }); await tick(); await tick(); oldCold.props.onClose();
  assert.equal(drawer().props.detail.record.id, 99);
  old = loaded(taskA); oldTree = renderDrawer(old); failures.set("41", new Error("PRIVATE_RETRY")); await observer(41).refetch();
  const retryTree = unavailable(), retry = button(retryTree, "Retry detail"), retryHeld = deferred();
  failures.delete("41"); transports.set("41", () => retryHeld.promise); retry.props.onClick(); retry.props.onClick();
  await assertInert(old, oldTree); unavailable("status"); select(taskB); const oldReadCount = reads.length; retry.props.onClick();
  assert.equal(reads.length, oldReadCount); transports.delete("41"); retryHeld.resolve(taskA); await tick(); await tick();
  assert.equal(drawer().props.detail.record.id, 99);

  // Current cache is the single context/action source even with a defensive stale hook snapshot.
  old = loaded(taskA); oldTree = renderDrawer(old);
  const current = { ...taskA, rowVersion: 9, driverName: "Current safety driver" };
  details.set("41", current); client.setQueryData(["safety", "detail", 41], current);
  snapshotOverride = { ...observer(41).getCurrentResult(), data: { ...taskB, notes: "WRONG_SNAPSHOT_NOTES" } };
  assert.equal(drawer().props.detail.record.id, 41);
  assert.doesNotMatch(renderToStaticMarkup(renderDrawer()), /WRONG_SNAPSHOT_NOTES|Synthetic other driver/);
  snapshotOverride = undefined;
  old.props.onEdit({ ...taskB }); assert.equal(visibleModal().props.initial.id, 41); assert.equal(visibleModal().props.initial.rowVersion, 9);
  visibleModal().props.onClose(); await tick();
  const beforeUnknown = snapshot();
  for (const type of ["unregistered", "edit", "export", "createEvidencePackage"]) {
    assert.equal(old.props.canRunAction(type), false);
    old.props.onAction(type, taskA); await tick(); assert.deepEqual(snapshot(), beforeUnknown);
  }
  // Distinct Review invocation consumes the current ID via the unchanged service.
  const pending = deferred(); reviewTransport = () => pending.promise;
  old.props.onAction("review", taskB);
  const beforePending = snapshot(); old.props.onEdit(taskA);
  for (const action of ["review", "dismiss", "resolve", "createCoaching", "createIncident"]) old.props.onAction(action, taskA);
  button(oldTree, "Export Report").props.onClick(); await tick();
  assert.equal(calls.length, beforePending.mutations + 1); assert.equal(calls.at(-1).url, "/api/safety/events/41/review");
  assert.equal(resets, beforePending.resets); assert.equal(exports.length, beforePending.exports); assert.equal(visibleModal(), undefined);
  old = drawer(); oldTree = renderDrawer(old); await assertInert(old, oldTree);
  reviewTransport = undefined; pending.resolve({ id: 41 }); await tick(); await tick();
  directPermission = false; old = drawer(); oldTree = renderDrawer(old);
  assert.equal(button(oldTree, "Review").props.disabled, true);
  assert.equal(button(oldTree, "Export Report").props.disabled, false);
  const beforeDeniedWrite = snapshot(); old.props.onEdit(taskA); old.props.onAction("review", taskA); await tick();
  assert.deepEqual(snapshot(), beforeDeniedWrite); assert.equal(visibleModal(), undefined);
  directPermission = true; exportPermission = false; old = drawer(); oldTree = renderDrawer(old);
  assert.equal(button(oldTree, "Review").props.disabled, false);
  assert.equal(button(oldTree, "Export Report").props.disabled, true);
  button(oldTree, "Export Report").props.onClick(); await tick(); assert.deepEqual(snapshot(), beforeDeniedWrite);
  directPermission = exportPermission = false; old = drawer(); oldTree = renderDrawer(old);
  await assertInert(old, oldTree); directPermission = exportPermission = true;

  // Scalar projection explicitly excludes all unlisted/nested fields in associations.
  assert.deepEqual(Object.keys(drawer().props.detail.coachingTasks[0]).sort(), ["id","status","coachingType","dueDate","assignedToName","assignedByName","completedAt","driverAcknowledgedAt"].sort());
  assert.deepEqual(Object.keys(drawer().props.detail.auditTrail[0]).sort(), ["actionName","actorName","createdAt"].sort());
  assert.equal(calls.length, 2); assert.equal(exports.length, 1);
  const { Drawer } = module.exports;
  for (const kind of ["coaching","dashcam","incidents","evidence"]) {
    scope = "drawer"; stateCursor = refCursor = 0;
    const tree = Drawer({ kind, config: { eyebrow: kind, actions: [], sections: [] }, detail: { record: { id: 1, title: "Retained kind" } }, loading: false, canUpdate: true, canExport: true, actionPending: false, canRunAction: () => true, onClose() {}, onEdit() {}, onAction() {} });
    assert.match(renderToStaticMarkup(tree), /Retained kind/); assert.ok(button(tree, "Export Report"));
  }
  console.log("Safety detail shape PASS: unchanged actual flat service/hook, current-query safe projection, privacy/shape/identity boundaries, read retries/stale callbacks/shared-flight admission, and four other-kind defaults; two deliberate controlled Review POSTs and one safe export, zero automatic writes.");
} finally { cleanup?.(); for (const unsubscribe of subscriptions) unsubscribe(); client.clear(); globalThis.document = originalDocument; }
