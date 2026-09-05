import assert from "node:assert/strict";
import { createRequire } from "node:module";
import { fileURLToPath } from "node:url";
import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { webcrypto } from "node:crypto";
import test from "node:test";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const require = createRequire(import.meta.url);
const esbuild = require("esbuild");
const built = await esbuild.build({
  stdin: { contents: 'export * from "@/services/dashcamApi"; export {apiClient} from "@/services/apiClient";', loader: "ts", resolveDir: root },
  bundle: true, platform: "node", format: "cjs", write: false, logLevel: "silent",
  alias: { "@": resolve(root, "src") }, external: ["axios"],
  define: { "import.meta.env": "{}" },
});
function serviceFixture(bundle = built.outputFiles[0].text, customRequire = require, document = {}, url = URL) {
  const storage = new Map();
  const localStorage = { getItem: (key) => storage.get(key) ?? null, setItem: (key, value) => storage.set(key, value), removeItem: (key) => storage.delete(key) };
  const session = { token: "camera-fixture-token", csrfToken: "", permissions: ["dashcam:manage"], role: "Test",
    user: { id: 7 }, company: { id: 4 } };
  storage.set("opstrax.session.v3", JSON.stringify({ session }));
  const module = { exports: {} };
  new Function("module", "exports", "require", "crypto", "localStorage", "window", "document", "URL", bundle)(
    module, module.exports, customRequire, webcrypto, localStorage,
    { location: { hostname: "example.test", pathname: "/dashcam" }, localStorage }, document, url);
  const calls = [];
  let response = { status: 201, data: { success: true, data: { id: 19, rowVersion: 1,
    dataSource: "stored_metadata", provenanceStatus: "unverified", mediaAvailable: false, automatedAssessmentAvailable: false } } };
  let adapter;
  module.exports.apiClient.defaults.adapter = async (config) => {
    calls.push(config);
    if (adapter) return adapter(config);
    return { ...response, config, headers: {}, statusText: "fixture" };
  };
  return { api: module.exports, calls, session, storage, setResponse: (value) => { response = value; }, setAdapter: (value) => { adapter = value; } };
}

test("actual camera service refuses forbidden manual fields before transport", async () => {
  const f = serviceFixture();
  let rejected = false;
  try { await f.api.dashcamApi.create({ eventType: "Near Miss", title: "Manual note", severity: "High", aiConfidence: 99, reviewStatus: "Reviewed" }, f.session); }
  catch { rejected = true; }
  assert.equal(f.calls.length, 0, "forbidden manual fields must never be forwarded");
  assert.equal(rejected, true);
});

const input = () => ({ eventType: "Near Miss", title: "Manual note", severity: "High" });
const receipt = (patch = {}) => ({ id: 19, rowVersion: 4, dataSource: "stored_metadata", provenanceStatus: "unverified", mediaAvailable: false, automatedAssessmentAvailable: false, ...patch });
const response = (data = receipt(), status = 201) => ({ status, data: { success: true, data } });
const record = (patch = {}) => ({ id: 19, rowVersion: 3, sourceAuthority: "LegacyUnverified", deletedAt: null, ...input(), driverId: 7, occurredAt: "2025-01-01T12:00:00.000Z", ...patch });
const deferred = () => { let resolve, reject; const promise = new Promise((yes, no) => { resolve = yes; reject = no; }); return { promise, resolve, reject }; };
const settle = async () => { for (let i = 0; i < 15; i++) await Promise.resolve(); await new Promise((resolve) => setImmediate(resolve)); };

test("actual create and update send only exact numeric/manual payload and validate pinned receipts", async () => {
  const f = serviceFixture();
  f.setResponse(response());
  const created = await f.api.dashcamApi.create({ ...input(), title: " Manual note ", driverId: 7, occurredAt: null }, f.session);
  assert.deepEqual(created, { ...receipt(), id: "19" });
  assert.deepEqual(JSON.parse(f.calls[0].data), { ...input(), driverId: 7, occurredAt: null });
  assert.equal(f.calls[0].headers.has("X-OpsTrax-Expected-Session-Guard"), false);
  f.setResponse(response(receipt({ id: "9223372036854775807" }), 200));
  const updated = await f.api.dashcamApi.update("9223372036854775807", { rowVersion: 3, title: "Changed", locationDescription: null }, f.session);
  assert.equal(updated.id, "9223372036854775807");
  assert.equal(f.calls[1].url, "/api/dashcam/events/9223372036854775807");
  assert.deepEqual(JSON.parse(f.calls[1].data), { rowVersion: 3, title: "Changed", locationDescription: null });
});

test("invalid primitives, aliases, bounds, unsafe JSON IDs and versions send no request", async () => {
  const f = serviceFixture();
  for (const patch of [{ eventType: "" }, { eventType: "x".repeat(121) }, { title: "x".repeat(221) },
    { severity: " High " }, { severity: "high" }, { severity: true }, { title: {} },
    { driverId: "7" }, { driverId: true }, { driverId: [7] }, { driverId: 0 }, { driverId: -1 },
    { driverId: Number.MAX_SAFE_INTEGER + 1 }, { locationDescription: "x".repeat(221) }, { locationDescription: "" }, { locationDescription: "  " },
    { occurredAt: "2025-02-29T12:00:00Z" }, { occurredAt: "2025-01-01T12:00:00" },
    { occurredAt: "2025-01-01T12:00:00.0000001Z" }, { occurredAt: "9999-01-01T12:00:00Z" },
    { EventType: "near miss" }, { sourceAuthority: "LegacyUnverified" }, { roadClipUrl: "private" }]) {
    await assert.rejects(() => f.api.dashcamApi.create({ ...input(), ...patch }, f.session));
  }
  for (const body of [{ rowVersion: 3 }, { title: "Changed" }, { rowVersion: "3", title: "Changed" },
    { rowVersion: true, title: "Changed" }, { rowVersion: -1, title: "Changed" },
    { rowVersion: Number.MAX_SAFE_INTEGER + 1, title: "Changed" }, { rowVersion: 3, safetyEventId: 7 }]) {
    await assert.rejects(() => f.api.dashcamApi.update(19, body, f.session));
  }
  for (const id of [0, -1, "019", "1e2", true, {}, Number.MAX_SAFE_INTEGER + 1, "9223372036854775808"]) {
    assert.throws(() => f.api.dashcamApi.update(id, { rowVersion: 3, title: "Changed" }, f.session));
  }
  assert.equal(f.calls.length, 0);
});

