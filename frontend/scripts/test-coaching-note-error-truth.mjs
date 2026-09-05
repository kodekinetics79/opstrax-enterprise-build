import assert from "node:assert/strict";
import { createRequire } from "node:module";
import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { renderToStaticMarkup } from "react-dom/server";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const require = createRequire(import.meta.url);
const esbuild = require("esbuild");
const { QueryClient } = require("@tanstack/react-query");
const client = new QueryClient();
const source = readFileSync(resolve(root, "src/pages/Batch4SafetyPage.tsx"), "utf8");
const singleFlightSource = readFileSync(resolve(root, "src/hooks/useSingleFlight.ts"), "utf8");
let scope = "page", stateCursor = 0, mutationCursor = 0, refCursor = 0;
const states = { page: [], modal: [] }, refs = [];
const mutations = [];
const requests = [];
const assignments = [];
let transport;
let assignTransport;
let refreshError;
const query = { isLoading: false, isFetching: false, isError: false };
// Assign is exercised from an actually assignable, successfully selected task.
const row = { id: 41, taskNumber: "Synthetic coaching", status: "Open", rowVersion: 3 };
const hooks = {
  useState(initial) {
    const index = stateCursor++;
    if (!(index in states[scope])) states[scope][index] = initial;
    const store = states[scope];
    return [store[index], next => { store[index] = typeof next === "function" ? next(store[index]) : next; }];
  },
  useMemo: callback => callback(),
  useRef(initial) { const index = refCursor++; return refs[index] ??= { current: initial }; },
  useCallback: callback => callback,
  useAuth: () => ({ session: { user: {}, company: {} } }),
  useHasPermission: () => () => true,
  useHasDirectPermission: () => () => true,
  useDialogFocus: () => null,
  useQueryClient: () => ({ getQueryState: key => client.getQueryState(key), invalidateQueries: async () => { if (refreshError) throw refreshError; } }),
  query(name, id) {
    const current = client.getQueryState(["coaching", "detail", id]);
    return { ...query, isSuccess: current?.status === "success", data: name.includes("Detail") ? current?.data : name.includes("Summary") ? {} : [row] };
  },
  useMutation(options) {
    const index = mutationCursor++;
    const mutation = mutations[index] ??= {
      error: null, isPending: false, resetAttempts: 0,
      reset() { this.resetAttempts++; assert.equal(this.isPending, false, "reset must not clear an in-flight outcome"); this.error = null; },
      async mutateAsync(variables) {
        this.isPending = true; this.error = null;
        try { const result = await this.options.mutationFn(variables); await this.options.onSuccess?.(result, variables); return result; }
        catch (error) { this.error = error; throw error; }
        finally { this.isPending = false; }
      },
    };
    mutation.options = options;
    // React Query exposes a result snapshot per render. Older callbacks retain
    // isPending=false until a rerender, even after mutateAsync has started.
    return { error: mutation.error, isPending: mutation.isPending, reset: mutation.reset.bind(mutation), mutateAsync: mutation.mutateAsync.bind(mutation) };
  },
};
const flightBuilt = await esbuild.transform(singleFlightSource.replace('import { useCallback, useRef } from "react";', 'const {useCallback,useRef}=__hooks;'), { loader: "ts", format: "cjs" });
const flightModule = { exports: {} };
new Function("module", "exports", "__hooks", flightBuilt.code)(flightModule, flightModule.exports, hooks);
hooks.useSingleFlight = flightModule.exports.useSingleFlight;
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
  .replace("function Drawer(", "export function Drawer(")
  .replace("function CoachingNoteModal(", "export function CoachingNoteModal(");
const api = {
  summary: async () => ({}), tasks: async () => [row], detail: async () => ({ record: row }),
  addNote: async (id, payload) => { requests.push({ id, payload }); return transport(); },
  assign: async (id, payload) => { assignments.push({ id, payload }); return assignTransport(); },
};
const built = await esbuild.build({ stdin: { contents: inspected, resolveDir: root, loader: "tsx" }, bundle: true, platform: "node", format: "cjs", jsx: "automatic", write: false,
  nodePaths: process.env.NODE_PATH ? [process.env.NODE_PATH] : [],
  alias: { "@": resolve(root, "src") }, define: { "import.meta.env": "{}" }, logLevel: "silent" });
