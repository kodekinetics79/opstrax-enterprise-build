import assert from "node:assert/strict";
import { createRequire } from "node:module";
import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { renderToStaticMarkup } from "react-dom/server";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const require = createRequire(import.meta.url);
const esbuild = require("esbuild");
const { QueryClient, MutationObserver } = require("@tanstack/react-query");
const source = readFileSync(resolve(root, "src/pages/Batch4SafetyPage.tsx"), "utf8");
const singleFlightSource = readFileSync(resolve(root, "src/hooks/useSingleFlight.ts"), "utf8");
const focusSource = readFileSync(resolve(root, "src/hooks/useDialogFocus.ts"), "utf8");
const client = new QueryClient({ defaultOptions: { mutations: { retry: false } } });
const observers = [], calls = [], states = { page: [], modal: [] }, refs = { page: [], modal: [] };
let scope = "page", stateCursor = 0, refCursor = 0, mutationCursor = 0, resetAttempts = 0;
let transport, refresh, mountedKey, cleanup;
const keys = new Set();
const fakeDialog = { isConnected: true, hidden: false, querySelectorAll: () => [], querySelector: () => null, contains: () => false };
const originalDocument = globalThis.document;
globalThis.document = { activeElement: null, querySelectorAll: () => [fakeDialog], addEventListener: (_type, handler) => keys.add(handler), removeEventListener: (_type, handler) => keys.delete(handler) };
const taskA = { id: 41, taskNumber: "Synthetic A", driverId: 7, rowVersion: 3, status: "Assigned", coachingType: "Following Distance", title: "Synthetic task A", description: "Initial A" };
const taskB = { ...taskA, id: 99, taskNumber: "Synthetic B", title: "Synthetic task B", description: "Initial B" };
const records = new Map([[41, taskA], [99, taskB]]);
const query = { isLoading: false, isFetching: false, isError: false };
const hooks = {
  useState(initial) { const index = stateCursor++; const store = states[scope]; if (!(index in store)) store[index] = typeof initial === "function" ? initial() : initial; return [store[index], next => { store[index] = typeof next === "function" ? next(store[index]) : next; }]; },
  useMemo: callback => callback(), useCallback: callback => callback,
  useRef(initial) { const index = refCursor++; return refs[scope][index] ??= { current: initial }; },
  useEffect(effect) { cleanup?.(); cleanup = effect(); },
  useAuth: () => ({ session: { user: {}, company: {} } }),
  useHasPermission: () => () => true, useHasDirectPermission: () => () => true,
  useQueryClient: () => ({ getQueryState: key => client.getQueryState(key), invalidateQueries: async () => { if (refresh) await refresh(); } }),
  query(name, id) { const current = client.getQueryState(["coaching", "detail", id]); return { ...query, isSuccess: current?.status === "success", data: name.includes("Detail") ? current?.data : name.includes("Summary") ? {} : [taskA, taskB] }; },
  useMutation(options) {
    const index = mutationCursor++;
    const observer = observers[index] ??= new MutationObserver(client, options);
    observer.setOptions(options);
    return { ...observer.getCurrentResult(), mutateAsync: observer.mutate.bind(observer), reset: () => { resetAttempts++; observer.reset(); } };
  },
};
async function loadHook(text, importLine) {
  const built = await esbuild.transform(text.replace(importLine, "const {useCallback,useRef,useEffect}=__hooks;"), { loader: "ts", format: "cjs" });
  const module = { exports: {} };
  new Function("module", "exports", "__hooks", built.code)(module, module.exports, hooks);
  return module.exports;
}
hooks.useSingleFlight = (await loadHook(singleFlightSource, 'import { useCallback, useRef } from "react";')).useSingleFlight;
hooks.useDialogFocus = (await loadHook(focusSource, 'import { useEffect, useRef } from "react";')).useDialogFocus;
const inspected = source
  .replace(/import \{ FormEvent, ReactNode, useMemo, useState(?:, useRef)? \} from "react";/, 'import type { FormEvent, ReactNode } from "react"; const {useMemo,useState,useRef}=__hooks;')
  .replace('import { useMutation, useQueryClient } from "@tanstack/react-query";', 'const {useMutation,useQueryClient}=__hooks;')
  .replace(/import \{ ([^;]+) \} from "@\/hooks\/useBatch4";/, (_line, names) => names.split(",").map(name => `const ${name.trim()}=(id)=>__hooks.query(${JSON.stringify(name.trim())},id);`).join("\n"))
  .replace('import { useHasDirectPermission, useHasPermission } from "@/hooks/usePermission";', 'const {useHasDirectPermission,useHasPermission}=__hooks;')
  .replace('import { useSingleFlight } from "@/hooks/useSingleFlight";', 'const {useSingleFlight}=__hooks;')
  .replace('import { useDialogFocus } from "@/hooks/useDialogFocus";', 'const {useDialogFocus}=__hooks;')
  .replace('import { useAuth } from "@/hooks/useAuth";', 'const {useAuth}=__hooks;')
  .replace('import { CameraMetadataDialog, useCameraMetadataWorkflow } from "@/components/CameraMetadataDialog";', 'const CameraMetadataDialog=()=>null; const useCameraMetadataWorkflow=()=>({ notice:null, warning:false, refreshing:false, pending:false, editor:null, error:null, canEdit:false, detailReady:false, open(){}, exportCurrent(){}, canLeave(){return true}, refresh(){}, change(){}, close(){}, submit(){} });')
  .replace('import { coachingApi } from "@/services/coachingApi";', 'const coachingApi=__api;')
  .replace("function Drawer(", "export function Drawer(");
