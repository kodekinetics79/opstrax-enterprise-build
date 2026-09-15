import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { createRequire } from "node:module";
import { runInNewContext } from "node:vm";
import test from "node:test";
import { renderToStaticMarkup } from "react-dom/server";
import ts from "typescript";
import { connectorAttemptHealth } from "../src/lib/connectorFreshness.ts";

const require = createRequire(import.meta.url);
function production(file, helpers) {
  const source = readFileSync(new URL(`../src/pages/${file}`, import.meta.url), "utf8");
  const tree = ts.createSourceFile(file, source, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
  const functions = name => {
    const node = tree.statements.find(candidate => ts.isFunctionDeclaration(candidate) && candidate.name?.text === name);
    assert.ok(node, `Production function ${name} must exist`);
    return node;
  };
  const helperSource = helpers.map(name => functions(name).getText(tree)).join("\n");
  function evaluate(body, bindings) {
    const exports = {};
    const code = ts.transpileModule(`${helperSource}\n${body}`, { compilerOptions: {
      module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, jsx: ts.JsxEmit.ReactJSX,
    } }).outputText;
    runInNewContext(code, { exports, require, ...bindings });
    return exports.value;
  }
  return {
    fn: (name, bindings = {}) => evaluate(`${functions(name).getText(tree)}\nexport const value = ${name};`, bindings),
    initializer(name, bindings) {
      let found;
      function visit(node) {
        if (ts.isVariableDeclaration(node) && node.name.getText(tree) === name) found = node;
        ts.forEachChild(node, visit);
      }
      visit(functions("MaintenanceQueue"));
      assert.ok(found?.initializer, `Production MaintenanceQueue initializer ${name} must exist`);
      return evaluate(`export const value = ${found.initializer.getText(tree)};`, bindings);
    },
    pageCallback(label, bindings) {
      let callback;
      function visit(node) {
        if (ts.isJsxOpeningElement(node) && node.tagName.getText(tree) === "button") {
          const attributes = node.attributes.properties.filter(ts.isJsxAttribute);
          const name = attributes.find(attribute => attribute.name.getText(tree) === "aria-label")?.initializer;
          const onClick = attributes.find(attribute => attribute.name.getText(tree) === "onClick")?.initializer;
          if (name && ts.isStringLiteral(name) && name.text === label && onClick && ts.isJsxExpression(onClick)) callback = onClick.expression;
        }
        ts.forEachChild(node, visit);
      }
      visit(functions("MaintenanceQueue"));
      assert.ok(callback, `Production paging callback ${label} must exist`);
      return evaluate(`export const value = ${callback.getText(tree)};`, bindings);
    },
    source,
  };
}
const integrations = production("IntegrationsPage.tsx", ["effectiveIntegrationStatus", "formatRelativeTime", "ConnectorPill"]);
const maintenance = production("MaintenanceCommandPage.tsx", ["queueTitle", "queueCode", "queueOrigin", "queuePriority", "fmtDate", "fmtDateTime"]);
const sharedUi = production("../components/ui.tsx", []);
for (const [status, tone] of [["Connected", "emerald"], ["Disconnected", "slate"], ["Error", "red"], ["Pending", "amber"]]) {
  test(`Actual StatusBadge renders ${status} with its ${tone} semantic tone`, () => {
    const badge = sharedUi.fn("StatusBadge");
    for (const value of [status, status.toLowerCase()]) {
      const element = badge({ status: value });
      const html = renderToStaticMarkup(element);
      assert.match(element.props.className, new RegExp(`text-${tone}-`));
      assert.match(html, new RegExp(`>${status}<`), "the owning status label remains human readable");
      if (status === "Disconnected" || status === "Error") assert.doesNotMatch(element.props.className, /emerald/, "unavailable or failed connectivity must not signal a healthy connection");
    }
  });
}
const decoration = {
  StatusBadge: ({ status }) => status, RiskBadge: ({ risk }) => risk,
  RefreshCw: () => null, Plug: () => null, Zap: () => null, Pencil: () => null, Trash2: () => null,
};
function find(node, predicate) {
  if (!node || typeof node !== "object") return null;
  if (predicate(node)) return node;
  const children = Array.isArray(node.props?.children) ? node.props.children : [node.props?.children];
  for (const child of children.flat()) { const match = find(child, predicate); if (match) return match; }
  return null;
}
function content(node) {
  if (node == null || typeof node === "boolean") return "";
  if (Array.isArray(node)) return node.map(content).join("");
  if (typeof node !== "object") return String(node);
  return content(node.props?.children);
}
function button(element, label) { return find(element, node => node.type === "button" && content(node).trim() === label); }
function connector(changes = {}, props = {}) {
  const calls = [];
  const record = { id: 104, key: "samsara", name: "Samsara", logo: "SAM", category: "Telematics & ELD", description: "Persisted provider account", connectedTo: ["Fleet", "Safety"], adapterAvailable: true, status: "Connected", lastTestOk: true, lastTestedAt: new Date().toISOString(), ...changes };
  const row = integrations.fn("ConnectorRow", { ...decoration, connectorAttemptHealth });
  const handlers = Object.fromEntries(["Connect", "Disconnect", "Sync", "Test", "Configure", "Edit", "Delete"].map(action => [`on${action}`, () => calls.push(action)]));
  return { calls, element: row({ integration: record, canManage: true, busy: false, testing: false, ...handlers, ...props }) };
}

// Run actual source callbacks/initializers and component server markup with
// controlled dependencies. No React mount, browser, provider or API evidence.
test("Stored Connected without handshake remains pending and cannot Sync or Disconnect", () => {
  const fixture = connector({ lastTestOk: null, lastTestedAt: null });
  assert.match(renderToStaticMarkup(fixture.element), /Pending/);
  assert.equal(button(fixture.element, "Sync now"), null);
  assert.equal(button(fixture.element, "Disconnect"), null);
  button(fixture.element, "Authorize").props.onClick();
  assert.deepEqual(fixture.calls, ["Connect"]);
});
test("Unavailable and read-only connector rows expose details without privileged operations", () => {
  for (const [record, props, label] of [[{ adapterAvailable: false }, {}, "View evaluation status"], [{}, { canManage: false }, "View details"]]) {
    const fixture = connector(record, props);
    for (const action of ["Sync now", "Connect", "Authorize", "Test connection", "Disconnect", "Delete connector"]) assert.equal(button(fixture.element, action), null);
    button(fixture.element, label).props.onClick();
    assert.deepEqual(fixture.calls, ["Configure"]);
  }
});
test("Verified connector actions call their owning handlers and pending controls are disabled", () => {
  const fixture = connector({ isCustom: true });
  for (const label of ["Sync now", "Test connection", "Disconnect", "Edit connector", "Delete connector"]) button(fixture.element, label).props.onClick();
  assert.deepEqual(fixture.calls, ["Sync", "Test", "Disconnect", "Edit", "Delete"]);
  const pending = connector({ isCustom: true }, { busy: true, testing: true });
  for (const label of ["Sync now", "Test connection", "Disconnect", "Delete connector"]) assert.equal(button(pending.element, label).props.disabled, true);
  assert.equal(find(fixture.element, node => node.type === "details").props.open, undefined, "secondary operations remain collapsed by default");
});
test("Failed handshake offers Reconnect and linked systems and stable sync evidence remain visible", () => {
  const fixture = connector({ lastTestOk: false });
  const html = renderToStaticMarkup(fixture.element);
  assert.match(html, /Error/);
  assert.match(html, /Fleet/); assert.match(html, /Safety/);
  assert.equal(button(fixture.element, "Sync now"), null);
  button(fixture.element, "Reconnect").props.onClick();
  assert.deepEqual(fixture.calls, ["Connect"]);
  const awaiting = connector();
  assert.match(renderToStaticMarkup(awaiting.element), /role="status"[^>]*>Connector sync status: awaiting first sync attempt\./);
});
test("Connector sync time distinguishes no recorded success from an unavailable recorded timestamp", () => {
  assert.match(renderToStaticMarkup(connector({ lastSyncAt: null }).element), /<strong>Never<\/strong>/);
  assert.match(renderToStaticMarkup(connector({ lastSyncAt: "invalid" }).element), /<strong[^>]*>Unavailable<\/strong>/);
});

function record(id, changes = {}) { return { id, rowVersion: 7, status: "Open", vehicleCode: `BOX-${id}`, title: `Work ${id}`, defectDescription: `Defect ${id}`, severity: "Major", priority: "Medium", createdAt: new Date(1_000_000 + id * 1000).toISOString(), ...changes }; }
function filtered(rows, changes = {}) { return maintenance.initializer("filtered", { rows, kind: "defect", statusFilter: "All", query: "", sort: "priority", ...changes }); }
function inspect(kind, changes = {}, props = {}) {
  const selected = record(114, changes), calls = [];
  const component = maintenance.fn("MaintenanceInspector", decoration);
  return { selected, calls, element: component({ record: selected, kind, canManage: true, canClose: true, actionPending: false, onAck: value => calls.push(["ack", value]), onResolve: value => calls.push(["resolve", value]), onComplete: value => calls.push(["complete", value]), ...props }) };
}
test("Maintenance combined filters retain canonical records and priority brings out-of-service work first", () => {
  const rows = [record(1), record(2, { outOfService: true }), record(3, { status: "Resolved", severity: "Critical" })];
  assert.deepEqual(Array.from(filtered(rows), value => value.id), [2, 3, 1]);
  assert.deepEqual(Array.from(filtered(rows, { statusFilter: "Resolved", query: "box-3" }), value => value.id), [3]);
});
test("Maintenance date sorting leaves undated records behind recorded dates", () => {
  const rows = [record(1, { createdAt: undefined }), record(2), record(3, { createdAt: "invalid" }), record(4)];
  assert.deepEqual(Array.from(filtered(rows, { sort: "oldest" }), value => value.id), [2, 4, 1, 3]);
  assert.deepEqual(Array.from(filtered(rows, { sort: "recent" }), value => value.id), [4, 2, 1, 3]);
});
test("Maintenance pagination clamps stale pages and selection uses the refreshed canonical row", () => {
  const rows = Array.from({ length: 16 }, (_, index) => record(index + 1));
  const currentPage = maintenance.initializer("currentPage", { page: 99, pageCount: 2 });
  const visible = maintenance.initializer("visible", { filtered: rows, preview: false, currentPage, pageSize: 15 });
  assert.equal(currentPage, 2);
  assert.deepEqual(Array.from(visible, value => value.id), [16]);
  const selected = maintenance.initializer("selected", { filtered: rows, selectedId: "16", preview: false, visible });
  assert.equal(selected, rows[15]);
  assert.equal(selected.rowVersion, 7);
  assert.equal(maintenance.initializer("selected", { filtered: [], selectedId: null, preview: false, visible: [] }), null);
});
test("Both maintenance paging directions release selection from the previous visible page", () => {
  for (const [label, page] of [["Previous maintenance page", 1], ["Next maintenance page", 3]]) {
    const updates = [];
    const callback = maintenance.pageCallback(label, { currentPage: 2, setPage: value => updates.push(["page", value]), setSelectedId: value => updates.push(["selection", value]) });
    callback();
    assert.deepEqual(updates, [["page", page], ["selection", null]]);
  }
});
test("Maintenance inspector actions preserve identity/version and independent manage/close permissions", () => {
  const manager = inspect("defect", {}, { canClose: false });
  button(manager.element, "Acknowledge").props.onClick();
  assert.equal(manager.calls[0][1], manager.selected);
  assert.equal(manager.calls[0][1].rowVersion, 7);
  assert.equal(button(manager.element, "Resolve defect"), null);
  const closer = inspect("defect", {}, { canManage: false });
  assert.equal(button(closer.element, "Acknowledge"), null);
  button(closer.element, "Resolve defect").props.onClick();
  assert.equal(closer.calls[0][1], closer.selected);
  const pending = inspect("defect", {}, { actionPending: true });
  assert.equal(button(pending.element, "Acknowledging…").props.disabled, true);
  const readonly = inspect("work-order", {}, { canClose: false });
  assert.equal(button(readonly.element, "Complete work order"), null);
});
test("Terminal maintenance records cannot repeat Resolve or Complete irrespective of status case", () => {
  for (const status of ["Resolved", "RESOLVED", "rejected"]) assert.equal(button(inspect("defect", { status }).element, "Resolve defect"), null);
  for (const status of ["Completed", "COMPLETED", "cancelled"]) assert.equal(button(inspect("work-order", { status }).element, "Complete work order"), null);
});
test("Maintenance context preserves provenance and recorded notes without inventing cost currency", () => {
  const fixture = inspect("work-order", { recordOrigin: "seeded_synthetic_database", description: "", notes: "Repair report retained", serviceNotes: "Technician service detail retained", estimatedCost: 0 });
  const html = renderToStaticMarkup(fixture.element);
  assert.match(html, /Demo Data/);
  assert.match(html, /Repair report retained/);
  assert.match(html, /Technician service detail retained/);
  assert.match(html, /currency not recorded/);
  assert.match(html, /Actual cost<\/dt><dd>Not recorded/);
  assert.doesNotMatch(html, /SAR|USD|\$/);
  const unknown = renderToStaticMarkup(inspect("work-order", { recordOrigin: "unknown_database_record" }).element);
  assert.match(unknown, /Unverified DB Record/);
});

assert.match(maintenance.source, /record: AnyRecord\) => maintenanceApi\.acknowledgeDefect\(Number\(record\.id\), Number\(record\["rowVersion"\] \?\? record\["row_version"\]\)\)/, "acknowledgement wiring retains the optimistic concurrency version");
assert.match(integrations.source, /<ConnectorRow[\s\S]*?onSync=\{\(\) => syncMut\.mutate\(integration\.id\)\}/, "row Sync wiring retains exact connector identity");