const query = require("@tanstack/react-query");
const react = require("react");
const jsxRuntime = require("react/jsx-runtime");
const workflowBuilt = await esbuild.build({
  stdin: { contents: 'export * from "@/components/CameraMetadataDialog"; export * from "@/services/dashcamApi"; export {apiClient} from "@/services/apiClient";', loader: "ts", resolveDir: root },
  bundle: true, platform: "node", format: "cjs", write: false, logLevel: "silent",
  alias: { "@": resolve(root, "src") }, external: ["axios", "react", "react/jsx-runtime", "@tanstack/react-query", "lucide-react"], define: { "import.meta.env": "{}" },
});
const pageBuilt = await esbuild.build({
  stdin: { contents: 'export * from "@/pages/Batch4SafetyPage"; export {apiClient} from "@/services/apiClient";', loader: "ts", resolveDir: root },
  bundle: true, platform: "node", format: "cjs", write: false, logLevel: "silent",
  alias: { "@": resolve(root, "src") }, external: ["axios", "react", "react/jsx-runtime", "@tanstack/react-query", "lucide-react", "react-router"], define: { "import.meta.env": "{}" },
  plugins: [{ name: "controlled-page-read-hooks", setup(build) {
    build.onResolve({ filter: /(?:^|\/)useAuth$/ }, () => ({ path: "camera-test-auth", external: true }));
    build.onResolve({ filter: /(?:^|\/)useBatch4$/ }, () => ({ path: "camera-test-queries", external: true }));
    build.onLoad({ filter: /Batch4SafetyPage\.tsx$/ }, ({ path }) => ({ contents: readFileSync(path, "utf8") + "\nexport { Drawer, configs, runAction };", loader: "tsx" }));
  } }],
});
const uiBuilt = await esbuild.build({
  stdin: { contents: 'export { DataTable } from "@/components/ui"; export {apiClient} from "@/services/apiClient";', loader: "ts", resolveDir: root },
  bundle: true, platform: "node", format: "cjs", write: false, logLevel: "silent", alias: { "@": resolve(root, "src") },
  external: ["axios", "react", "react/jsx-runtime", "lucide-react"], define: { "import.meta.env": "{}" },
});
const { renderToStaticMarkup } = require("react-dom/server");
function elements(tree) {
  if (Array.isArray(tree)) return tree.flatMap(elements);
  if (!tree || typeof tree !== "object" || !tree.props) return [];
  return [tree, ...elements(tree.props.children)];
}
const component = (tree, name) => elements(tree).find((element) => element.type?.name === name);
const button = (tree, label) => elements(tree).find((element) => element.type === "button" && renderToStaticMarkup(element).includes(label));

test("actual camera page feeds the shared DataTable neutral severity and whitelisted values", () => {
  const f = workflowFixture({ source: pageBuilt.outputFiles[0].text });
  try {
    const raw = record({ severity: null, aiSummary: "private-ai", roadClipUrl: "private-url", providerPayloadHash: "private-hash" });
    const page = f.renderPage("dashcam", { rows: { ...f.view.rows, data: [raw] }, summary: { ...f.view.summary, data: {} } });
    const table = component(page, "DataTable"); assert.ok(table);
    assert.equal(table.props.rows[0].recordedLevel, "Unavailable"); assert.equal(Object.hasOwn(table.props.rows[0], "severity"), false);
    const ui = serviceFixture(uiBuilt.outputFiles[0].text).api;
    const html = renderToStaticMarkup(react.createElement(ui.DataTable, table.props));
    assert.match(html, /Recorded Level/); assert.match(html, /Unavailable/); assert.doesNotMatch(html, /private|Low|bg-emerald/);
    assert.equal(component(page, "KpiCard").props.value, "Unavailable");
    assert.doesNotMatch(JSON.stringify(table.props.rows), /private/);
  } finally { f.cleanup(); }
});

test("actual camera summary consumer does not relabel an all-record count as today", () => {
  const f = workflowFixture({ source: pageBuilt.outputFiles[0].text });
  try {
    // API8c365703 counts company/nondeleted rows without a time predicate;
    // retain its existing wire key, not its misleading temporal label.
    const page = f.renderPage("dashcam", { summary: { ...f.view.summary, data: { dashcamEventsToday: 3 } } });
    const card = component(page, "KpiCard");
    assert.equal(card.props.label, "Stored event records");
    assert.equal(card.props.value, "3");
    assert.doesNotMatch(renderToStaticMarkup(card), /today/i);
  } finally { f.cleanup(); }
});

test("actual camera page treats malformed list as unavailable, never empty/healthy or exportable", async () => {
  const f = workflowFixture({ source: pageBuilt.outputFiles[0].text });
  try {
    for (const raw of [{}, null, "invalid", 7]) {
      const page = f.renderPage("dashcam", { rows: { ...f.view.rows, data: raw } });
      assert.equal(component(page, "DataTable"), undefined);
      const empty = component(page, "EmptyState"); assert.match(empty.props.title, /unavailable/); assert.match(empty.props.subtitle, /No empty or healthy/);
      assert.equal(component(page, "PageHeader"), undefined); assert.equal(f.blobs.length, 0);
    }
    const service = serviceFixture(); service.setResponse(response({ unexpected: true }, 200));
    await assert.rejects(() => service.api.dashcamApi.events(), /unavailable/);
  } finally { f.cleanup(); }
});