const module = { exports: {} };
new Function("require", "module", "exports", "__hooks", "__api", built.outputFiles[0].text)(require, module, module.exports, hooks, api);
const { Batch4SafetyPage, CoachingNoteModal } = module.exports;
function find(node, predicate) {
  if (!node || typeof node !== "object") return undefined;
  if (Array.isArray(node)) { for (const child of node) { const hit = find(child, predicate); if (hit) return hit; } return undefined; }
  return predicate(node) ? node : find(node.props?.children, predicate);
}
function page() { scope = "page"; stateCursor = mutationCursor = refCursor = 0; return Batch4SafetyPage({ kind: "coaching" }); }
function noteElement(tree = page()) { return find(tree, element => element.type === CoachingNoteModal); }
function drawer(tree = page()) { return find(tree, element => element.props?.kind === "coaching" && typeof element.props?.onAction === "function"); }
function modal(element = noteElement()) { assert.ok(element); scope = "modal"; stateCursor = 0; return CoachingNoteModal(element.props); }
function control(tree, type) { return find(tree, element => element.type === type); }
function tick() { return new Promise(resolve => setImmediate(resolve)); }

for (const error of [new Error("Synthetic timeout"), { response: { status: 400, data: { message: "PRIVATE SERVER TRACE" } } }, { response: { status: 500, data: { message: "PRIVATE SERVER TRACE" } } }]) {
  scope = "modal"; stateCursor = 0;
  const initialFailure = renderToStaticMarkup(CoachingNoteModal({ saving: false, error, onClose() {}, onSubmit() {} }));
  assert.match(initialFailure, /role="alert"[\s\S]*Note save could not be confirmed/, "actual note modal must expose an unconfirmed write result");
  assert.doesNotMatch(initialFailure, /PRIVATE SERVER TRACE|not saved|no note was created|save failed/i);
}
states.modal = [];
client.setQueryData(["coaching", "detail", row.id], { record: row });
find(page(), element => typeof element.props?.onSelect === "function").props.onSelect(row);
page();
const action = mutations[1];
for (const outcome of ["reject", "resolve"]) {
  // Exercise both a closed dialog and an existing dialog during another shared
  // action. The exact old callbacks retain isPending=false before rerender.
  if (outcome === "resolve") { drawer().props.onAction("addNote", row); await tick(); }
  const oldDrawer = drawer();
  const oldNote = noteElement();
  const resetsBeforeAssign = action.resetAttempts;
  let settleAssign;
  assignTransport = () => new Promise((resolve, reject) => { settleAssign = outcome === "resolve" ? resolve : () => reject(new Error("Synthetic assign rejection")); });
  oldDrawer.props.onAction("assign", row);
  oldDrawer.props.onAction("addNote", { ...row, id: 99 });
  oldNote?.props.onClose();
  assert.equal(action.resetAttempts, resetsBeforeAssign, "note callbacks cannot attempt to reset any shared action flight before rerender, even if the flight catches an assertion");
  assert.equal(Boolean(noteElement()), Boolean(oldNote), "denied callbacks neither open nor close the note dialog");
  settleAssign({ id: row.id });
  await tick(); await tick();
  assert.equal(Boolean(noteElement()), Boolean(oldNote), "denied UI operations are not queued for later settlement");
  if (oldNote) { noteElement().props.onClose(); await tick(); states.modal = []; }
  drawer().props.onAction("addNote", row);
  await tick();
  assert.ok(noteElement(), "shared-flight settlement permits a new deliberate note open");
  assert.equal(action.error, null);
  noteElement().props.onClose();
  await tick();
  assert.equal(noteElement(), undefined);
  states.modal = [];
}
assert.equal(assignments.length, 2);
assert.deepEqual(assignments, [{ id: 41, payload: { rowVersion: 3 } }, { id: 41, payload: { rowVersion: 3 } }]);
assert.equal(requests.length, 0, "shared action/guard scenarios do not create notes");
action.error = new Error("Unrelated previous action");
drawer().props.onAction("addNote", row);
await tick();
assert.equal(action.error, null, "opening a note must clear stale unrelated errors");
let element = noteElement();
let tree = modal(element);
control(tree, "textarea").props.onChange({ target: { value: "  Synthetic driver safety note  " } });
tree = modal();
assert.equal(control(tree, "textarea").props.value, "  Synthetic driver safety note  ");

