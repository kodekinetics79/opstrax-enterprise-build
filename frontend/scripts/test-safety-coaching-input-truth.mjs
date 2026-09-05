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
const { QueryClient, QueryObserver, MutationObserver, onlineManager } = require("@tanstack/react-query");
const client = new QueryClient({ defaultOptions: { queries: { retry: 1, refetchOnWindowFocus: false, staleTime: 30_000 }, mutations: { retry: false } } });
client.mount();
const states = { page: [], drawer: [], modal: [] }, refs = { page: [], drawer: [], modal: [] }, mutations = [], queries = new Map(), surfaces = new Map(), subscriptions = [];
let mutationPromise, sessionOverride;
let scope = "page", stateCursor = 0, refCursor = 0, mutationCursor = 0, resets = 0, directPermission = true, exportPermission = true, cleanup, snapshotOverride, reviewTransport, keydown, rowsError = false, summaryError = false;
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
let dialogNodes = [fakeDialog];
globalThis.document = { activeElement: null, querySelectorAll: () => dialogNodes, addEventListener(_type, handler) { keydown = handler; }, removeEventListener() { keydown = undefined; } };
const apiClient = {
  get: async url => { const id = url.split("/").at(-1); reads.push(id); if (transports.has(id)) return { data: { success: true, data: await transports.get(id)() } }; if (failures.has(id)) throw failures.get(id); return { data: { success: true, data: details.get(id) } }; },
  post: async (url, payload) => { calls.push({ url, payload }); return reviewTransport ? await reviewTransport() : { status: 201, data: { success: true, data: { id: 120, rowVersion: 0 } } }; },
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
  useAuth: () => ({ session: sessionOverride ?? { user: {}, company: {} } }),
  useHasPermission: () => () => exportPermission,
  useHasDirectPermission: () => () => directPermission,
  useQueryClient: () => client,
  query(name, id) { return name.includes("Detail") ? snapshotOverride ?? observer(id).getCurrentResult() : surfaces.get(name)?.getCurrentResult() ?? { data: name.includes("Summary") ? {} : [taskA, taskB], isLoading: false, isFetching: false, isError: name.includes("Summary") ? summaryError : rowsError }; },
  useMutation(options) { const index = mutationCursor++; const mutation = mutations[index] ??= new MutationObserver(client, options); mutation.setOptions(options); return { ...mutation.getCurrentResult(), mutateAsync: args => { const promise = mutation.mutate(args); if (index === 1) mutationPromise = promise; return promise; }, reset: () => { resets++; mutation.reset(); } }; },
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


function inputModal(tree = page()) { return find(tree, node => node.type?.name === "SafetyCoachingInputDialog"); }
function renderInput(element = inputModal()) { scope = "modal"; stateCursor = refCursor = 0; return element?.type(element.props); }
function notice(tree = page()) { return find(tree, node => node.type?.name === "SafetyCoachingReceiptNotice"); }
function noticeTree(element = notice()) { return element?.type(element.props); }
function owner() { return refs.page.find(ref => ref.current && "eventId" in Object(ref.current) && "open" in ref.current)?.current; }
function draft(value) { inputModal().props.onChange(value); return inputModal(); }
function submit(element = inputModal()) { const form = find(renderInput(element), node => node.type === "form"); form.props.onSubmit({ preventDefault() {} }); }
async function settled() { for (let i = 0; (mutations[1]?.getCurrentResult().isPending || owner()?.pending) && i < 400; i++) await new Promise(resolve => setTimeout(resolve, 25)); assert.equal(owner()?.pending, false, "owned pending must settle"); }
async function open(row = taskA) { const element = loaded(row); button(renderDrawer(element), "Create Coaching").props.onClick(); await tick(); assert.ok(inputModal()); return inputModal(); }
function response(data = { id: 120, rowVersion: 0 }, status = 201, success = true) { return { status, data: { success, data } }; }
try {
  await observer(41).refetch(); select(taskA);
  const tree = renderDrawer(); button(tree, "Create Coaching").props.onClick();
  assert.ok(inputModal(), "Safety Create Coaching must collect a human description before dispatch"); // Parent RED.
  assert.equal(calls.length, 0);
  await tick();
  assert.equal(inputModal().props.description, "");
  assert.doesNotMatch(renderToStaticMarkup(renderInput()), /Synthetic event notes|PRIVATE_/);
  // Controlled DOM-order seam from actual page element order, not mounted browser QA.
  // The shipped focus hook must treat the new input (not its Drawer) as topmost.
  const order = [];
  function collectDialogs(node) {
    if (!node || typeof node !== "object") return;
    if (Array.isArray(node)) { node.forEach(collectDialogs); return; }
    if (["Drawer", "SafetyCoachingInputDialog"].includes(node.type?.name)) order.push(node.type.name);
    collectDialogs(node.props?.children);
  }
  collectDialogs(page());
  const underlyingDialog = { ...fakeDialog };
  dialogNodes = order.map(name => name === "Drawer" ? underlyingDialog : fakeDialog);
  refs.modal[0].current = fakeDialog;
  renderInput(); keydown({ key: "Escape", preventDefault() {}, stopPropagation() {} }); await tick();
  assert.equal(inputModal(), undefined, "normal DOM order must let the input own Escape above its Drawer");
  assert.equal(refs.page.find(ref => ref.current?.id === 41 && "generation" in ref.current)?.current.id, 41);
  dialogNodes = [fakeDialog]; await open();
  for (const value of ["", "   ", "x".repeat(4001)]) { const before = snapshot(); draft(value); submit(); inputModal().props.onSubmit(); await tick(); assert.deepEqual(snapshot(), before); }
  for (const value of [" x ", " " + "x".repeat(4000) + " "]) {
    if (!inputModal()) await open();
    const before = calls.length; draft(value); submit(); await tick(); await settled();
    assert.equal(calls.length, before + 1); assert.deepEqual(calls.at(-1), { url: "/api/safety/events/41/coaching", payload: { notes: value.trim() } });
    assert.equal(inputModal(), undefined); assert.equal(notice().props.receipt.eventId, 41);
    assert.match(renderToStaticMarkup(noticeTree()), /Coaching task created.*41.*Task 120/);
  }

  // Actual method-local envelope and canonical HTTP/marker receipt contract.
  let serviceCases = 0;
  for (const candidate of [response(), response({ id: "9223372036854775807", rowVersion: 0, replayed: false }), response({ id: 120, rowVersion: 2147483648, replayed: true }, 200), response({ id: "120", rowVersion: Number.MAX_SAFE_INTEGER, replayed: true }, 200)]) {
    reviewTransport = async () => candidate; const result = await api.createCoaching(41, { notes: "Human draft" }); serviceCases++;
    assert.deepEqual(Object.keys(result).sort(), ["id", "replayed", "rowVersion"]); assert.equal(result.replayed, candidate.data.data.replayed === true);
  }
  const invalid = [response({}, 201), response(null), response([]), { status: 201, data: null }, { status: 201, data: [] }, { status: 201, data: { success: true } },
    response(undefined, 201, 1), response(undefined, 201, "true"), response(undefined, "201"), response(undefined, 202),
    response({ id: 120, rowVersion: 0 }, 200), response({ id: 120, rowVersion: 0, replayed: true }, 201), response({ id: 120, rowVersion: 1 }, 201)];
  // Undefined arguments use the helper default; make missing HTTP status explicit.
  invalid.push({ data: { success: true, data: { id: 120, rowVersion: 0 } } });
  for (const id of [0, -1, Number.MAX_SAFE_INTEGER + 1, "01", "1e2", "9223372036854775808", "", null, undefined]) invalid.push(response({ id, rowVersion: 0 }));
  for (const rowVersion of ["0", null, undefined, -1, 0.1, Number.MAX_SAFE_INTEGER + 1, NaN]) invalid.push(response({ id: 120, rowVersion, replayed: true }, 200));
  for (const replayed of [undefined, null, 0, 1, "false", "true"]) invalid.push(response({ id: 120, rowVersion: 0, replayed }));
  for (const alias of ["taskId", "task_id", "Id", "row_version", "RowVersion", "Replayed"]) invalid.push(response({ id: 120, rowVersion: 0, [alias]: undefined }));
  for (const [index, candidate] of invalid.entries()) { reviewTransport = async () => candidate; await assert.rejects(api.createCoaching(41, { notes: "Human draft" }), `invalid receipt case ${index}`); serviceCases++; }
  reviewTransport = async () => response({ id: 120, rowVersion: 0, secret: "PRIVATE_RECEIPT", sourceEventId: 99, status: "COMPLETED" });
  assert.deepEqual(await api.createCoaching(41, { notes: "Human draft" }), { id: 120, rowVersion: 0, replayed: false }); serviceCases++;
  assert.deepEqual(await serviceModule.exports.unwrap(Promise.resolve({ data: { success: 1, data: { retained: true } } })), { retained: true }, "global unwrap intentionally unchanged");
  // Defensive own-marker seam, not a claim of reachable prototype pollution.
  // Restore the original descriptor even when the negative case fails.
  const priorReplay = Object.getOwnPropertyDescriptor(Object.prototype, "replayed");
  try {
    Object.defineProperty(Object.prototype, "replayed", { value: true, configurable: true });
    reviewTransport = async () => response({ id: 120, rowVersion: 0 }, 200);
    await assert.rejects(api.createCoaching(41, { notes: "Human draft" }), "HTTP200 needs an own literal replay marker"); serviceCases++;
    reviewTransport = async () => response({ id: 120, rowVersion: 0 }, 201);
    assert.deepEqual(await api.createCoaching(41, { notes: "Human draft" }), { id: 120, rowVersion: 0, replayed: false }); serviceCases++;
  } finally {
    if (priorReplay) Object.defineProperty(Object.prototype, "replayed", priorReplay); else delete Object.prototype.replayed;
  }
  reviewTransport = undefined;

  // Session-close/reopen, current read and selection boundaries are actual callbacks.
  let modal = await open(); const stale = modal; modal.props.onClose(); await tick(); await open();
  let before = snapshot(); stale.props.onChange("stale"); stale.props.onSubmit(); stale.props.onClose(); await tick();
  assert.deepEqual(snapshot(), before); assert.equal(inputModal().props.description, "");
  draft("Earlier human draft"); const earlierDraft = inputModal(); draft("Latest human draft");
  before = snapshot(); earlierDraft.props.onSubmit(); await tick(); assert.deepEqual(snapshot(), before, "earlier same-session submit must not send obsolete text");
  draft("Current human draft"); modal = inputModal(); client.setQueryData(["safety", "detail", 41], taskB);
  before = snapshot(); modal.props.onSubmit(); modal.props.onChange("wrong"); await tick(); assert.deepEqual(snapshot(), before); assert.equal(inputModal().props.description, "Current human draft");
  loaded(taskA); before = snapshot(); modal.props.onSubmit(); await tick(); assert.deepEqual(snapshot(), before, "same-row new selection generation revokes old dialog");
  modal.props.onClose(); await tick(); await open(); draft("Current human draft"); modal = inputModal();
  const heldRead = deferred(); transports.set("41", () => heldRead.promise); const read = observer(41).refetch();
  before = snapshot(); modal.props.onSubmit(); await tick(); assert.deepEqual(snapshot(), before);
  transports.delete("41"); heldRead.resolve(taskA); await read;
  directPermission = false; before = snapshot(); modal.props.onSubmit(); await tick(); assert.deepEqual(snapshot(), before); directPermission = true;
  const readyDrawer = drawer(); readyDrawer.props.safetyRead.onRetry(); before = snapshot(); modal.props.onSubmit(); await tick(); assert.deepEqual(snapshot(), before); await tick();
  inputModal().props.onClose(); await tick();

  // Same-turn unrelated shared action cannot reset or open the new input.
  const heldReview = deferred(); reviewTransport = () => heldReview.promise;
  const control = loaded(taskA); control.props.onAction("review", taskA); const reviewBefore = resets;
  control.props.onAction("createCoaching", taskA); await tick(); assert.equal(inputModal(), undefined); assert.equal(resets, reviewBefore);
  heldReview.resolve(response({ id: 41 })); await settled(); reviewTransport = undefined;

  // Pending description/Close/Cancel/Escape/open and duplicate writes are inert.
  await open(); draft("Pending human draft"); modal = inputModal(); const oldInput = renderInput(modal), closedSubmit = modal.props.onSubmit;
  const heldPost = deferred(); reviewTransport = () => heldPost.promise; before = snapshot(); submit(modal);
  modal.props.onChange("lost edit"); modal.props.onClose(); modal.props.onSubmit(); drawer().props.onAction("createCoaching", taskA);
  keydown?.({ key: "Escape", preventDefault() {}, stopPropagation() {} }); await tick();
  assert.equal(calls.length, before.mutations + 1); assert.equal(resets, before.resets); assert.equal(inputModal().props.description, "Pending human draft");
  assert.equal(inputModal().props.saving, true); assert.equal(find(renderInput(), node => node.type === "textarea").props.disabled, true);
  keydown?.({ key: "Escape", preventDefault() {}, stopPropagation() {} }); button(oldInput, "Cancel").props.onClick();
  heldPost.reject(Object.assign(new Error("PRIVATE_NETWORK_ERROR"), { response: { status: 409 } })); await settled();
  assert.equal(inputModal().props.description, "Pending human draft"); assert.match(renderToStaticMarkup(renderInput()), /could not be confirmed.*earlier request may already/);
  assert.doesNotMatch(renderToStaticMarkup(renderInput()), /PRIVATE_NETWORK_ERROR/);
  reviewTransport = undefined;

  // Observer settlement publication occurs before shared-flight finalization.
  const publications = []; let publicationControl;
  const unsubscribe = mutations[1].subscribe(result => {
    if (result.status === "success" || result.status === "error") {
      publications.push({ status: result.status, pending: owner().pending, resets });
      publicationControl?.props.onAction("createCoaching", taskA);
    }
  }); subscriptions.push(unsubscribe);
  publicationControl = drawer(); const priorPublication = publications.length; before = snapshot(); submit(); await settled();
  assert.equal(publications.length, priorPublication + 1); assert.equal(publications.at(-1).pending, true);
  assert.equal(publications.at(-1).status, "success"); assert.equal(mutations[1].getCurrentResult().status, "success");
  assert.equal(resets, before.resets); assert.equal(inputModal(), undefined); before = snapshot(); closedSubmit(); await tick(); assert.deepEqual(snapshot(), before);
  await open(); draft("Rejected human draft"); publicationControl = drawer(); reviewTransport = async () => { throw new Error("PRIVATE_400"); };
  before = snapshot(); submit(); await settled(); assert.equal(publications.at(-1).status, "error"); assert.equal(publications.at(-1).pending, true);
  assert.equal(resets, before.resets); assert.equal(inputModal().props.description, "Rejected human draft");
  unsubscribe(); publicationControl = undefined; reviewTransport = undefined;

  // Deterministic later handoff seam: attach to the exact mutateAsync promise
  // AFTER submit has attached its await continuation. This runs after its catch,
  // but before the shipped shared-flight outer settlement; no pending polling.
  inputModal().props.onClose(); await tick(); await open(); draft("Late rejection handoff draft");
  const handoffModal = inputModal(), handoffControl = drawer(), handoffPost = deferred();
  reviewTransport = () => handoffPost.promise; before = snapshot(); submit(handoffModal);
  const handoffProbe = mutationPromise.catch(() => {
    const captured = { pending: owner().pending, ...snapshot() };
    handoffModal.props.onSubmit(); handoffModal.props.onClose(); handoffControl.props.onAction("createCoaching", taskA);
    return captured;
  });
  handoffPost.reject(new Error("Controlled late handoff rejection"));
  const handoff = await handoffProbe;
  assert.equal(handoff.pending, true, "late rejected promise handoff must remain owned until shared-flight settlement");
  assert.equal(handoff.mutations, before.mutations + 1); assert.equal(handoff.resets, before.resets);
  assert.equal(calls.length, before.mutations + 1); assert.equal(resets, before.resets); assert.ok(inputModal());
  await settled(); assert.equal(inputModal().props.description, "Late rejection handoff draft"); reviewTransport = undefined;

  // Valid acknowledgement survives ordinary GET errors and callback exceptions.
  const originalInvalidate = client.invalidateQueries.bind(client);
  for (const failure of ["get", "throw"]) {
    inputModal()?.props.onClose(); await tick(); await open(); draft("Human acknowledged draft");
    if (failure === "get") failures.set("41", new Error("PRIVATE_GET_ERROR"));
    else client.invalidateQueries = async () => { throw new Error("PRIVATE_REFRESH_EXCEPTION"); };
    before = snapshot(); submit(); await settled();
    assert.equal(calls.length, before.mutations + 1); assert.equal(inputModal(), undefined);
    assert.equal(mutations[1].getCurrentResult().status, "success");
    assert.equal(notice().props.receipt.refreshWarning, true);
    const html = renderToStaticMarkup(noticeTree()); assert.match(html, /Coaching task created.*could not be refreshed/); assert.doesNotMatch(html, /PRIVATE_/);
    rowsError = summaryError = true; assert.ok(notice()); assert.ok(button(noticeTree(), "Retry related reads")); rowsError = summaryError = false;
    failures.clear(); client.invalidateQueries = originalInvalidate;
    const priorPost = calls.length, priorRead = reads.length; button(noticeTree(), "Retry related reads").props.onClick(); await tick(); await tick();
    assert.equal(calls.length, priorPost); assert.ok(reads.length > priorRead); assert.equal(notice().props.receipt.refreshWarning, false);
  }

  // Replay is a task receipt for the captured event, not a new task or source ID.
  await open(); draft("Replay human draft"); reviewTransport = async () => response({ id: "9223372036854775807", rowVersion: 2147483648, replayed: true }, 200); submit(); await settled();
  assert.match(renderToStaticMarkup(noticeTree()), /Existing coaching task returned.*41.*9223372036854775807/);
  assert.doesNotMatch(renderToStaticMarkup(noticeTree()), /Coaching task created/);
  const originNotice = notice(); select(taskB); const beforeRetry = calls.length; originNotice.props.onRetry(); originNotice.props.onRetry(); await tick(); await tick();
  assert.equal(calls.length, beforeRetry); assert.equal(reads.at(-1), "41");
  const staleRead = deferred(); transports.set("41", () => staleRead.promise); originNotice.props.onRetry(); await tick();
  await open(taskB); assert.equal(notice(), undefined); transports.delete("41"); staleRead.resolve(taskA); await tick(); await tick();
  assert.equal(notice(), undefined); assert.equal(inputModal().props.eventId, 99); assert.equal(inputModal().props.description, "");
  inputModal().props.onClose(); await tick();
  reviewTransport = async () => response({}); await open(); draft("Unconfirmed human draft"); submit(); await settled();
  assert.equal(inputModal().props.description, "Unconfirmed human draft"); assert.equal(notice(), undefined); assert.equal(inputModal().props.error, true);
  inputModal().props.onClose(); await tick(); await open(); draft("Human draft before network pause");
  reviewTransport = async () => response();
  transports.set("41", async () => { onlineManager.setOnline(false); throw new Error("Controlled read failure followed by offline retry pause"); });
  const pausedPosts = calls.length; submit(); await tick();
  for (let i = 0; client.getQueryState(["safety", "detail", 41])?.fetchStatus !== "paused" && i < 100; i++) await new Promise(resolve => setTimeout(resolve, 25));
  assert.equal(client.getQueryState(["safety", "detail", 41])?.fetchStatus, "paused");
  await settled();
  assert.equal(notice().props.receipt.refreshWarning, true, "paused display read qualifies an already-known acknowledgement");
  assert.equal(inputModal(), undefined); assert.equal(calls.length, pausedPosts + 1);
  button(noticeTree(), "Retry related reads").props.onClick(); await tick(); assert.equal(notice().props.receipt.refreshing, false);
  transports.clear(); onlineManager.setOnline(true); await tick(); await observer(41).refetch();
  button(noticeTree(), "Retry related reads").props.onClick(); await tick(); await tick();
  assert.equal(notice().props.receipt.refreshWarning, false); assert.equal(calls.length, pausedPosts + 1);

  // The task link and even directly retained GET retry callbacks obey all three
  // existing access boundaries. An empty session elsewhere is legacy_allow.
  for (const denial of ["view", "safety", "fleet.driver_safety"]) {
    sessionOverride = { user: {}, company: {}, entitlements: { safety: true, "fleet.driver_safety": true } };
    if (denial === "view") exportPermission = false;
    else sessionOverride.entitlements[denial] = false;
    const denied = notice(), deniedTree = noticeTree(denied), priorReads = reads.length, priorPosts = calls.length;
    assert.equal(denied.props.canView, false); assert.equal(find(deniedTree, node => node.type === "a"), undefined);
    const deniedRetry = button(deniedTree, "Retry related reads");
    if (denied.props.receipt.refreshWarning) assert.equal(deniedRetry.props.disabled, true);
    else assert.equal(deniedRetry, undefined, "healthy known receipts do not expose a recovery control");
    denied.props.onRetry(); await tick(); assert.equal(reads.length, priorReads); assert.equal(calls.length, priorPosts);
    assert.equal(notice().props.receipt.refreshWarning, true);
    exportPermission = true; sessionOverride = undefined;
  }

  // Actual shipped list/summary hook options and QueryObservers, not render-only
  // error flags. Failures and offline retry pauses cannot erase a known receipt.
  details.set("events", [taskA, taskB]); details.set("dashboard", {});
  for (const name of ["useSafetyEvents", "useSafetySummary"]) {
    const surface = new QueryObserver(client, hookModule.exports[name]()); surfaces.set(name, surface);
    subscriptions.push(surface.subscribe(() => {})); await surface.refetch();
  }
  for (const failed of ["events", "dashboard"]) {
    await open(); draft(`Human draft with ${failed} read failure`);
    failures.set(failed, new Error("PRIVATE_SURFACE_READ")); submit(); await settled();
    assert.equal(surfaces.get(failed === "events" ? "useSafetyEvents" : "useSafetySummary").getCurrentResult().isError, true);
    assert.ok(notice()); assert.equal(notice().props.receipt.refreshWarning, true); assert.equal(mutations[1].getCurrentResult().status, "success");
    assert.doesNotMatch(renderToStaticMarkup(noticeTree()), /PRIVATE_SURFACE_READ/);
    failures.clear(); const priorPosts = calls.length; notice().props.onRetry(); await tick(); await tick();
    assert.equal(calls.length, priorPosts); assert.equal(notice().props.receipt.refreshWarning, false);
  }
  await open(); draft("Human draft with paused list read");
  transports.set("events", async () => { onlineManager.setOnline(false); throw new Error("Controlled list offline retry pause"); });
  const listPausePosts = calls.length; submit(); await tick();
  for (let i = 0; client.getQueryState(["safety"])?.fetchStatus !== "paused" && i < 100; i++) await new Promise(resolve => setTimeout(resolve, 25));
  assert.equal(client.getQueryState(["safety"])?.fetchStatus, "paused"); await settled();
  assert.ok(notice()); assert.equal(notice().props.receipt.refreshWarning, true); assert.equal(calls.length, listPausePosts + 1);
  transports.clear(); onlineManager.setOnline(true); await tick();
  await Promise.all([...surfaces.values()].map(surface => surface.refetch())); await observer(41).refetch();
  notice().props.onRetry(); await tick(); await tick(); assert.equal(calls.length, listPausePosts + 1); assert.equal(notice().props.receipt.refreshWarning, false);

  // Captured source can become inactive while the POST is pending. Initial
  // invalidation does not invent a confirmed refresh; explicit retry GETs A,
  // without changing current B selection or repeating the acknowledged POST.
  await open(); draft("Human draft before selection change");
  const inactivePost = deferred(); reviewTransport = () => inactivePost.promise; const inactivePosts = calls.length; submit();
  const selectionPage = page(); observer(41).destroy(); queries.delete(41); select(taskB, selectionPage); observer(99); inactivePost.resolve(response()); await settled();
  assert.equal(notice().props.receipt.eventId, 41); assert.equal(notice().props.receipt.refreshWarning, true);
  const inactiveReads = reads.length; notice().props.onRetry(); await tick(); await tick();
  assert.ok(reads.slice(inactiveReads).includes("41")); assert.equal(calls.length, inactivePosts + 1);
  assert.equal(refs.page.find(ref => ref.current?.id === 99 && "generation" in ref.current)?.current.id, 99);
  assert.equal(notice().props.receipt.refreshWarning, false);
  console.log("Safety coaching input PASS: actual notes-only explicit dialog, " + serviceCases + " direct receipt cases, actual query/mutation observers and settlement publications, owned pending/draft/current-query boundaries, replay/uncertainty truth and origin-bound GET-only receipt refresh; controlled transport only.");
} finally { onlineManager.setOnline(true); cleanup?.(); for (const unsubscribe of subscriptions) unsubscribe(); client.clear(); client.unmount(); globalThis.document = originalDocument; }