test("actual camera page uses direct manage grant, current detail admission and denies retained legacy actions", async () => {
  for (const permission of ["safety:update", "safety:review", "dashcam:manage", "dashcam.manage", "*"]) {
    const f = workflowFixture({ source: pageBuilt.outputFiles[0].text });
    try {
      const page = f.renderPage("dashcam", { session: { ...f.session, permissions: [permission] } });
      const create = button(component(page, "PageHeader").props.actions, "Record Event Metadata");
      const allowed = ["dashcam:manage", "dashcam.manage", "*"].includes(permission);
      assert.equal(create.props.disabled, !allowed); create.props.onClick();
      assert.equal(Boolean(component(f.renderPage(), "CameraMetadataDialog")), allowed);
      const drawer = component(page, "Drawer");
      for (const type of ["review", "falsePositive", "createCoaching", "createEvidencePackage", "createIncidentReport", "unknown"]) {
        assert.equal(drawer.props.canRunAction(type), false);
        drawer.props.onAction(type, record()); await f.api.runAction("dashcam", type, record());
      }
      assert.equal(f.calls.length, 0);
    } finally { f.cleanup(); }
  }
  const f = workflowFixture({ source: pageBuilt.outputFiles[0].text });
  try {
    let page = f.renderPage(); component(page, "DataTable").props.onSelect({ id: "19" }); page = f.renderPage();
    let drawer = component(page, "Drawer"); assert.equal(drawer.props.canUpdate, true);
    drawer.props.onEdit(f.view.detail.data.record); assert.ok(component(f.renderPage(), "CameraMetadataDialog"));
    f.renderPage("dashcam", { session: { ...f.session, permissions: [] } });
    drawer.props.onEdit(f.view.detail.data.record); assert.equal(component(f.renderPage(), "CameraMetadataDialog"), undefined);
    assert.equal(f.calls.length, 0);
  } finally { f.cleanup(); }
});

test("actual camera drawer never renders provider/private assessment fields or legacy action panels", () => {
  const f = workflowFixture({ source: pageBuilt.outputFiles[0].text });
  try {
    const props = { config: f.api.configs.dashcam, detail: { record: record({ sourceAuthority: "Authoritative", aiSummary: "private-ai", roadClipUrl: "private-url", falsePositive: true }), recommendations: [{ aiSummary: "private-recommendation" }], evidencePackages: [{ exportUrl: "private-export" }] },
      loading: false, canUpdate: false, canExport: true, actionPending: false, canRunAction: () => true,
      onClose() {}, onEdit() {}, onAction() { assert.fail("legacy action should not render"); }, cameraReady: true, cameraOpen: true };
    const drawer = f.api.Drawer(props); const html = renderToStaticMarkup(drawer);
    assert.match(html, /Stored provider authority — not verified in this view/); assert.match(html, /not assessed in this view/);
    assert.doesNotMatch(html, /private-|Evidence \/ Legal Readiness|Event Summary \/ Action|False Positive|Create Coaching|Create Evidence|Create Incident/);
    assert.equal(button(drawer, "Edit manual metadata").props.disabled, true);
    const unavailable = renderToStaticMarkup(f.api.Drawer({ ...props, cameraReady: false }));
    assert.match(unavailable, /Current metadata is unavailable/); assert.doesNotMatch(unavailable, /Stored provider authority|Manual note/);
  } finally { f.cleanup(); }
});

test("other four actual page kinds retain their configured fields/actions and service dispatch", async () => {
  const cases = [["safety", "review", /\/safety\/events\/19\/review$/, "Create Safety Event"], ["coaching", "assign", /\/coaching\/tasks\/19\/assign$/, "Create Coaching Task"],
    ["incidents", "status", /\/incidents\/19\/status$/, "Create Incident"], ["evidence", "lock", /\/evidence-packages\/19\/lock-package$/, "Create Evidence Package"]];
  for (const [kind, type, route, createLabel] of cases) {
    const f = workflowFixture({ source: pageBuilt.outputFiles[0].text });
    try {
      f.setResponse(response({}, 200)); const page = f.renderPage(kind, { session: { ...f.session, permissions: ["*"] } });
      const config = f.api.configs[kind]; assert.ok(config.actions.includes(type)); assert.ok(config.fields.length); assert.ok(config.sections.length);
      assert.ok(button(component(page, "PageHeader").props.actions, createLabel));
      assert.equal(component(page, "DataTable").props.rows[0].severity, "High");
      await f.api.runAction(kind, type, record(), { status: "Under Review" });
      assert.equal(f.calls.length, 1); assert.match(f.calls[0].url, route);
    } finally { f.cleanup(); }
  }
});

