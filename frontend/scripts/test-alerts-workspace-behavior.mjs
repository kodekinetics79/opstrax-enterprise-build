import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { createRequire } from "node:module";
import { runInNewContext } from "node:vm";
import test from "node:test";
import axios from "axios";
import ts from "typescript";
import { renderToStaticMarkup } from "react-dom/server";

const require = createRequire(import.meta.url);
const filename = new URL("../src/pages/AlertsCenterPage.tsx", import.meta.url);
const source = readFileSync(filename, "utf8");
const tree = ts.createSourceFile(filename.pathname, source, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
const nodes = [];
function visit(node) { nodes.push(node); ts.forEachChild(node, visit); }
visit(tree);
function hasJsx(node) {
  let present = false;
  function inspect(child) {
    if (ts.isJsxElement(child) || ts.isJsxSelfClosingElement(child) || ts.isJsxFragment(child)) present = true;
    if (!present) ts.forEachChild(child, inspect);
  }
  inspect(node);
  return present;
}
const helpers = tree.statements.filter(node =>
  (ts.isFunctionDeclaration(node) && !hasJsx(node)) || ts.isVariableStatement(node),
).map(node => node.getText(tree)).join("\n");

// Execute production callbacks/initializers and server-render the actual inspector.
// These fixtures do not mount React, contact an API, or prove browser interactions.
function execute(body, bindings = {}) {
  const exports = {};
  const code = ts.transpileModule(`${helpers}\n${body}`, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, jsx: ts.JsxEmit.ReactJSX },
  }).outputText;
  runInNewContext(code, { exports, require, axios, Error, useMemo: factory => factory(), ...bindings }, { filename: filename.pathname });
  return exports.value;
}
function variable(name, bindings) {
  const node = nodes.find(candidate => ts.isVariableDeclaration(candidate) && candidate.name.getText(tree) === name);
  assert.ok(node?.initializer, `Production initializer ${name} must exist`);
  return execute(`export const value = ${node.initializer.getText(tree)};`, bindings);
}
function productionFunction(name, bindings) {
  const node = nodes.find(candidate => ts.isFunctionDeclaration(candidate) && candidate.name?.text === name);
  assert.ok(node, `Production function ${name} must exist`);
  return execute(`${node.getText(tree)}\nexport const value = ${name};`, bindings);
}
const now = Date.now();
function alert(id, changes = {}) {
  return { id, alertId: String(id), title: `Alert ${id}`, category: "Telematics", severity: "High", status: "Open", createdAt: new Date(now - id * 3_600_000).toISOString(), ...changes };
}
function filtered(alerts, changes = {}) {
  return variable("filtered", { alerts, categoryFilter: "All", statusFilter: "All", severityFilter: "All", search: "", sortOrder: "priority", ...changes });
}

test("Closed and case-insensitive combined filters retain only matching persisted records", () => {
  const open = alert(1);
  const closed = alert(2, { status: "Closed", entity: "BOX-102" });
  const otherCategory = alert(3, { status: "Closed", category: "Safety", entity: "BOX-102" });
  const rows = filtered([open, closed, otherCategory], { statusFilter: "Closed", categoryFilter: "Telematics", severityFilter: "High", search: "  box-102  " });
  assert.deepEqual(Array.from(rows, row => row.id), [closed.id]);
});

test("Priority sorting puts unresolved records before closed records and critical before high", () => {
  const rows = filtered([alert(1, { status: "Closed", severity: "Critical" }), alert(2), alert(3, { severity: "Critical" })]);
  assert.deepEqual(Array.from(rows, row => row.id), [3, 2, 1]);
});

test("Date sorts leave unavailable timestamps behind measured dates", () => {
  const records = [alert(1, { createdAt: undefined }), alert(2), alert(3, { createdAt: "not-a-date" }), alert(4)];
  assert.deepEqual(Array.from(filtered(records, { sortOrder: "newest" }), row => row.id), [2, 4, 1, 3]);
  assert.deepEqual(Array.from(filtered(records, { sortOrder: "oldest" }), row => row.id), [4, 2, 1, 3]);
});

