import assert from "node:assert/strict";
import { createRequire } from "node:module";
import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { renderToStaticMarkup } from "react-dom/server";

// Actual query/mutation observers, detail-hook options, page/Drawer callbacks,
// and single-flight/focus hooks. Manual React frames; no HTTP, DB or browser.
const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const require = createRequire(import.meta.url);
const esbuild = require("esbuild");
const { QueryClient, QueryObserver, MutationObserver } = require("@tanstack/react-query");
const client = new QueryClient({ defaultOptions: { queries: { retry: 1, refetchOnWindowFocus: false, staleTime: 30_000 }, mutations: { retry: false } } });
const states = { page: [], drawer: [] }, refs = { page: [], drawer: [] }, mutations = [], queries = new Map(), subscriptions = [];
let scope = "page", stateCursor = 0, refCursor = 0, mutationCursor = 0, resets = 0, directPermission = true, exportPermission = true, cleanup, snapshotOverride, assignTransport;
const calls = [], exports = [], reads = [], failures = new Map(), transports = new Map();
const taskA = { id: 41, taskNumber: "Synthetic A", driverId: 7, rowVersion: 3, status: "Open", title: "Synthetic task A", description: "Synthetic private description", coachingType: "Following Distance", driverAcknowledged: true, acknowledgedAt: "2026-09-05T12:00:00Z" };
const taskB = { ...taskA, id: 99, taskNumber: "Synthetic B" };
const detailFor = record => ({ record, notes: [{ noteText: "Synthetic private coaching note" }], auditTrail: [{ actionName: "Synthetic private audit" }] });
const details = new Map([[41, detailFor(taskA)], [99, detailFor(taskB)]]);
const originalDocument = globalThis.document;
const fakeDialog = { isConnected: true, hidden: false, querySelectorAll: () => [], querySelector: () => null, contains: () => false };
globalThis.document = { activeElement: null, querySelectorAll: () => [fakeDialog], addEventListener() {}, removeEventListener() {} };
const api = {
  detail: async id => { reads.push(id); if (transports.has(id)) return transports.get(id)(); if (failures.has(id)) throw failures.get(id); return details.get(id); },
  assign: async (id, payload) => { calls.push({ type: "assign", id, payload }); return assignTransport ? assignTransport() : { id }; },
  complete: async (id, payload) => { calls.push({ type: "complete", id, payload }); return { id }; },
  addNote: async (id, payload) => { calls.push({ type: "addNote", id, payload }); return { id }; },
};
const hookSource = readFileSync(resolve(root, "src/hooks/useBatch4.ts"), "utf8");
const hookBuilt = await esbuild.transform(hookSource.replace(/^import .*;$/gm, ""), { loader: "ts", format: "cjs" });
const hookModule = { exports: {} };
new Function("module", "exports", "useQuery", "coachingApi", hookBuilt.code)(hookModule, hookModule.exports, options => options, api);
function observer(id) {
  if (!queries.has(id)) {
    const query = new QueryObserver(client, hookModule.exports.useCoachingTaskDetail(id));
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
  .replace('import { coachingApi } from "@/services/coachingApi";', 'const coachingApi=__api;')
  .replace("function Drawer(", "export function Drawer(");
const built = await esbuild.build({ stdin: { contents: inspected, resolveDir: root, loader: "tsx" }, bundle: true, platform: "node", format: "cjs", jsx: "automatic", write: false, nodePaths: process.env.NODE_PATH ? [process.env.NODE_PATH] : [], alias: { "@": resolve(root, "src") }, define: { "import.meta.env": "{}" }, logLevel: "silent" });
const module = { exports: {} }; new Function("require", "module", "exports", "__hooks", "__api", "__exports", built.outputFiles[0].text)(require, module, module.exports, hooks, api, exports);
const { Batch4SafetyPage } = module.exports;
function find(node, predicate) { if (!node || typeof node !== "object") return; if (Array.isArray(node)) { for (const child of node) { const hit = find(child, predicate); if (hit) return hit; } return; } return predicate(node) ? node : find(node.props?.children, predicate); }
function page() { scope = "page"; stateCursor = refCursor = mutationCursor = 0; return Batch4SafetyPage({ kind: "coaching" }); }
function drawer(tree = page()) { return find(tree, node => typeof node.props?.onEdit === "function"); }
function renderDrawer(element = drawer()) { scope = "drawer"; stateCursor = refCursor = 0; return element.type(element.props); }
function button(tree, label) { return find(tree, node => node.type === "button" && (node.props["aria-label"] === label || node.props.children === label || (Array.isArray(node.props.children) && node.props.children.includes(` ${label}`)))); }
function select(row, tree = page()) { find(tree, node => typeof node.props?.onSelect === "function").props.onSelect(row); }
function loaded(row) { details.set(row.id, detailFor(row)); client.setQueryData(["coaching", "detail", row.id], detailFor(row)); select(row); return drawer(); }
function deferred() { let resolve, reject; const promise = new Promise((yes, no) => { resolve = yes; reject = no; }); return { promise, resolve, reject }; }
function tick() { return new Promise(resolve => setImmediate(resolve)); }
function visibleModal(tree = page()) { return find(tree, node => ["Modal", "CoachingNoteModal", "CoachingCompleteModal"].includes(node.type?.name)); }
function snapshot() { return { mutations: calls.length, resets, exports: exports.length }; }
async function assertInert(oldElement, oldTree) {
  const before = snapshot();
  oldElement.props.onEdit(taskA);
  await tick();
  // Release admission between attempts: one denied callback must not mask a
  // missing guard on another via the shared single-flight flag.
  for (const action of ["assign", "addNote", "complete"]) { oldElement.props.onAction(action, taskA); await tick(); }
  button(oldTree, "Export Report").props.onClick();
  await tick();
  assert.deepEqual(snapshot(), before, "unconfirmed/stale detail callbacks have zero effects");
  assert.equal(visibleModal(), undefined);
}
function assertUnavailable(expected) {
  const tree = renderDrawer(); const html = renderToStaticMarkup(tree);
  assert.match(html, expected);
  assert.doesNotMatch(html, /Synthetic private|Synthetic A|Synthetic B/);
  assert.equal(button(tree, "Assign"), undefined);
  assert.equal(button(tree, "Export Report"), undefined);
  assert.ok(button(tree, "Close detail"));
  return tree;
}

try {
  // Baseline RED fails here: actual refetch error retains cached private detail.
  for (const failure of [Object.assign(new Error("Synthetic secret 404"), { response: { status: 404 } }), Object.assign(new Error("Synthetic secret 403"), { response: { status: 403 } }), new Error("Synthetic secret transport")]) {
    failures.delete(41); const old = loaded(taskA), oldTree = renderDrawer(old);
    failures.set(41, failure); const beforeReads = reads.length;
    await observer(41).refetch();
    assert.equal(reads.length - beforeReads, 2);
    assert.equal(observer(41).getCurrentResult().isRefetchError, true);
    assert.equal(observer(41).getCurrentResult().data.record.id, 41);
    // Deliberately invoke old callbacks BEFORE rendering the error snapshot.
    await assertInert(old, oldTree);
    const unavailable = assertUnavailable(/role="alert"/);
    assert.doesNotMatch(renderToStaticMarkup(unavailable), /Synthetic secret/);
    let retry = button(unavailable, "Retry detail"); assert.ok(retry);
    const beforeRetryFailure = snapshot(); const readCount = reads.length;
    retry.props.onClick(); await tick();
    // Await the actual observer's configured retry delay and terminal read.
    for (let wait = 0; observer(41).getCurrentResult().isFetching && wait < 200; wait++) await new Promise(resolve => setTimeout(resolve, 25));
    assert.equal(observer(41).getCurrentResult().isFetching, false);
    assert.equal(reads.length - readCount, 2);
    assert.deepEqual(snapshot(), beforeRetryFailure);
    retry = button(assertUnavailable(/role="alert"/), "Retry detail");
    const before = snapshot(); failures.delete(41); retry.props.onClick(); retry.props.onClick();
    await assertInert(old, oldTree); await tick(); await tick();
    assert.equal(calls.length, before.mutations); assert.equal(exports.length, before.exports);
    assert.match(renderToStaticMarkup(renderDrawer()), /Synthetic private coaching note/);
  }
  let old = loaded(taskA), oldTree = renderDrawer(old);
  const pending = deferred(); transports.set(41, () => pending.promise);
  const refetch = observer(41).refetch();
  await assertInert(old, oldTree); assertUnavailable(/role="status"/);
  transports.delete(41); pending.resolve(detailFor(taskA)); await refetch;

  // Same-turn select, Close, Retry and same-row reopen revoke old callbacks.
  old = drawer(); oldTree = renderDrawer(old); const beforeSelect = page();
  client.setQueryData(["coaching", "detail", 99], detailFor(taskB)); select(taskB, beforeSelect);
  await assertInert(old, oldTree); assert.equal(drawer().props.detail.record.id, 99);
  old = drawer(); oldTree = renderDrawer(old); old.props.onClose();
  await assertInert(old, oldTree); assert.equal(renderDrawer(), null);
  loaded(taskB); await assertInert(old, oldTree);

  // A cold selected task keeps usable progress and Close, never a blank drawer.
  const cold = deferred(); transports.set(77, () => cold.promise); select({ id: 77 });
  assertUnavailable(/role="status"/);
  const superseded = drawer(); select(taskB);
  cold.resolve(detailFor({ ...taskA, id: 77 })); await tick(); await tick();
  assert.equal(drawer().props.detail.record.id, 99); superseded.props.onClose();
  assert.equal(drawer().props.detail.record.id, 99, "old Close must not close the new selection");

  for (const invalid of [{}, { record: null }, { record: [] }, { record: "Malformed" }, { record: { ...taskA, id: 99 } }]) {
    old = loaded(taskA); oldTree = renderDrawer(old);
    client.setQueryData(["coaching", "detail", 41], invalid);
    await assertInert(old, oldTree); assertUnavailable(/role="alert"/);
  }
  old = loaded(taskA); oldTree = renderDrawer(old); select({});
  await assertInert(old, oldTree); assertUnavailable(/role="alert"/);
  // Retry revokes old generation synchronously even before the next React frame.
  old = loaded(taskA); oldTree = renderDrawer(old);
  failures.set(41, new Error("Synthetic read failure")); await observer(41).refetch();
  const retryTree = assertUnavailable(/role="alert"/), retry = button(retryTree, "Retry detail");
  const retryPending = deferred(); transports.set(41, () => retryPending.promise); failures.delete(41);
  retry.props.onClick(); retry.props.onClick();
  await assertInert(old, oldTree); assertUnavailable(/role="status"/);
  select(taskB); const readsBeforeStaleRetry = reads.length; retry.props.onClick();
  assert.equal(reads.length, readsBeforeStaleRetry);
  transports.delete(41); retryPending.resolve(detailFor(taskA)); await tick(); await tick();
  assert.equal(drawer().props.detail.record.id, 99);

  // A still-current selection callback must consume the newly trusted row/version.
  old = loaded(taskA); oldTree = renderDrawer(old);
  const current = { ...taskA, rowVersion: 9, title: "Current trusted title" };
  details.set(41, detailFor(current)); client.setQueryData(["coaching", "detail", 41], detailFor(current));
  // Defensive seam: React's selected-ID hook snapshot and live cache differ.
  // Render context from the same complete live detail object used by admission.
  snapshotOverride = { ...observer(41).getCurrentResult(), data: { record: taskB, notes: [{ noteText: "Wrong snapshot narrative" }] } };
  assert.equal(drawer().props.detail.record.id, 41);
  assert.doesNotMatch(renderToStaticMarkup(renderDrawer()), /Wrong snapshot narrative|Synthetic B/);
  snapshotOverride = undefined;
  old.props.onEdit(taskA); assert.equal(visibleModal().props.initial.rowVersion, 9);
  visibleModal().props.onClose(); await tick();
  old.props.onAction("addNote", taskA); await tick();
  assert.ok(visibleModal()); visibleModal().props.onClose(); await tick();
  old.props.onAction("assign", taskA); await tick(); await tick();
  assert.deepEqual(calls, [{ type: "assign", id: 41, payload: { rowVersion: 9 } }]);
  old = drawer(); oldTree = renderDrawer(old); button(oldTree, "Export Report").props.onClick();
  assert.equal(exports.length, 1); assert.equal(exports[0][1][0].rowVersion, 9);
  await tick();

  // Same-turn shared Assign flight denies detail Edit/export/opening another
  // action, even while the old mutation result snapshot still says idle.
  const heldAssign = deferred(); assignTransport = () => heldAssign.promise;
  old = drawer(); oldTree = renderDrawer(old); old.props.onAction("assign", current);
  const pendingCounts = snapshot(); old.props.onEdit(current);
  old.props.onAction("addNote", current); old.props.onAction("complete", current);
  button(oldTree, "Export Report").props.onClick();
  await tick(); assert.equal(calls.length, pendingCounts.mutations + 1);
  assert.equal(resets, pendingCounts.resets); assert.equal(exports.length, pendingCounts.exports); assert.equal(visibleModal(), undefined);
  // Rendered-pending callbacks are denied too; keep the shared flight held.
  old = drawer(); oldTree = renderDrawer(old); const atPending = snapshot();
  await assertInert(old, oldTree); assert.deepEqual(snapshot(), atPending);
  assignTransport = undefined; heldAssign.resolve({ id: 41 }); await tick(); await tick();

  directPermission = false; exportPermission = false; old = drawer(); oldTree = renderDrawer(old);
  await assertInert(old, oldTree);
  directPermission = exportPermission = true;
  loaded({ ...taskA, status: "Completed" }); old = drawer();
  const beforeTerminal = snapshot(); old.props.onEdit(taskA); old.props.onAction("assign", taskA); old.props.onAction("complete", taskA); await tick();
  assert.deepEqual(snapshot(), beforeTerminal); assert.equal(visibleModal(), undefined);
  loaded({ ...taskA, status: "Driver Acknowledged", driverAcknowledged: false }); old = drawer(); old.props.onAction("complete", taskA); assert.equal(visibleModal(), undefined);
  loaded({ ...taskA, status: "Driver Acknowledged" }); old = drawer(); old.props.onAction("complete", taskA); assert.equal(visibleModal()?.type.name, "CoachingCompleteModal");
  visibleModal().props.onClose();

  // Shared Drawer defaults remain unchanged for the four unscoped consumers.
  const { Drawer } = module.exports;
  for (const kind of ["safety", "dashcam", "incidents", "evidence"]) {
    scope = "drawer"; stateCursor = refCursor = 0;
    const tree = Drawer({ kind, config: { eyebrow: kind, actions: [], sections: [] }, detail: detailFor(taskA), loading: false, canUpdate: true, canExport: true, actionPending: false, canOpenExistingCoaching: false, canRunAction: () => true, actionUnavailableReason: () => undefined, onClose() {}, onEdit() {}, onAction() {} });
    assert.match(renderToStaticMarkup(tree), /Synthetic A/);
    assert.match(renderToStaticMarkup(tree), /Synthetic private audit/);
    assert.ok(button(tree, "Export Report"));
  }
  console.log("Coaching detail read truth PASS: actual hook/QueryObserver errors, pending, selection/retry/stale callbacks, shared-flight protection, current-row admission, permissions and four other-kind defaults; two deliberate Assigns and one deliberate export, zero automatic writes.");
} finally { cleanup?.(); for (const unsubscribe of subscriptions) unsubscribe(); client.clear(); globalThis.document = originalDocument; }