// These are actual shipped hooks/callbacks with controlled React hook storage,
// plus installed Query/Mutation observers. Not a React mount or browser session.
function workflowFixture({ source = workflowBuilt.outputFiles[0].text } = {}) {
  const slots = [], effects = [], mutations = [], observations = [], downloads = [], blobs = [], cleanups = [];
  let cursor = 0, resetCount = 0, lastMutationPromise;
  const client = new query.QueryClient({ defaultOptions: { queries: { retry: false, gcTime: Infinity, staleTime: Infinity }, mutations: { retry: false } } });
  client.mount();
  const peek = () => slots.find((slot) => slot?.current && Object.hasOwn(slot.current, "attempt"))?.current;
  const hooks = { ...react,
    useRef: (value) => { const index = cursor++; return slots[index] ??= { current: value }; },
    useState: (value) => { const index = cursor++; if (!Object.hasOwn(slots, index)) slots[index] = typeof value === "function" ? value() : value; return [slots[index], (next) => { slots[index] = typeof next === "function" ? next(slots[index]) : next; }]; },
    useCallback: (fn) => fn,
    useMemo: (fn) => fn(),
    useEffect: (fn) => { effects.push(fn); },
  };
  const useMutation = (options) => {
    const index = cursor++;
    if (!slots[index]) {
      const observer = new query.MutationObserver(client, options);
      slots[index] = observer; mutations.push(observer);
      cleanups.push(observer.subscribe((result) => { observations.push({ status: result.status, pending: Boolean(peek()?.attempt), editor: peek()?.editor }); }));
    }
    const observer = slots[index]; observer.setOptions(options);
    return { ...observer.getCurrentResult(), mutateAsync: (attempt) => { lastMutationPromise = observer.mutate(attempt); return lastMutationPromise; }, reset: () => { resetCount++; observer.reset(); } };
  };
  const document = { createElement: () => ({ click() { downloads.push(this); } }) };
  class FixtureUrl extends URL { static createObjectURL(blob) { blobs.push(blob); return "blob:camera-fixture"; } static revokeObjectURL() {} }
  let view;
  const f = serviceFixture(source, (name) => name === "react" ? hooks
    : name === "@tanstack/react-query" ? { ...query, useMutation, useQueryClient: () => client }
    : name === "camera-test-auth" ? { useAuth: () => ({ session: view.session }) }
    : name === "camera-test-queries" ? new Proxy({}, { get: (_, key) => () => String(key).includes("Summary") ? view.summary : String(key).includes("Detail") ? view.detail : view.rows })
    : require(name), document, FixtureUrl);
  const idle = { isError: false, isLoading: false, isFetching: false, fetchStatus: "idle", refetch: async () => {} };
  view = { enabled: true, session: f.session, canManage: true, canExport: true, selectedId: 19, visibleIds: ["19"],
    detail: { ...idle, data: { record: record() } }, rows: { ...idle, data: [record()] },
    summary: { ...idle, data: { dashcamEventsToday: 1 } }, queryClient: client };
  const render = (patch = {}) => { view = { ...view, ...patch }; cursor = 0; return f.api.useCameraMetadataWorkflow(view); };
  const activateReads = (read = async (key) => key.length === 1 ? [record()] : key[1] === "summary" ? { dashcamEventsToday: 1 } : { record: record() }) => {
    for (const key of [["dashcam"], ["dashcam", "summary"], ["dashcam", "detail", 19]]) {
      const observer = new query.QueryObserver(client, { queryKey: key, queryFn: () => read(key), initialData: key.length === 1 ? [record()] : key[1] === "summary" ? { dashcamEventsToday: 1 } : { record: record() }, staleTime: Infinity, retry: false });
      cleanups.push(observer.subscribe(() => {}));
    }
  };
  return { ...f, render, renderPage: (kind = "dashcam", patch = {}) => { view = { ...view, ...patch }; cursor = 0; return f.api.Batch4SafetyPage({ kind }); },
    get view() { return view; }, peek, mutations, observations, client, activateReads, downloads, blobs, effects,
    get lastMutationPromise() { return lastMutationPromise; }, get resetCount() { return resetCount; },
    cleanup: () => { cleanups.forEach((fn) => fn()); client.clear(); client.unmount(); } };
}
function edit(f) {
  let h = f.render(); h.open(f.view.detail.data.record); h = f.render();
  assert.ok(h.editor); h.change(h.editor, "title", "Changed"); return f.render();
}

test("actual workflow refuses stale/manual/permission/session admission and same-identity replacement", async () => {
  for (const patch of [{ canManage: false }, { session: null }, { session: { token: "x", user: {}, company: {}, permissions: [] } },
    { detail: { data: { record: record({ sourceAuthority: "Authoritative" }) }, isError: false, isLoading: false, isFetching: false, fetchStatus: "idle" } },
    { detail: { data: { record: record() }, isError: true, isLoading: false, isFetching: false, fetchStatus: "idle" } }]) {
    const f = workflowFixture(); try { const h = f.render(patch); h.open(f.view.detail.data.record); assert.equal(f.render().editor, null); assert.equal(f.calls.length, 0); } finally { f.cleanup(); }
  }
  for (const change of [f => ({ canManage: false }), f => ({ selectedId: 20 }), f => ({ session: { ...f.session } }),
    f => ({ detail: { ...f.view.detail, data: { record: { ...f.view.detail.data.record } } } }), f => ({ detail: { ...f.view.detail, fetchStatus: "paused" } })]) {
    const f = workflowFixture(); try {
      const h = edit(f), old = h.editor; f.render(change(f)); await h.submit(old);
      assert.equal(f.calls.length, 0);
    } finally { f.cleanup(); }
  }
});

test("actual single-flight keeps pending close/open/input/reset guarded until success release", async () => {
  const f = workflowFixture();
  try {
    const p = deferred(); f.setAdapter(async (config) => ({ ...await p.promise, config, headers: {}, statusText: "fixture" }));
    const h = edit(f), old = h.editor, resets = f.resetCount;
    const first = h.submit(old); void h.submit(old); h.close(old); h.open(); h.change(old, "title", "Rejected change");
    await settle();
    assert.equal(f.calls.length, 1); assert.equal(f.peek().editor, old); assert.equal(f.resetCount, resets); assert.equal(f.render().pending, true);
    p.resolve(response(receipt(), 200)); await first; await settle();
    const final = f.render(); assert.equal(final.pending, false); assert.equal(final.editor, null); assert.equal(final.notice.receipt.id, "19");
    const success = f.observations.filter((item) => item.status === "success");
    assert.ok(success.length); assert.equal(success.at(-1).pending, true); assert.equal(success.at(-1).editor, null);
    assert.equal(f.mutations[0].getCurrentResult().status, "success");
    const settledResets = f.resetCount;
    await h.submit(old); h.close(old); h.change(old, "title", "Old callback");
    assert.equal(f.calls.length, 1); assert.equal(f.resetCount, settledResets);
    final.open(f.view.detail.data.record); const reopened = f.render().editor; assert.notEqual(reopened, old);
    await h.submit(old); h.close(old); assert.equal(f.render().editor, reopened);
  } finally { f.cleanup(); }
});