const api = {
  create: async payload => { calls.push({ type: "create", payload }); return transport(); },
  update: async (id, payload) => { calls.push({ type: "update", id, payload }); return transport(); },
};
const built = await esbuild.build({ stdin: { contents: inspected, resolveDir: root, loader: "tsx" }, bundle: true, platform: "node", format: "cjs", jsx: "automatic", write: false, nodePaths: process.env.NODE_PATH ? [process.env.NODE_PATH] : [], alias: { "@": resolve(root, "src") }, define: { "import.meta.env": "{}" }, logLevel: "silent" });
const module = { exports: {} };
new Function("require", "module", "exports", "__hooks", "__api", built.outputFiles[0].text)(require, module, module.exports, hooks, api);
const { Batch4SafetyPage } = module.exports;
function find(node, predicate) { if (!node || typeof node !== "object") return undefined; if (Array.isArray(node)) { for (const child of node) { const hit = find(child, predicate); if (hit) return hit; } return undefined; } return predicate(node) ? node : find(node.props?.children, predicate); }
function page() { scope = "page"; stateCursor = refCursor = mutationCursor = 0; return Batch4SafetyPage({ kind: "coaching" }); }
function drawer(tree = page()) { return find(tree, node => typeof node.props?.onEdit === "function"); }
function editor() { return find(page(), node => node.type?.name === "Modal"); }
function renderEditor() {
  const element = editor(); assert.ok(element);
  if (mountedKey !== element.key) { cleanup?.(); cleanup = undefined; states.modal = []; refs.modal = []; mountedKey = element.key; }
  scope = "modal"; stateCursor = refCursor = 0;
  return element.type(element.props);
}
function unmount() { cleanup?.(); cleanup = undefined; states.modal = []; refs.modal = []; mountedKey = undefined; }
function field(id, tree = renderEditor()) { return find(tree, node => node.props?.id === `safety-record-${id}`); }
function button(label, tree = renderEditor()) { return find(tree, node => node.type === "button" && (node.props["aria-label"] === label || node.props.children === label)); }
function createButton(tree = page()) { const header = find(tree, node => node.props?.createLabel === undefined && node.props?.actions); return find(header.props.actions, node => node.type === "button" && node.props.className === "btn-primary"); }
function form(tree = renderEditor()) { return find(tree, node => node.type === "form"); }
function open(record) {
  client.setQueryData(["coaching", "detail", record.id], { record });
  find(page(), node => typeof node.props?.onSelect === "function").props.onSelect(record);
  drawer().props.onEdit(record);
}
function type(id, value) { field(id).props.onChange({ target: { value } }); }
function submit(tree = renderEditor()) { form(tree).props.onSubmit({ preventDefault() {} }); }
function escape() { for (const handler of keys) handler({ key: "Escape", preventDefault() {}, stopPropagation() {} }); }
function tick() { return new Promise(resolve => setImmediate(resolve)); }
function deferred() { let resolve, reject; const promise = new Promise((yes, no) => { resolve = yes; reject = no; }); return { promise, resolve, reject }; }

