import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { createRequire } from "node:module";
import { fileURLToPath } from "node:url";
import { dirname, resolve } from "node:path";
import vm from "node:vm";
import { execFileSync } from "node:child_process";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const require = createRequire(import.meta.url);
const esbuild = require("esbuild");
const sourceArg = process.argv[2];
assert.ok(!sourceArg || /^--source-ref=[a-f0-9]{40}$/.test(sourceArg), "optional source ref requires full SHA");
const page = sourceArg
  ? execFileSync("git", ["show", `${sourceArg.slice(13)}:frontend/src/pages/VehiclesPage.tsx`], { cwd: resolve(root, ".."), encoding: "utf8" })
  : readFileSync(resolve(root, "src/pages/VehiclesPage.tsx"), "utf8");
const shared = readFileSync(resolve(root, "src/components/ConfirmDialog.tsx"), "utf8");

// Execute the ACTUAL shared component's callbacks with bounded hook/element doubles.
// This is not React mounting, a real DOM, focus-trap accessibility acceptance,
// browser networking, authorization middleware or persisted lifecycle evidence.
const built = await esbuild.build({
  entryPoints: [resolve(root, "src/components/ConfirmDialog.tsx")],
  bundle: true, format: "cjs", write: false, logLevel: "silent",
  external: ["react", "react/jsx-runtime", "lucide-react"],
});
function mountSimulation({ busy = false, connected = true } = {}) {
  const effects = [], nodes = [], listeners = new Map();
  let confirms = 0, cancels = 0;
  const document = { activeElement: null, querySelector: () => landmark };
  class Element {
    constructor(type, props = {}) { this.type = type; this.props = props; this.isConnected = true; this.attrs = {}; }
    focus() { document.activeElement = this; }
    hasAttribute(key) { return key in this.attrs; }
    setAttribute(key, value) { this.attrs[key] = value; }
    contains(node) { return nodes.includes(node); }
    querySelectorAll() { return nodes.filter(x => x.type === "button" && !x.props.disabled); }
  }
  const landmark = new Element("main"), opener = new Element("button");
  opener.isConnected = connected;
  document.activeElement = opener;
  const jsx = (type, props) => {
    const node = new Element(type, props);
    nodes.push(node);
    if (props?.ref) props.ref.current = node;
    return node;
  };
  const module = { exports: {} };
  vm.runInNewContext(built.outputFiles[0].text, {
    module, exports: module.exports,
    require: name => {
      if (name === "react") return { useId: () => `id-${nodes.length}`, useRef: value => ({ current: value }), useEffect: callback => effects.push(callback) };
      if (name === "react/jsx-runtime") return { jsx, jsxs: jsx };
      if (name === "lucide-react") return { X: () => null };
      throw new Error(`Unexpected test import: ${name}`);
    },
    HTMLElement: Element, document,
    window: { addEventListener: (name, fn) => listeners.set(name, fn), removeEventListener: name => listeners.delete(name) },
  });
  module.exports.ConfirmDialog({
    title: "Archive W1CERT-20260831-001 (1145)", message: "Controlled target only",
    confirmLabel: "Archive vehicle", busy, error: "Controlled rejection",
    returnFocusTo: opener, onConfirm: () => confirms++, onCancel: () => cancels++,
  });
  const cleanups = effects.map(fn => fn()).filter(fn => typeof fn === "function");
  const cancel = nodes.find(x => x.props.ref && x.type === "button");
  const confirm = nodes.find(x => x.type === "button" && x.props.onClick && x.props.children === (busy ? "Working..." : "Archive vehicle"));
  assert.ok(cancel && confirm);
  assert.equal(document.activeElement, cancel, "actual mount effect focuses Cancel in this simulation");
  return { nodes, cancel, confirm, document, opener, landmark,
    escape: () => listeners.get("keydown")({ key: "Escape" }),
    cleanup: () => cleanups.forEach(fn => fn()), counts: () => ({ confirms, cancels }) };
}
for (const busy of [false, true]) {
  const dialog = mountSimulation({ busy });
  assert.equal(dialog.confirm.props.disabled, busy);
  dialog.cancel.props.onClick();
  dialog.escape();
  assert.deepEqual(dialog.counts(), { confirms: 0, cancels: busy ? 0 : 2 }, "Cancel/Escape never submit; busy refuses cancellation");
  assert.ok(dialog.nodes.some(x => x.props.role === "dialog" && x.props["aria-modal"] === "true"));
  assert.ok(dialog.nodes.some(x => x.props.role === "alert" && x.props.children === "Controlled rejection"));
  dialog.cleanup();
  assert.equal(dialog.document.activeElement, dialog.opener, "connected opener restored by actual cleanup callback");
}
const detached = mountSimulation({ connected: false });
detached.cleanup();
assert.equal(detached.document.activeElement, detached.landmark, "detached opener falls back to stable landmark");
console.log("Shared confirmation callback simulation passed (not a mounted/browser test).");