async function successHandoffOracle(source = workflowBuilt.outputFiles[0].text) {
  const f = workflowFixture({ source });
  try {
    const p = deferred(); f.setAdapter(async (config) => ({ ...await p.promise, config, headers: {} }));
    const h = edit(f), old = h.editor, resets = f.resetCount, observations = [];
    const operation = h.submit(old); await settle();
    const stop = f.mutations[0].subscribe((result) => {
      if (result.status !== "success") return;
      // Observe first. Assertions inside a subscriber can be swallowed by
      // MutationObserver; all guarantees are checked after settlement below.
      observations.push({ pending: Boolean(f.peek().attempt), editor: f.peek().editor, resets: f.resetCount });
      h.close(old); h.open(); h.change(old, "title", "late change"); void h.submit(old);
      observations.at(-1).afterResets = f.resetCount;
    });
    p.resolve(response(receipt(), 200)); await operation; await settle(); stop();
    assert.equal(observations.length, 1); assert.equal(observations[0].pending, true, "success subscriber must still observe command ownership");
    assert.equal(observations[0].editor, null); assert.equal(observations[0].resets, resets); assert.equal(observations[0].afterResets, resets);
    assert.equal(f.mutations[0].getCurrentResult().status, "success");
    assert.equal(f.calls.length, 1); assert.equal(f.render().pending, false); assert.equal(f.render().editor, null);
  } finally { f.cleanup(); }
}
test("successful settlement seam asserts pending/reset ownership outside real mutation subscribers", () => successHandoffOracle());

test("retained success oracle detects an in-memory early-ownership-release negative control", async () => {
  const target = "state.current.editor = null; // Revoke retained callbacks before pending releases.";
  const source = readFileSync(resolve(root, "src/components/CameraMetadataDialog.tsx"), "utf8");
  assert.equal(source.split(target).length, 2);
  const mutant = await esbuild.build({
    stdin: { contents: 'export * from "@/components/CameraMetadataDialog"; export * from "@/services/dashcamApi"; export {apiClient} from "@/services/apiClient";', loader: "ts", resolveDir: root },
    bundle: true, platform: "node", format: "cjs", write: false, logLevel: "silent", alias: { "@": resolve(root, "src") },
    external: ["axios", "react", "react/jsx-runtime", "@tanstack/react-query", "lucide-react"], define: { "import.meta.env": "{}" },
    plugins: [{ name: "in-memory-admission-negative-control", setup(build) {
      build.onLoad({ filter: /CameraMetadataDialog\.tsx$/ }, () => ({ contents: source.replace(target, "state.current.attempt = null; " + target), loader: "tsx" }));
    } }],
  });
  await assert.rejects(() => successHandoffOracle(mutant.outputFiles[0].text), (error) => error.code === "ERR_ASSERTION" && error.message.includes("success subscriber must still observe command ownership"));
});

test("actual drawer then dedicated form order makes only topmost focus hook consume Escape", () => {
  const pageFixture = workflowFixture({ source: pageBuilt.outputFiles[0].text });
  try {
    let page = pageFixture.renderPage(); component(page, "DataTable").props.onSelect({ id: "19" }); page = pageFixture.renderPage();
    component(page, "Drawer").props.onEdit(pageFixture.view.detail.data.record); page = pageFixture.renderPage();
    const children = elements(page); assert.ok(children.indexOf(component(page, "Drawer")) < children.indexOf(component(page, "CameraMetadataDialog")));
  } finally { pageFixture.cleanup(); }

  const listeners = new Set(), dialogs = [], effects = [], hookSlots = new Map();
  let active = "", cursor = 0, closes = 0, changes = 0, submissions = 0, outerCloses = 0;
  const document = { activeElement: { isConnected: true, focus() { document.activeElement = this; } },
    querySelectorAll: () => dialogs, addEventListener: (_, fn) => listeners.add(fn), removeEventListener: (_, fn) => listeners.delete(fn) };
  const hooks = { ...react, useRef: (value) => { const slots = hookSlots.get(active); const index = cursor++; return slots[index] ??= { current: value }; },
    useEffect: (effect) => effects.push(effect) };
  const requireHooks = (name) => name === "react" ? hooks : require(name);
  const formModule = serviceFixture(workflowBuilt.outputFiles[0].text, requireHooks, document).api;
  const drawerModule = serviceFixture(pageBuilt.outputFiles[0].text, (name) => name === "camera-test-auth" ? {} : name === "camera-test-queries" ? {} : requireHooks(name), document).api;
  const initial = formModule.cameraRecord(record()).values;
  const editor = { id: "19", version: 3, initial, draft: { ...formModule.cameraDraft(initial), title: "Changed" } };
  const call = (name, fn) => { active = name; cursor = 0; if (!hookSlots.has(name)) hookSlots.set(name, []); return fn(); };
  const outer = call("drawer", () => drawerModule.Drawer({ config: drawerModule.configs.dashcam, detail: { record: record() }, cameraOpen: true, cameraReady: true,
    loading: false, canUpdate: true, canExport: true, actionPending: false, canRunAction: () => false, onClose: () => outerCloses++, onEdit() {}, onAction() {} }));
  const renderForm = (pending) => call("form", () => formModule.CameraMetadataDialog({ editor, pending, error: "", onClose: () => closes++, onChange: () => changes++, onSubmit: () => submissions++ }));
  let form = renderForm(true);
  const node = () => {
    const first = { hidden: false, isConnected: true, focus() { document.activeElement = this; } }, last = { hidden: false, isConnected: true, focus() { document.activeElement = this; } };
    return { isConnected: true, hidden: false, first, last, querySelector: () => null, querySelectorAll: () => [first, last], contains: (element) => [first, last].includes(element) };
  };
  dialogs.push(node(), node()); outer.props.ref.current = dialogs[0]; form.props.ref.current = dialogs[1];
  const stops = effects.splice(0).map((effect) => effect());
  const key = (value, shiftKey = false) => { const result = { key: value, shiftKey, prevented: 0, stopped: 0, preventDefault() { this.prevented++; }, stopPropagation() { this.stopped++; } }; [...listeners].forEach((fn) => fn(result)); return result; };
  assert.equal(document.activeElement, dialogs[1].first);
  assert.equal(key("Escape").prevented, 1); assert.equal(closes, 0); assert.equal(outerCloses, 0);
  const controls = elements(form).filter((element) => ["input", "select"].includes(element.type));
  assert.equal(controls.length, 9); controls.forEach((element) => element.props.onChange({ target: { value: "ignored" } }));
  elements(form).find((element) => element.type === "form").props.onSubmit({ preventDefault() {} }); button(form, "Cancel").props.onClick();
  assert.deepEqual([changes, submissions, closes], [0, 0, 0]); assert.equal(elements(form).find((element) => element.type === "fieldset").props.disabled, true);
  document.activeElement = dialogs[1].last; key("Tab"); assert.equal(document.activeElement, dialogs[1].first);
  key("Tab", true); assert.equal(document.activeElement, dialogs[1].last);
  form = renderForm(false); key("Escape"); assert.equal(closes, 1); assert.equal(outerCloses, 0);
  elements(form).find((element) => element.type === "form").props.onSubmit({ preventDefault() {} }); assert.equal(submissions, 1);
  const markup = renderToStaticMarkup(form); assert.match(markup, /current UTC recorded time/); assert.match(markup, /not an operator-observed occurrence/); assert.doesNotMatch(markup, /datetime-local|AI Confidence|Review Status/);
  stops.reverse().forEach((stop) => stop?.()); assert.equal(listeners.size, 0);
});

