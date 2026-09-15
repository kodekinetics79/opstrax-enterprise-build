import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { createRequire } from "node:module";
import { runInNewContext } from "node:vm";
import test from "node:test";
import { renderToStaticMarkup } from "react-dom/server";
import ts from "typescript";

const require = createRequire(import.meta.url);
const source = readFileSync(new URL("../src/components/WorkspaceGuidance.tsx", import.meta.url), "utf8");
const tree = ts.createSourceFile("WorkspaceGuidance.tsx", source, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
const component = tree.statements.find(node => ts.isFunctionDeclaration(node) && node.name?.text === "WorkspaceGuidance");
assert.ok(component, "the production guidance component must exist");
const code = ts.transpileModule(`${component.getText(tree)}\nexport const value = WorkspaceGuidance;`, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, jsx: ts.JsxEmit.ReactJSX },
}).outputText;

// Controlled hooks exercise actual component callbacks and server markup only;
// this is not a mounted React, browser, viewport or API evidence test.
function fixture(id) {
  let expanded = false;
  const exports = {};
  runInNewContext(code, {
    exports, require, useId: () => id,
    useState: () => [expanded, update => { expanded = typeof update === "function" ? update(expanded) : update; }],
    ArrowRight: () => null, CircleHelp: () => null, ChevronDown: () => null,
  });
  return { render: props => exports.value(props) };
}
function find(node, predicate) {
  if (!node || typeof node !== "object") return null;
  if (predicate(node)) return node;
  const children = Array.isArray(node.props?.children) ? node.props.children : [node.props?.children];
  for (const child of children.flat()) { const match = find(child, predicate); if (match) return match; }
  return null;
}

test("Workflow guidance preserves the visible next step and toggles the supplied ordered steps", () => {
  const workflow = fixture("workflow-test-1");
  const props = { nextStep: "Select a persisted alert to inspect it.", steps: ["Filter the current records.", "Review the selected message.", "Close records workflow completion only."] };
  let element = workflow.render(props);
  assert.match(renderToStaticMarkup(element), /Select a persisted alert to inspect it\./);
  assert.equal(find(element, node => node.type === "ol"), null, "secondary help is collapsed initially");
  let button = find(element, node => node.type === "button");
  assert.equal(button.props["aria-expanded"], false);
  assert.equal(button.props["aria-controls"], "workflow-test-1");
  assert.ok(button.props.title.length > 0, "help explains its purpose");
  button.props.onClick();
  element = workflow.render(props);
  button = find(element, node => node.type === "button");
  const list = find(element, node => node.type === "ol");
  assert.equal(button.props["aria-expanded"], true);
  assert.equal(list.props.id, button.props["aria-controls"]);
  assert.deepEqual(Array.from(list.props.children, item => find(item, node => node.type === "p").props.children), props.steps, "all owning workflow steps remain intact and ordered");
  button.props.onClick();
  assert.equal(find(workflow.render(props), node => node.type === "ol"), null, "help can close without hiding the next step");
});

test("Guidance does not infer readiness and separates the controls of multiple workflows", () => {
  const first = fixture("workflow-test-a"), second = fixture("workflow-test-b");
  const props = { nextStep: "Evidence is unavailable.", steps: ["Collect actual evidence before release."] };
  for (const workflow of [first, second]) {
    const element = workflow.render(props);
    assert.match(renderToStaticMarkup(element), /Evidence is unavailable\./);
    assert.doesNotMatch(renderToStaticMarkup(element), /Certified|Ready for release|0 records/);
    find(element, node => node.type === "button").props.onClick();
  }
  assert.notEqual(find(first.render(props), node => node.type === "ol").props.id, find(second.render(props), node => node.type === "ol").props.id);
});