let rejectTransport;
transport = () => new Promise((_resolve, reject) => { rejectTransport = reject; });
const beforeSubmitElement = noteElement();
const beforeSubmitDrawer = drawer();
const beforeSubmitResets = action.resetAttempts;
control(tree, "form").props.onSubmit({ preventDefault() {} });
assert.equal(requests.length, 1);
assert.deepEqual(requests[0], { id: 41, payload: { noteText: "Synthetic driver safety note" } });
// No rerender yet: these callbacks still hold the old isPending=false snapshot.
beforeSubmitElement.props.onClose();
beforeSubmitDrawer.props.onAction("addNote", { ...row, id: 99 });
assert.equal(action.resetAttempts, beforeSubmitResets);
assert.ok(noteElement(), "synchronous pending guard preserves modal before the pending render");
// Even a stale callback in the same event turn is governed by the shipped hook.
control(tree, "form").props.onSubmit({ preventDefault() {} });
assert.equal(requests.length, 1);
element = noteElement(); tree = modal(element);
assert.equal(element.props.saving, true);
assert.equal(find(tree, node => node.type === "button" && node.props.type === "submit").props.disabled, true);
assert.equal(control(tree, "textarea").props.disabled, true);
assert.equal(find(tree, node => node.type === "button" && node.props["aria-label"] === "Close coaching note dialog").props.disabled, true);
const resetsWhilePending = action.resetAttempts;
element.props.onClose();
await tick();
drawer().props.onAction("addNote", { ...row, id: 99 });
assert.equal(action.resetAttempts, resetsWhilePending);
assert.ok(noteElement(), "pending close must not imply cancellation or discard the modal");

rejectTransport(new Error("Network response lost; PRIVATE SERVER TRACE"));
await tick(); await tick();
element = noteElement(); tree = modal(element);
const html = renderToStaticMarkup(tree);
assert.match(html, /role="alert"/);
assert.match(html, /Note save could not be confirmed/);
assert.match(html, /existing notes/i);
assert.match(html, /before.*(?:submitting|resubmitting)/i);
assert.doesNotMatch(html, /PRIVATE SERVER TRACE|note was not saved|no note was created|save failed/i);
assert.equal(control(tree, "textarea").props.value, "  Synthetic driver safety note  ");
assert.equal(requests.length, 1, "rerender/error settlement must never automatically replay a note write");
await tick(); assert.equal(requests.length, 1);

element.props.onClose();
assert.equal(action.error, null);
assert.equal(noteElement(), undefined);
await tick(); // The next deliberate user action is a separate event turn.
states.modal = []; // Model React unmount, then the actual fresh modal initializer.
drawer().props.onAction("addNote", row);
await tick();
tree = modal();
assert.equal(control(tree, "textarea").props.value, "");
assert.doesNotMatch(renderToStaticMarkup(tree), /Note save could not be confirmed/);
assert.equal(requests.length, 1, "cancel/reopen must not replay");
control(tree, "form").props.onSubmit({ preventDefault() {} });
assert.equal(requests.length, 1, "empty draft cannot submit");

control(tree, "textarea").props.onChange({ target: { value: "Deliberate new note after inspection" } });
transport = async () => ({ id: 71 });
control(modal(), "form").props.onSubmit({ preventDefault() {} });
await tick(); await tick();
assert.equal(requests.length, 2);
assert.deepEqual(requests[1], { id: 41, payload: { noteText: "Deliberate new note after inspection" } });
assert.equal(noteElement(), undefined, "confirmed success closes the note modal");
states.modal = [];
drawer().props.onAction("addNote", row);
await tick();
tree = modal();
assert.equal(control(tree, "textarea").props.value, "", "successful settlement releases the guard for a fresh modal");
control(tree, "textarea").props.onChange({ target: { value: "Synthetic accepted response before refresh rejection" } });
refreshError = new Error("PRIVATE REFRESH TRACE");
control(modal(), "form").props.onSubmit({ preventDefault() {} });
await tick(); await tick();
assert.equal(requests.length, 3);
tree = modal();
assert.match(renderToStaticMarkup(tree), /Note save could not be confirmed/);
assert.doesNotMatch(renderToStaticMarkup(tree), /PRIVATE REFRESH TRACE|not saved|save failed/i);
assert.equal(control(tree, "textarea").props.value, "Synthetic accepted response before refresh rejection");
await tick(); assert.equal(requests.length, 3, "a display-refresh rejection must not automatically replay a successful API response");
noteElement().props.onClose();
assert.equal(noteElement(), undefined, "refresh rejection also releases the pending guard");
console.log("Coaching note error truth: actual modal/page callback scenario passed (draft, note/shared-action pre-render races, pending single-flight, neutral transport/refresh uncertainty, safe cancel/reopen, manual success); no browser or persistence claim.");
client.clear();