test("actual rejected handoff blocks retained submission before outer single-flight release", async () => {
  const f = workflowFixture();
  try {
    const p = deferred(); f.setAdapter(() => p.promise);
    const h = edit(f), old = h.editor;
    const first = h.submit(old);
    const seam = [];
    f.lastMutationPromise.catch(() => { seam.push(Boolean(f.peek().attempt)); void h.submit(old); });
    await settle(); p.reject({ response: { status: 409 } });
    await first; await settle();
    assert.deepEqual(seam, [true]); assert.equal(f.calls.length, 1); assert.equal(f.render().pending, false);
    assert.equal(f.render().editor, old); assert.match(f.render().error, /record changed/i);
  } finally { f.cleanup(); }
});

test("stale draft revision and session replacement cannot submit, overwrite or reveal prior acknowledgement", async () => {
  const f = workflowFixture();
  try {
    const h = edit(f), old = h.editor;
    h.change(old, "title", "New draft"); const newer = f.render().editor;
    await h.submit(old); h.close(old); assert.equal(f.render().editor, newer); assert.equal(f.calls.length, 0);
    const p = deferred(); f.setAdapter(async (config) => ({ ...await p.promise, config, headers: {}, statusText: "fixture" }));
    const operation = f.render().submit(newer); await settle();
    f.render({ session: { ...f.session } });
    p.resolve(response(receipt(), 200)); await operation; await settle();
    assert.equal(f.render().notice, null); assert.equal(f.render().editor, null); assert.equal(f.render().pending, false);
  } finally { f.cleanup(); }
});

test("known acknowledgement survives missing or failed reads; read-only recovery never resubmits", async () => {
  for (const failing of ["missing", "failure"]) {
    const f = workflowFixture();
    try {
      if (failing === "failure") f.activateReads(async () => { throw new Error("read unavailable"); });
      f.setResponse(response(receipt(), 200)); const h = edit(f); await h.submit(h.editor); await settle();
      let final = f.render(); assert.equal(final.notice.receipt.id, "19"); assert.equal(final.pending, false); assert.equal(final.warning, true);
      assert.equal(f.mutations[0].getCurrentResult().status, "success");
      const writes = f.calls.length;
      if (failing === "missing") f.activateReads();
      else for (const q of f.client.getQueryCache().getAll()) q.setOptions({ ...q.options, queryFn: async () => ({ fixture: true }), retry: false });
      await final.refresh(final.notice); await settle(); final = f.render();
      assert.equal(final.warning, false); assert.equal(final.refreshing, false); assert.equal(f.calls.length, writes);
    } finally { f.cleanup(); }
  }
});

test("old successful read completion cannot clear a newer target's warning or repeat a write", async () => {
  const f = workflowFixture();
  const nextClient = new query.QueryClient({ defaultOptions: { queries: { retry: false, gcTime: Infinity, staleTime: Infinity } } });
  const stops = [];
  try {
    const oldRead = deferred(); f.activateReads(() => oldRead.promise);
    f.setResponse(response(receipt(), 200)); let h = edit(f); await h.submit(h.editor); await settle();
    const oldNotice = f.render().notice; assert.equal(f.render().refreshing, true);
    for (const key of [["dashcam"], ["dashcam", "summary"], ["dashcam", "detail", 20]]) {
      const observer = new query.QueryObserver(nextClient, { queryKey: key, queryFn: async () => { throw new Error("new read unavailable"); }, initialData: {}, staleTime: Infinity, retry: false });
      stops.push(observer.subscribe(() => {}));
    }
    h = f.render({ selectedId: 20, detail: { ...f.view.detail, data: { record: record({ id: 20 }) } }, queryClient: nextClient });
    h.open(f.view.detail.data.record); h = f.render(); h.change(h.editor, "title", "Second target"); h = f.render();
    f.setResponse(response(receipt({ id: 20 }), 200)); await h.submit(h.editor); await settle();
    const newer = f.render().notice; assert.equal(newer.receipt.id, "20"); assert.equal(f.render().warning, true); assert.equal(f.render().refreshing, false);
    oldRead.resolve({ fixture: true }); await settle();
    assert.equal(f.render().notice, newer); assert.equal(f.render().warning, true); assert.equal(f.calls.length, 2);
    await f.render().refresh(oldNotice); assert.equal(f.calls.length, 2); assert.equal(f.render().notice, newer);
  } finally { stops.forEach((stop) => stop()); nextClient.clear(); f.cleanup(); }
});