test("Pagination clamps a stale page index and keeps the final matching record reachable", () => {
  const records = Array.from({ length: 26 }, (_, index) => alert(index + 1));
  const currentPage = variable("currentPage", { page: 99, filtered: records });
  const visibleRows = variable("visibleRows", { currentPage, filtered: records });
  assert.equal(currentPage, 1);
  assert.deepEqual(Array.from(visibleRows, row => row.id), [26]);
});

test("Zero-result filters clear selected context and close the mobile inspector", () => {
  const effect = nodes.find(node => ts.isCallExpression(node) && node.expression.getText(tree) === "useEffect" && node.arguments[0]?.getText(tree).includes("setSelectedAlert"));
  assert.ok(effect, "Production selection reconciliation effect must exist");
  const updates = [];
  const reconcile = execute(`export const value = ${effect.arguments[0].getText(tree)};`, {
    filtered: [], selectedAlert: alert(1),
    setSelectedAlert: value => updates.push(["selection", value]),
    setMobileDetail: value => updates.push(["mobile", value]),
  });
  reconcile();
  assert.deepEqual(updates, [["selection", null], ["mobile", false]]);
});

test("Selected context rebinds to the refreshed record rather than the stale selected object", () => {
  const stale = alert(1);
  const fresh = { ...stale, status: "Closed" };
  const current = variable("currentSelection", { selectedAlert: stale, filtered: [fresh] });
  assert.equal(current, fresh);
  assert.equal(current.status, "Closed");
});

function actionFixture(type, mutation) {
  const calls = [];
  const context = {
    actionType: type, actionAlert: alert(4), actionPending: false,
    setActionError: value => calls.push(["error", value]),
    setActionType: value => calls.push(["type", value]),
    setActionAlert: value => calls.push(["target", value]),
  };
  for (const name of ["acknowledge", "close", "task"]) context[`${name}Mutation`] = {
    mutateAsync: payload => { calls.push([name, payload]); return mutation(); },
  };
  return { calls, confirm: productionFunction("handleActionConfirm", context) };
}

test("Failed acknowledgement preserves the target and exposes an error", async () => {
  const fixture = actionFixture("acknowledge", async () => { throw new Error("fixture HTTP 500"); });
  await fixture.confirm({ note: "reviewed the recorded event" });
  assert.equal(fixture.calls.filter(call => call[0] === "acknowledge").length, 1);
  assert.equal(fixture.calls.some(call => call[0] === "type" || call[0] === "target"), false);
  assert.equal(fixture.calls.filter(call => call[0] === "error").at(-1)[1], "fixture HTTP 500 Your text is still here. Retry, or cancel and refresh the alert.");
});

test("Failed task creation shows the server message without dismissing entered text", async () => {
  const response = { status: 409, data: { message: "Tasks are unavailable for this tenant." } };
  const failure = new axios.AxiosError("Request failed with status code 409", "ERR_BAD_REQUEST", undefined, undefined, response);
  const fixture = actionFixture("task", async () => { throw failure; });
  const payload = { title: "Inspect recorded device failure" };
  await fixture.confirm(payload);
  assert.equal(fixture.calls.find(call => call[0] === "task")[1].payload, payload);
  assert.equal(fixture.calls.some(call => call[0] === "type" || call[0] === "target"), false);
  assert.equal(fixture.calls.filter(call => call[0] === "error").at(-1)[1], "Tasks are unavailable for this tenant. Your text is still here. Retry, or cancel and refresh the alert.");
});

test("Missing server error text falls back to actionable failure guidance", async () => {
  const failure = new axios.AxiosError("Network Error", "ERR_NETWORK");
  const fixture = actionFixture("close", async () => { throw failure; });
  await fixture.confirm({ resolution: "Recorded service complete" });
  assert.equal(fixture.calls.filter(call => call[0] === "error").at(-1)[1], "The action could not be saved. Your text is still here. Retry, or cancel and refresh the alert.");
  assert.equal(fixture.calls.some(call => call[0] === "type" || call[0] === "target"), false);
});