try {
  open(taskA); type("description", "Captured A");
  const oldElement = editor(), oldTree = renderEditor(), oldDrawer = drawer(), oldCreate = createButton();
  const beforeReset = resetAttempts;
  const pending = deferred(); transport = () => pending.promise;
  submit(oldTree);
  // Same-turn callbacks: no pending rerender yet.
  button("Close record dialog", oldTree).props.onClick();
  assert.ok(editor(), "pending Close must not discard the submitted coaching editor");
  button("Cancel", oldTree).props.onClick(); escape();
  oldDrawer.props.onEdit(taskB); oldCreate.props.onClick();
  field("description", oldTree).props.onChange({ target: { value: "Late discarded edit" } });
  submit(oldTree); oldElement.props.onSave({ ...taskB });
  assert.equal(resetAttempts, beforeReset);
  assert.equal(editor().props.initial.id, 41);
  assert.equal(field("description").props.value, "Captured A");
  await tick(); assert.equal(calls.length, 1);
  assert.equal(calls[0].id, 41); assert.equal(calls[0].payload.rowVersion, 3); assert.equal("status" in calls[0].payload, false);
  assert.equal(field("driverId").props.disabled, true);
  assert.equal(field("description").props.disabled, true);
  assert.equal(button("Close record dialog").props.disabled, true);
  assert.equal(button("Cancel").props.disabled, true);
  assert.match(renderToStaticMarkup(renderEditor()), /Saving.*(?:draft|editing)/i);
  const heldRefresh = deferred(); refresh = () => heldRefresh.promise;
  pending.resolve({ id: 41, rowVersion: 4 }); await tick();
  const atHandoff = resetAttempts;
  oldDrawer.props.onEdit(taskB); oldCreate.props.onClick(); oldElement.props.onClose();
  assert.equal(editor(), undefined, "save ownership remains held through success/refresh handoff");
  assert.equal(resetAttempts, atHandoff);
  heldRefresh.resolve(); refresh = undefined; await tick(); await tick();
  assert.equal(editor(), undefined, "denied actions are never queued");
  unmount(); open(taskA); type("description", "New same-row draft");
  oldElement.props.onClose(); oldElement.props.onSave({ ...taskA });
  field("description", oldTree).props.onChange({ target: { value: "Stale same-row input" } });
  await tick(); assert.equal(calls.length, 1);
  assert.equal(field("description").props.value, "New same-row draft");
  const sameRowElement = editor(); sameRowElement.props.onClose(); unmount();
  open(taskA); assert.equal(field("description").props.value, "Initial A");
  sameRowElement.props.onClose(); sameRowElement.props.onSave({ ...taskA });
  assert.ok(editor()); assert.equal(calls.length, 1);

  for (const failure of [new Error("Synthetic timeout"), { response: { status: 409, data: { message: "Refresh current coaching record" } } }]) {
    type("description", "Draft retained after uncertainty");
    const current = deferred(); transport = () => current.promise; submit(); await tick();
    current.reject(failure); await tick(); await tick();
    assert.equal(field("description").props.value, "Draft retained after uncertainty");
    assert.equal(field("description").props.disabled, false);
    assert.match(renderToStaticMarkup(renderEditor()), /role="alert"/);
  }
  assert.equal(calls.length, 3); await tick(); assert.equal(calls.length, 3);
  editor().props.onClose(); unmount();
  createButton().props.onClick();
  for (const [key, value] of Object.entries({ driverId: "7", coachingType: "Following Distance", title: "Synthetic new draft", description: "New create draft" })) type(key, value);
  const oldCreateSession = editor(); transport = async () => ({ id: 101 }); submit(); await tick(); await tick();
  assert.equal(calls.length, 4); assert.equal(calls[3].type, "create"); assert.equal(editor(), undefined);
  unmount(); open(taskB); type("description", "B remains B");
  oldCreateSession.props.onClose(); oldCreateSession.props.onSave(calls[3].payload);
  assert.equal(field("description").props.value, "B remains B"); assert.equal(editor().props.initial.id, 99);

  // Controlled supersession seam, not a reachable pending-close user journey:
  // bypass ONLY the owner's pending admission flag, then use actual Edit. This
  // proves session/attempt checks independently of the normal pending lock.
  for (const outcome of ["resolve", "reject"]) {
    await tick(); // A new deliberate editor session, after the prior detail-open event.
    editor().props.onClose(); unmount(); open(taskA); type("description", `Owned A ${outcome}`);
    const delayed = deferred(); transport = () => delayed.promise; submit(); await tick();
    const owner = refs.page.find(ref => ref.current && typeof ref.current === "object" && "session" in ref.current && "attempt" in ref.current && !("eventId" in ref.current))?.current;
    assert.ok(owner, "coaching editor lifetime owner exists");
    owner.pending = false;
    open(outcome === "resolve" ? taskB : taskA); type("description", `New draft survives old ${outcome}`);
    if (outcome === "resolve") delayed.resolve({ id: 41 }); else delayed.reject(new Error("Old-session failure must stay old"));
    await tick(); await tick();
    assert.equal(field("description").props.value, `New draft survives old ${outcome}`);
    assert.doesNotMatch(renderToStaticMarkup(renderEditor()), /Old-session failure/);
    assert.equal(field("description").props.disabled, false);
  }
  assert.equal(calls.length, 6); await tick(); assert.equal(calls.length, 6, "six deliberate operations and zero automatic/queued writes");
  const RecordModal = editor().type;
  transport = async () => ({ id: 41, rowVersion: 4 }); submit(); await tick(); await tick();
  assert.equal(calls.length, 7); assert.equal(editor(), undefined, "new session remains usable after old settlement");
  // Other generic-editor consumers opt out. Camera is deliberately excluded:
  // the camera-first base routes it through its separately accepted dedicated
  // dialog/workflow rather than this generic Modal.
  assert.match(source, /camera\.editor \? <CameraMetadataDialog/);
  assert.match(source, /editing && kind !== "dashcam"/);
  for (const kind of ["safety", "incidents", "evidence"]) {
    unmount();
    let sent, closed = 0;
    const initial = kind === "incidents"
        ? { incidentType: "Collision", severity: "High", occurredAt: "2026-09-05T10:00:00Z", locationDescription: "Synthetic site", aiSummary: "Synthetic report", driverId: "7", title: "Original incident title" }
        : { id: 82, title: `Original ${kind} title` };
    const props = { kind, title: "Synthetic default form", fields: [["title", "Title"]], initial, saving: true, onClose: () => { closed++; }, onSave: payload => { sent = payload; } };
    const renderDefault = () => { scope = "modal"; stateCursor = refCursor = 0; return RecordModal(props); };
    let tree = renderDefault();
    assert.equal(button("Close record dialog", tree).props.disabled, false);
    assert.equal(button("Cancel", tree).props.disabled, false);
    const input = field("title", tree); assert.equal(input.props.disabled, false);
    input.props.onChange({ target: { value: `Edited ${kind} title` } }); tree = renderDefault();
    assert.equal(field("title", tree).props.value, `Edited ${kind} title`);
    form(tree).props.onSubmit({ preventDefault() {} });
    button("Close record dialog", tree).props.onClick(); assert.equal(closed, 1);
    if (kind === "incidents") assert.deepEqual(sent, { ...initial, title: "Edited incidents title", occurredAt: "2026-09-05T10:00:00.000Z" });
    else assert.deepEqual(sent, { ...initial, title: `Edited ${kind} title` });
    assert.doesNotMatch(renderToStaticMarkup(tree), /Saving this draft/);
  }
  assert.equal(calls.length, 7, "other-kind callback checks do not dispatch a service request");
  console.log("Coaching editor session: actual page/Modal/single-flight/MutationObserver and Escape-handler scenarios plus 3 unchanged generic-editor payload/default contracts and the locked dedicated camera path passed; 7 deliberate controlled service calls, zero automatic writes; no browser/HTTP/persistence claim.");
} finally { cleanup?.(); client.clear(); globalThis.document = originalDocument; }