test("actual camera page retains acknowledgement on a subsequent read-error path", async () => {
  const f = workflowFixture({ source: pageBuilt.outputFiles[0].text });
  try {
    let page = f.renderPage(); button(component(page, "PageHeader").props.actions, "Record Event Metadata").props.onClick();
    for (const [key, value] of Object.entries(input())) {
      const form = component(f.renderPage(), "CameraMetadataDialog"); form.props.onChange(form.props.editor, key, value);
    }
    const form = component(f.renderPage(), "CameraMetadataDialog"); form.props.onSubmit(form.props.editor); await settle();
    assert.equal(f.calls.length, 1);
    page = f.renderPage("dashcam", { rows: { ...f.view.rows, isError: true } });
    const html = renderToStaticMarkup(page); assert.match(html, /acknowledgement for record 19/); assert.match(html, /Display refresh is not confirmed/); assert.match(html, /unavailable/);
    assert.doesNotMatch(html, /durable commit confirmed|certified|PRODUCTION READY/);
  } finally { f.cleanup(); }
});

test("actual Query observers release already-paused and newly-paused reads without losing acknowledgement", async () => {
  for (const alreadyOffline of [true, false]) {
    const f = workflowFixture();
    try {
      const pauseRead = deferred();
      f.activateReads(alreadyOffline ? async () => ({ fixture: true }) : () => pauseRead.promise);
      if (alreadyOffline) query.onlineManager.setOnline(false);
      // Mutations stay online for admission; change connectivity only after the
      // real service acknowledgement, immediately before onSuccess refresh runs.
      query.onlineManager.setOnline(true);
      f.setAdapter(async (config) => { if (alreadyOffline) query.onlineManager.setOnline(false); return { ...response(receipt(), 200), config, headers: {} }; });
      const h = edit(f); const operation = h.submit(h.editor); await settle();
      if (!alreadyOffline) {
        query.onlineManager.setOnline(false);
        for (const q of f.client.getQueryCache().getAll()) q.setOptions({ ...q.options, retry: 1, retryDelay: 0 });
        pauseRead.reject(new Error("controlled read failure before offline retry"));
      }
      await operation; await settle(); await new Promise((resolve) => setTimeout(resolve, 5)); await settle();
      const final = f.render(); assert.equal(final.pending, false); assert.equal(final.notice.receipt.id, "19"); assert.equal(final.warning, true); assert.equal(final.refreshing, false);
      assert.equal(f.calls.length, 1); assert.equal(f.mutations[0].getCurrentResult().status, "success");
    } finally {
      // Resume controlled reads before disposing observers; cancellation of a
      // paused Query retryer is not an assertion about the write outcome.
      for (const q of f.client.getQueryCache().getAll()) q.setOptions({ ...q.options, queryFn: async () => ({ fixture: true }), retry: false });
      query.onlineManager.setOnline(true); await settle(); f.cleanup();
    }
  }
});