test("Close and task dialogs dismiss only after their owning mutation succeeds", async () => {
  for (const type of ["close", "task"]) {
    let release;
    const pending = new Promise(resolve => { release = resolve; });
    const fixture = actionFixture(type, () => pending);
    const completion = fixture.confirm(type === "close" ? { resolution: "service completed" } : { title: "Review device history" });
    assert.equal(fixture.calls.filter(call => call[0] === type).length, 1);
    assert.equal(fixture.calls.some(call => call[0] === "type" || call[0] === "target"), false);
    release();
    await completion;
    assert.deepEqual(fixture.calls.filter(call => call[0] === "type" || call[0] === "target"), [["type", null], ["target", null]]);
  }
});

test("An in-flight action cannot dispatch a second mutation", async () => {
  const confirm = productionFunction("handleActionConfirm", {
    actionType: "task", actionAlert: alert(4), actionPending: true,
    taskMutation: { mutateAsync() { assert.fail("Duplicate task dispatched while saving"); } },
  });
  await confirm({ title: "Review device history" });
});

function inspectorMarkup(changes = {}) {
  const record = alert(1, { status: "Closed" });
  const inspector = productionFunction("AlertInspector", { StatusBadge: ({ status }) => status });
  return renderToStaticMarkup(inspector({
    alert: record, detail: { alert: { ...record, status: "Open" }, tasks: [], auditTrail: [] },
    loading: false, failed: false, retry() {}, onAction() {}, onNavigate() {},
    canAcknowledge: true, canClose: true, ...changes,
  }));
}

function findElement(node, predicate) {
  if (!node || typeof node !== "object") return null;
  if (predicate(node)) return node;
  const children = Array.isArray(node.props?.children) ? node.props.children : [node.props?.children];
  for (const child of children) {
    const found = Array.isArray(child)
      ? child.map(entry => findElement(entry, predicate)).find(Boolean)
      : findElement(child, predicate);
    if (found) return found;
  }
  return null;
}

test("Related-module actions preserve the persisted entity route", () => {
  const navigations = [];
  const inspector = productionFunction("AlertInspector", { StatusBadge: ({ status }) => status });
  const route = "/vehicles?vehicleId=104";
  const element = inspector({ alert: alert(1, { entityRoute: route }), detail: { alert: null, tasks: [], auditTrail: [] }, loading: false, failed: false, retry() {}, canAcknowledge: false, canClose: false, onAction() {}, onNavigate: value => navigations.push(value) });
  const button = findElement(element, node => node.type === "button" && String(node.props.children).startsWith("Open related module"));
  assert.ok(button);
  button.props.onClick();
  assert.deepEqual(navigations, [route]);
});

test("Action dialogs send the owning endpoint's note, resolution, or task-title payload", () => {
  for (const [type, note, expected] of [
    ["acknowledge", "Reviewed source event", { note: "Reviewed source event" }],
    ["close", "Vehicle serviced", { resolution: "Vehicle serviced" }],
    ["task", "", { title: "Follow-up: Alert 4" }],
  ]) {
    let submitted;
    const modal = productionFunction("ActionModal", {
      useState: () => [note, () => {}], useDialogFocus: () => ({ current: null }), X: () => null,
    });
    const element = modal({ type, alert: alert(4), pending: false, error: null, onClose() {}, onConfirm: payload => { submitted = payload; } });
    const label = type === "acknowledge" ? "Acknowledge" : type === "close" ? "Close alert" : "Create task";
    const confirm = findElement(element, node => node.type === "button" && node.props.children === label);
    assert.ok(confirm);
    confirm.props.onClick();
    assert.deepEqual(JSON.parse(JSON.stringify(submitted)), expected);
  }
});

test("Inspector lifecycle actions use the refreshed queue status instead of stale cached detail", () => {
  const html = inspectorMarkup();
  assert.doesNotMatch(html, />Acknowledge<|>Close alert</);
  assert.match(html, />Closed</);
});

test("Inspector hides privileged actions for read-only users", () => {
  const html = inspectorMarkup({ alert: alert(1), canAcknowledge: false, canClose: false });
  assert.doesNotMatch(html, />Acknowledge<|>Create task<|>Close alert</);
});

test("Loading and failed detail requests do not claim zero tasks or history", () => {
  for (const state of [{ loading: true }, { failed: true }]) {
    const html = inspectorMarkup(state);
    assert.doesNotMatch(html, /No follow-up tasks recorded|No activity history recorded/);
    assert.match(html, /Loading linked tasks|could not be loaded/);
  }
});