// Product wiring contract. This assertion intentionally fails on the c36 baseline.
assert.doesNotMatch(page, /window\.confirm\(/, "vehicle lifecycle must use the supported in-app dialog, not native window.confirm");
assert.match(page, /import\s*\{\s*ConfirmDialog\s*\}\s*from\s*["']@\/components\/ConfirmDialog["']/);
assert.match(page, /<ConfirmDialog\b/);
assert.doesNotMatch(page, /onDelete=\{[^\n]*remove\.mutate/, "opening Archive cannot dispatch its mutation");
assert.match(page, /onReactivate=\{reactivateSelected\}/, "direct identified reactivation uses its guard, not a new confirmation");
assert.match(page, /returnFocusTo=\{/, "the page must supply the opener captured before dialog mount");
assert.match(page, /document\.activeElement instanceof HTMLElement/, "capture actual opener before state update");
assert.match(page, /busy=\{lifecycleBusy\}/, "pending supplied to dialog; settled uncertainty may be dismissed with retained latch");
assert.match(page, /apiErrorMessage\(/, "handled lifecycle errors use the existing safe envelope renderer");
assert.match(page, /const lifecycleSelectionPreparing = selectedId != null && \(detail\.isLoading \|\| detail\.isFetching\)/,
  "lifecycle busy semantics describe only an active authoritative-detail request");
assert.match(page, /const lifecycleSelectionUnavailable = selectedId != null && !lifecycleSelectionPreparing[\s\S]*detail\.isError \|\| !selectedDetailRecord/,
  "terminal detail failure is a fail-closed unavailable state rather than false busy");
assert.match(page, /lifecycleSelectionPreparing=\{lifecycleSelectionPreparing\}[\s\S]*lifecycleSelectionUnavailable=\{lifecycleSelectionUnavailable\}/,
  "pending and unavailable lifecycle states are supplied independently to the drawer");
assert.equal([...page.matchAll(/disabled=\{lifecycleBusy \|\| lifecycleSelectionPreparing \|\| lifecycleSelectionUnavailable \|\| lifecycleNeedsRefresh\}/g)].length, 2,
  "both Archive and Reactivate remain disabled until authoritative detail is ready");
assert.equal([...page.matchAll(/aria-busy=\{lifecycleSelectionPreparing \? true : undefined\}/g)].length, 2,
  "both lifecycle controls expose busy only while a request is active");
assert.match(page, /Status unavailable[\s\S]*Retry status check/,
  "terminal detail failure is truthful and provides an explicit retry path");
assert.match(shared, /event\.key === "Escape" && !busyRef\.current/);
assert.match(shared, /onClick=\{\(\) => \{ if \(!busy\) onCancel\(\); \}\}/);
assert.match(shared, /opener && opener\.isConnected/);
assert.match(shared, /fallbackFocusSelector = "main"/);
console.log("Vehicle lifecycle confirmation source contract passed.");

// Actual page-local handlers, not a hand-copied state machine. Controlled bindings
// model rerenders; this is NOT a React mount, network request or persisted result.
const start = page.indexOf("  const lifecycleSelectionValid =");
const end = page.indexOf("\n  return (", start);
assert.ok(start > 0 && end > start, "guard/handler region is identifiable");
const handlerCode = esbuild.transformSync(page.slice(start, end) + "\nglobalThis.handlers = { openArchive, cancelArchive, confirmArchive, reactivateSelected, closeVehicle, reloadLifecycleStatus };", { loader: "ts" }).code;
function documentSimulation() {
  const begin = page.indexOf("let vehicleLifecycleRefreshRequired = false;");
  const finish = page.indexOf("\ntype VehicleArchiveTarget", begin);
  assert.ok(begin > 0 && finish > begin);
  const context = {};
  vm.runInNewContext(esbuild.transformSync(page.slice(begin, finish) + `
    globalThis.state = {
      get vehicleLifecycleRefreshRequired() { return vehicleLifecycleRefreshRequired; },
      set vehicleLifecycleRefreshRequired(value) { vehicleLifecycleRefreshRequired = value; },
      get vehicleLifecyclePending() { return vehicleLifecyclePending; },
      set vehicleLifecyclePending(value) { vehicleLifecyclePending = value; },
      notifyVehicleLifecycle, subscribeVehicleLifecycle, vehicleLifecycleSnapshot
    };`, { loader: "ts" }).code, context);
  return context.state;
}
function pageSimulation({ archived = false, moduleState = documentSimulation() } = {}) {
  const calls = [], opener = {};
  const ctx = {
    session: { company: { id: 4 } }, selectedId: 1145,
    selectedRecord: { id: 1145, companyId: 4, vehicleCode: "W1CERT-20260831-001", deletedAt: archived ? "2026-08-31T19:30:00Z" : null },
    archivedView: archived, detail: { isError: false, isLoading: false, isFetching: false }, canDelete: true, canUpdate: true,
    lifecycleRefreshRequired: { current: false }, lifecycleInFlight: { current: false }, lifecycleBusy: false,
    lifecycleNeedsRefresh: false, archiveTarget: null, error: null, save: { isPending: false }, assign: { isPending: false },
    g: (row, ...keys) => keys.map(key => row[key]).find(value => value !== undefined),
    document: { activeElement: opener }, HTMLElement: Object,
    window: { location: { reload: () => calls.push(["reload"]) } },
    setArchiveTarget: value => { ctx.archiveTarget = value; },
    setLifecycleGuardError: value => { ctx.error = value; },
    setLifecycleNeedsRefresh: value => { ctx.lifecycleNeedsRefresh = value; },
    setSelectedId: value => { ctx.selectedId = value; },
    remove: { reset() {}, mutate: id => calls.push(["archive", id]) },
    reactivate: { reset() {}, mutate: id => calls.push(["reactivate", id]) },
  };
  for (const name of Object.keys(moduleState)) Object.defineProperty(ctx, name, {
    get: () => moduleState[name], set: value => { moduleState[name] = value; }, configurable: true,
  });
  ctx.selectedDetailRecord = ctx.selectedRecord;
  ctx.apiErrorMessage = (error, fallback) => error?.message ?? fallback;
  ctx.queryClient = { invalidateQueries: async () => { ctx.invalidations++; } };
  ctx.invalidations = 0;
  // Capture the actual mutation configuration without invoking its endpoint.
  // This executes callbacks, not React Query's lifecycle scheduler.
  ctx.useMutation = config => config;
  ctx.vehiclesApi = { archive: () => { throw new Error("No API calls in simulation"); }, reactivate: () => { throw new Error("No API calls in simulation"); } };
  const mutationStart = page.indexOf("  const remove = useMutation({");
  const mutationEnd = page.indexOf("  const assign = useMutation({", mutationStart);
  const mutationCode = esbuild.transformSync(page.slice(mutationStart, mutationEnd) + "\nglobalThis.mutationCallbacks = { remove, reactivate };", { loader: "ts" }).code;
  // Separate VM lexical names avoid shadowing the mutation-dispatch stubs in handlers.
  vm.runInNewContext(`(() => { ${mutationCode} })()`, ctx);
  vm.runInNewContext(handlerCode, ctx);
  return { ctx, calls, h: ctx.handlers, callbacks: ctx.mutationCallbacks, opener };
}
{
  const { ctx, calls, h, opener } = pageSimulation();
  h.openArchive();
  assert.equal(calls.length, 0);
  assert.ok(Object.isFrozen(ctx.archiveTarget));
  assert.equal(ctx.archiveTarget.id, "1145");
  assert.equal(ctx.archiveTarget.companyId, "4");
  assert.equal(ctx.archiveTarget.opener, opener);
  h.cancelArchive();
  assert.equal(ctx.archiveTarget, null);
  assert.equal(calls.length, 0);
  h.openArchive(); h.confirmArchive(); h.confirmArchive(); h.cancelArchive();
  assert.deepEqual(calls, [["archive", "1145"]], "two synchronous confirmations dispatch once");
  assert.ok(ctx.archiveTarget, "pending cancellation retains target");
  ctx.lifecycleInFlight.current = false; ctx.lifecycleBusy = true;
  h.cancelArchive(); assert.ok(ctx.archiveTarget, "React pending state independently blocks cancellation");
}
const invalidContexts = [
  ctx => { ctx.canDelete = false; ctx.canUpdate = false; },
  ctx => { ctx.session = null; },
  ctx => { ctx.session = { company: { id: 5 } }; },
  ctx => { ctx.selectedId = 999; },
  ctx => { ctx.selectedRecord.companyId = 5; },
  ctx => { ctx.selectedRecord.id = ""; },
  ctx => { ctx.selectedRecord.vehicleCode = ""; },
  ctx => { ctx.selectedRecord.vehicleCode = "   "; },
  ctx => { ctx.selectedDetailRecord = undefined; },
  ctx => { ctx.selectedDetailRecord = { ...ctx.selectedRecord }; },
  ctx => { ctx.detail.isError = true; },
  ctx => { ctx.detail.isLoading = true; },
  ctx => { ctx.detail.isFetching = true; },
  ctx => { ctx.archivedView = !ctx.archivedView; },
  ctx => { ctx.selectedRecord.deletedAt = undefined; },
  ctx => { ctx.save.isPending = true; },
  ctx => { ctx.assign.isPending = true; },
  ctx => { ctx.lifecycleRefreshRequired.current = true; },
];
for (const invalidate of invalidContexts) {
  for (const archived of [false, true]) {
    const { ctx, calls, h } = pageSimulation({ archived });
    invalidate(ctx);
    if (archived) h.reactivateSelected(); else h.openArchive();
    assert.equal(calls.length, 0, `invalid context cannot dispatch: ${invalidate}`);
    assert.equal(ctx.archiveTarget, null, `invalid context cannot open Archive: ${invalidate}`);
  }
}
for (const invalidate of [...invalidContexts, ctx => { ctx.session = { company: { id: 4 } }; }, ctx => { ctx.selectedRecord.vehicleCode = "CHANGED"; }]) {
  const { ctx, calls, h } = pageSimulation();
  h.openArchive(); invalidate(ctx); h.confirmArchive();
  assert.equal(calls.length, 0, `changed context fails closed at confirm: ${invalidate}`);
}
{
  const { ctx, calls, h } = pageSimulation({ archived: true });
  h.reactivateSelected(); h.reactivateSelected(); h.openArchive();
  assert.deepEqual(calls, [["reactivate", "1145"]], "direct reactivation shares synchronous lock");
  assert.equal(ctx.archiveTarget, null);
}
{
  const { ctx, calls, h } = pageSimulation();
  h.openArchive();
  ctx.lifecycleRefreshRequired.current = true; ctx.lifecycleNeedsRefresh = true; ctx.error = "Uncertain outcome. Reload vehicle status.";
  h.confirmArchive(); h.cancelArchive(); h.openArchive(); h.reactivateSelected(); h.closeVehicle();
  assert.equal(calls.length, 0);
  assert.equal(ctx.archiveTarget, null, "settled uncertain error may be dismissed");
  assert.match(ctx.error, /Uncertain outcome/, "error survives cancellation/drawer close");
  assert.equal(ctx.lifecycleRefreshRequired.current, true);
  ctx.lifecycleInFlight.current = true; h.reloadLifecycleStatus();
  assert.equal(calls.length, 0, "reload blocked while pending");
  ctx.lifecycleInFlight.current = false; h.reloadLifecycleStatus();
  assert.deepEqual(calls, [["reload"]], "uncertain recovery reloads, never redispatches");
}
for (const mutation of ["remove", "reactivate"]) {
  const moduleState = documentSimulation();
  const errorCase = pageSimulation({ moduleState });
  errorCase.ctx.lifecycleInFlight.current = true;
  errorCase.callbacks[mutation].onError(new Error("Controlled backend rejection"));
  assert.equal(moduleState.vehicleLifecycleRefreshRequired, true);
  assert.equal(errorCase.ctx.lifecycleRefreshRequired.current, true);
  assert.equal(errorCase.ctx.lifecycleNeedsRefresh, true);
  assert.match(errorCase.ctx.error, /Controlled backend rejection/);
  errorCase.callbacks[mutation].onSettled();
  assert.equal(errorCase.ctx.lifecycleInFlight.current, false);
  assert.equal(moduleState.vehicleLifecycleRefreshRequired, true, "settlement never clears error latch");
  const successCase = pageSimulation();
  await successCase.callbacks[mutation].onSuccess();
  assert.equal(successCase.ctx.selectedId, null);
  assert.equal(successCase.ctx.invalidations, 1);
  const begin = page.indexOf(`  const ${mutation} = useMutation({`);
  const finish = page.indexOf("\n  });", begin);
  const code = page.slice(begin, finish);
  assert.match(code, /retry: false/);
  assert.match(code, /onError:[\s\S]*vehicleLifecycleRefreshRequired = true/);
  assert.match(code, /onError:[\s\S]*lifecycleRefreshRequired\.current = true/);
  assert.match(code, /setLifecycleNeedsRefresh\(true\)/);
  assert.match(code, /onSuccess:[\s\S]*setSelectedId\(null\)/);
  assert.match(code, /invalidateQueries\(\{ queryKey: \["vehicles"\] \}\)/);
}
assert.match(page, /useState\(vehicleLifecycleRefreshRequired\)/);
assert.match(page, /useRef\(vehicleLifecycleRefreshRequired\)/);
assert.equal([...page.matchAll(/vehicleLifecycleRefreshRequired = false/g)].length, 1, "only document/module initialization clears latch");
assert.doesNotMatch(page, /lifecycleRefreshRequired\.current = false/, "SPA actions cannot clear uncertainty latch");
assert.match(page, /onConfirm=\{lifecycleNeedsRefresh \? reloadLifecycleStatus : confirmArchive\}/);
console.log("Actual page handler simulation passed (no React mount, HTTP or persistence claim).");

// Two page contexts share the ACTUAL module store, simulating an old request
// finishing after remount. Local refs deliberately remain false on the new page:
// action-time module guards must work even before a subscription-driven rerender.
for (const mutation of ["remove", "reactivate"]) {
  for (const outcome of ["success", "error"]) {
    const moduleState = documentSimulation(), snapshots = [];
    const unsubscribe = moduleState.subscribeVehicleLifecycle(() => snapshots.push(moduleState.vehicleLifecycleSnapshot()));
    assert.equal(moduleState.vehicleLifecycleSnapshot(), 0);
    const oldPage = pageSimulation({ archived: mutation === "reactivate", moduleState });
    if (mutation === "remove") { oldPage.h.openArchive(); oldPage.h.confirmArchive(); }
    else oldPage.h.reactivateSelected();
    assert.equal(oldPage.calls.length, 1);
    assert.equal(moduleState.vehicleLifecyclePending, true);
    assert.equal(moduleState.vehicleLifecycleSnapshot(), 1);
    const newActive = pageSimulation({ moduleState });
    const newArchived = pageSimulation({ archived: true, moduleState });
    newActive.h.openArchive(); newActive.h.confirmArchive(); newActive.h.closeVehicle();
    newArchived.h.reactivateSelected(); newActive.h.reloadLifecycleStatus();
    assert.equal(newActive.calls.length + newArchived.calls.length, 0, "old pending request excludes all new-page actions/reload");
    assert.equal(newActive.ctx.selectedId, 1145, "pending guard also excludes drawer close");
    if (outcome === "error") {
      oldPage.callbacks[mutation].onError(new Error("Controlled old-page failure"));
      assert.equal(moduleState.vehicleLifecycleSnapshot(), 3);
      newActive.h.reloadLifecycleStatus();
      assert.equal(newActive.calls.length, 0, "failure does not permit reload before old settlement");
    } else {
      await oldPage.callbacks[mutation].onSuccess();
      assert.equal(moduleState.vehicleLifecyclePending, true, "success waits for settlement before releasing shared lock");
    }
    oldPage.callbacks[mutation].onSettled();
    assert.equal(moduleState.vehicleLifecyclePending, false);
    assert.equal(moduleState.vehicleLifecycleRefreshRequired, outcome === "error");
    if (outcome === "error") {
      newActive.h.openArchive(); newActive.h.confirmArchive(); newArchived.h.reactivateSelected();
      assert.equal(newActive.calls.length + newArchived.calls.length, 0, "new-page stale local refs cannot bypass old failure latch");
      newActive.h.reloadLifecycleStatus();
      assert.deepEqual(newActive.calls, [["reload"]]);
      assert.equal(moduleState.vehicleLifecycleSnapshot(), 2, "reload stub cannot clear document latch");
      assert.deepEqual(snapshots, [1, 3, 2]);
    } else {
      newActive.h.openArchive(); newActive.h.confirmArchive(); newArchived.h.reactivateSelected();
      assert.deepEqual(newActive.calls, [["archive", "1145"]], "new action may begin only after old settlement and valid detail");
      assert.equal(newArchived.calls.length, 0, "new action again holds shared lock");
      assert.equal(moduleState.vehicleLifecyclePending, true);
      assert.deepEqual(snapshots, [1, 0, 1]);
    }
    unsubscribe();
    const before = snapshots.length;
    moduleState.notifyVehicleLifecycle();
    assert.equal(snapshots.length, before, "actual unsubscribe removes listener");
  }
}
assert.match(page, /useSyncExternalStore\(subscribeVehicleLifecycle, vehicleLifecycleSnapshot, vehicleLifecycleSnapshot\)/);
assert.match(page, /const lifecycleBusy = \(lifecycleDocumentState & 1\) !== 0/);
assert.match(page, /const lifecycleNeedsRefresh = localLifecycleNeedsRefresh \|\| \(lifecycleDocumentState & 2\) !== 0/);
assert.match(page, /const lifecycleRecoveryError = lifecycleGuardError \?\? \(lifecycleNeedsRefresh/);
console.log("Cross-context pending/settlement/error-store simulation passed (not a mounted SPA/navigation test).");