test("actual shared CSV sink sees only neutral whitelisted current fields and formula escaping", async () => {
  const f = workflowFixture();
  try {
    const contaminated = record({ title: "=SUM(1,2)", roadClipUrl: "private-url", aiSummary: "private-ai", providerPayloadHash: "private-hash", sourceAuthority: "Authoritative" });
    let h = f.render({ detail: { ...f.view.detail, data: { record: contaminated } }, rows: { ...f.view.rows, data: [contaminated] } });
    h.exportCurrent("list"); h.exportCurrent("detail");
    assert.equal(f.blobs.length, 2); assert.equal(f.downloads.length, 2);
    for (const blob of f.blobs) {
      const csv = await blob.text(); assert.match(csv, /'=SUM/); assert.doesNotMatch(csv, /private|rowVersion|sourceAuthority|deletedAt|recordedLevel/); assert.match(csv, /not provided or verified/);
    }
    for (const patch of [{ canExport: false }, { rows: { ...f.view.rows, data: {} } }, { rows: { ...f.view.rows, isError: true } }, { rows: { ...f.view.rows, fetchStatus: "paused" } }]) {
      h = f.render(patch); h.exportCurrent("list"); assert.equal(f.blobs.length, 2);
    }
    f.render({ detail: { ...f.view.detail, isError: true } }); h.exportCurrent("detail"); assert.equal(f.blobs.length, 2);
  } finally { f.cleanup(); }
});

test("UTC parser preserves representable instant, rejects calendar rollover and submillisecond loss", () => {
  const { cameraUtc } = serviceFixture().api;
  assert.equal(cameraUtc("2024-02-29T12:01:02.1230000Z"), "2024-02-29T12:01:02.123Z");
  assert.equal(cameraUtc("0001-01-01T00:00:00Z"), "0001-01-01T00:00:00.000Z");
  for (const value of ["0000-01-01T00:00:00Z", "2024-02-30T00:00:00Z", "2025-01-01T24:00:00Z", "2025-01-01T00:60:00Z", "2025-01-01T00:00:60Z", "2025-01-01T00:00:00+00:00", "2025-01-01T00:00:00.0010001Z"]) assert.equal(cameraUtc(value), null);
  const now = Date.UTC(2025, 0, 1);
  assert.equal(cameraUtc("2025-01-01T00:05:00Z", now), "2025-01-01T00:05:00.000Z");
  assert.equal(cameraUtc("2025-01-01T00:05:00.001Z", now), null);
});

test("manual admission uses own sourceAuthority, active marker and exact core identities", () => {
  const { cameraRecord, cameraProjection } = serviceFixture().api;
  assert.equal(cameraRecord(record()).manual, true);
  assert.equal(cameraRecord({ id: 19, row_version: "3", source_authority: "LegacyUnverified", deleted_at: null }).manual, true);
  for (const patch of [{ sourceAuthority: undefined }, { sourceAuthority: "ProviderPending" }, { sourceAuthority: "Authoritative" },
    { sourceAuthority: "Unknown" }, { sourceAuthority: {} }, { sourceAuthority: "" }, { deletedAt: "2025-01-01" },
    { deletedAt: undefined }, { rowVersion: Number.MAX_SAFE_INTEGER + 1 }]) assert.equal(cameraRecord(record(patch)).manual, false);
  const sourceEventOnly = record(); delete sourceEventOnly.sourceAuthority; sourceEventOnly.sourceEvent = "LegacyUnverified";
  assert.equal(cameraRecord(sourceEventOnly).manual, false);
  for (const patch of [{ source_authority: "Authoritative" }, { row_version: 4 }, { deleted_at: "2025-01-01" },
    { Id: 20 }, { id: Number.MAX_SAFE_INTEGER + 1 }, { driverId: true }, { driverId: Number.MAX_SAFE_INTEGER + 1 }, { vehicleId: [] }]) {
    assert.equal(cameraProjection(record(patch)), null);
  }
  assert.equal(cameraRecord(Object.create(record())), null);
  const projected = cameraProjection(record({ aiSummary: "private-ai", roadClipUrl: "private-url", providerPayloadHash: "private-hash", evidenceStatus: "Ready", unknown: ["private"] }));
  assert.doesNotMatch(JSON.stringify(projected), /private|sourceAuthority|rowVersion|deletedAt|evidenceStatus/);
  assert.match(cameraRecord(record({ sourceAuthority: "Authoritative" })).source, /not verified/);
});

test("draft update refuses normalized no-op and omits untouched high-precision timestamp", () => {
  const api = serviceFixture().api;
  const initial = api.cameraRecord(record({ occurredAt: "2025-01-01T12:00:00.1234567Z" })).values;
  const editor = { id: "19", version: 3, initial, draft: api.cameraDraft(initial) };
  assert.throws(() => api.cameraDraftPayload(editor), /No metadata changes/);
  assert.throws(() => api.cameraDraftPayload({ ...editor, draft: { ...editor.draft, title: " Manual note " } }), /No metadata changes/);
  assert.deepEqual(api.cameraDraftPayload({ ...editor, draft: { ...editor.draft, title: "Changed" } }), { rowVersion: 3, title: "Changed" });
  const ordinary = api.cameraRecord(record()).values;
  assert.throws(() => api.cameraDraftPayload({ ...editor, initial: ordinary, draft: { ...api.cameraDraft(ordinary), occurredAt: "2025-01-01T12:00:00Z" } }), /No metadata changes/);
  assert.deepEqual(api.cameraDraftPayload({ ...editor, draft: { ...editor.draft, driverId: "" } }), { rowVersion: 3, driverId: null });
  assert.deepEqual(api.cameraDraftPayload({ ...editor, initial: { ...initial, locationDescription: "Depot" }, draft: { ...editor.draft, locationDescription: "   " } }), { rowVersion: 3, locationDescription: null });
  for (const patch of [{ severity: " High " }, { driverId: "9007199254740992" }, { occurredAt: "bad" }]) assert.throws(() => api.cameraDraftPayload({ ...editor, draft: { ...editor.draft, ...patch } }));
});

test("actual write rejects malformed/status/identity receipts as unconfirmed without retry", async () => {
  const cases = [response(null), response([]), response(receipt({ id: 0 })), response(receipt({ id: Number.MAX_SAFE_INTEGER + 1 })),
    response(receipt({ rowVersion: "4" })), response(receipt({ rowVersion: false })), response(receipt({ rowVersion: Number.MAX_SAFE_INTEGER + 1 })),
    response(receipt({ dataSource: "provider" })), response(receipt({ mediaAvailable: "false" })), response(receipt({ extra: "private" })),
    response(receipt(), 200), { status: 201, data: { success: "true", data: receipt() } },
    { status: 201, data: { success: true, Success: false, data: receipt() } },
    { status: 201, data: { success: true, data: receipt(), Data: {} } },
    { status: 201, data: { success: undefined, data: receipt() } }, { status: 201, data: { success: true, data: undefined } },
    { status: 201, data: { data: receipt() } }, { status: 201, data: { success: true } },
    { status: 201, data: Object.create({ success: true, data: receipt() }) }];
  for (const candidate of cases) {
    const f = serviceFixture(); f.setResponse(candidate);
    await assert.rejects(() => f.api.dashcamApi.create(input(), f.session), (error) => error.kind === "unconfirmed" && !error.message.includes("private"));
    assert.equal(f.calls.length, 1);
  }
  const f = serviceFixture(); f.setResponse(response(receipt({ id: 20 }), 200));
  await assert.rejects(() => f.api.dashcamApi.update(19, { rowVersion: 3, title: "Changed" }, f.session), (error) => error.kind === "unconfirmed");
});

test("actual session interceptor rejects changed token/company/user or missing session before transport", async () => {
  for (const change of [session => ({ ...session, token: "replacement-fixture-token" }), session => ({ ...session, user: { id: 8 } }),
    session => ({ ...session, company: { id: 5 } }), () => null]) {
    const f = serviceFixture();
    const promise = f.api.dashcamApi.create(input(), f.session);
    const next = change(f.session);
    f.storage.clear(); if (next) f.storage.set("opstrax.session.v3", JSON.stringify({ session: next }));
    await assert.rejects(() => promise, (error) => error.kind === "session" && !/document|fixture-token/.test(error.message));
    assert.equal(f.calls.length, 0);
  }
});

test("service captures payload and session before Axios dispatch; failures remain privacy safe", async () => {
  const f = serviceFixture();
  const payload = input();
  const pending = f.api.dashcamApi.create(payload, f.session);
  payload.title = "Later mutation"; f.session.user.id = 99; f.session.token = "later-fixture-token";
  await pending;
  assert.equal(JSON.parse(f.calls[0].data).title, "Manual note");
  for (const status of [400, 403, 404, 409, 503, 500, undefined]) {
    const fixture = serviceFixture();
    fixture.setAdapter(async () => { throw { response: { status, data: { message: "private-provider-data" } } }; });
    await assert.rejects(() => fixture.api.dashcamApi.create(input(), fixture.session), (error) => !error.message.includes("private") && error.kind === ([400,403,404,409,503].includes(status) ? "rejected" : "unconfirmed"));
    assert.equal(fixture.calls.length, 1);
  }
});
