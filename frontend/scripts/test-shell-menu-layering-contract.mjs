import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { execFileSync } from "node:child_process";
import { createRequire } from "node:module";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const require = createRequire(import.meta.url);
const ts = require("typescript");
const arg = process.argv[2];
assert.ok(!arg || /^--source-ref=[a-f0-9]{40}$/.test(arg), "optional baseline requires full SHA");
const read = path => readFileSync(resolve(root, path), "utf8");
const shell = arg ? execFileSync("git", ["show", `${arg.slice(13)}:frontend/src/layouts/AppShell.tsx`], { cwd: resolve(root, ".."), encoding: "utf8" }) : read("src/layouts/AppShell.tsx");
const parse = (source, name) => ts.createSourceFile(name, source, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
const shellAst = parse(shell, "AppShell.tsx");
const moduleConfig = read("src/modules/moduleConfig.ts");
const moduleConfigAst = parse(moduleConfig, "moduleConfig.ts");
const unwrap = node => {
  while (node && (ts.isAsExpression(node) || ts.isSatisfiesExpression(node))) node = node.expression;
  return node;
};
const variableInitializer = (ast, name) => {
  let result;
  const visit = node => {
    if (ts.isVariableDeclaration(node) && node.name.getText() === name) result = unwrap(node.initializer);
    ts.forEachChild(node, visit);
  };
  visit(ast);
  return result;
};
const stringProperty = (object, name) => {
  const property = object.properties.find(entry => ts.isPropertyAssignment(entry) && entry.name.getText().replace(/["']/g, "") === name);
  return property && ts.isStringLiteral(property.initializer) ? property.initializer.text : undefined;
};
const modulesArray = variableInitializer(moduleConfigAst, "modules");
const navigationArray = variableInitializer(shellAst, "NAV_SECTIONS");
assert.ok(ts.isArrayLiteralExpression(modulesArray) && ts.isArrayLiteralExpression(navigationArray));
const moduleKeys = modulesArray.elements.filter(ts.isObjectLiteralExpression).map(object => stringProperty(object, "key")).filter(Boolean);
const navigationKeys = new Set(navigationArray.elements.filter(ts.isObjectLiteralExpression).flatMap(section => {
  const items = section.properties.find(entry => ts.isPropertyAssignment(entry) && entry.name.getText().replace(/["']/g, "") === "items");
  return items && ts.isArrayLiteralExpression(items.initializer) ? items.initializer.elements.filter(ts.isStringLiteral).map(entry => entry.text) : [];
}));
assert.deepEqual(moduleKeys.filter(key => !navigationKeys.has(key)), [], "every declared customer-facing module remains reachable from role/entitlement-filtered navigation");
function openings(ast) {
  const nodes = [];
  function visit(node) { if (ts.isJsxOpeningElement(node) || ts.isJsxSelfClosingElement(node)) nodes.push(node); ts.forEachChild(node, visit); }
  visit(ast); return nodes;
}
const attr = (node, name) => node.attributes.properties.find(p => ts.isJsxAttribute(p) && p.name.getText() === name)?.initializer;
const classes = node => { const value = attr(node, "className"); return value && ts.isStringLiteral(value) ? value.text.split(/\s+/) : []; };
const has = (node, value) => classes(node).includes(value);
const layer = node => {
  const values = classes(node).map(c => /^z-(?:\[(\d+)\]|(\d+))$/.exec(c)).filter(Boolean);
  assert.equal(values.length, 1, "selected surface has one literal layer");
  return Number(values[0][1] ?? values[0][2]);
};
const shellNodes = openings(shellAst);
const headers = shellNodes.filter(n => n.tagName.getText() === "header" && has(n, "shell-header"));
assert.equal(headers.length, 1);
const header = headers[0];
assert.ok(has(header, "relative"), "header has explicit positioned stacking context");
const sticky = openings(parse(read("src/pages/VehiclesModulePage.tsx"), "VehiclesModulePage.tsx")).find(n => n.tagName.getText() === "nav" && has(n, "sticky"));
assert.ok(sticky);
const headerLayer = layer(header), stickyLayer = layer(sticky);
const vehicleOverlays = openings(parse(read("src/pages/VehiclesPage.tsx"), "VehiclesPage.tsx")).filter(n => has(n, "fixed") && has(n, "inset-0"));
const confirmOverlay = openings(parse(read("src/components/ConfirmDialog.tsx"), "ConfirmDialog.tsx")).find(n => has(n, "fixed") && has(n, "inset-0"));
const mobileOverlay = shellNodes.find(n => has(n, "fixed") && has(n, "inset-0") && has(n, "xl:hidden"));
assert.ok(vehicleOverlays.length >= 3 && confirmOverlay && mobileOverlay);
assert.ok(headerLayer > stickyLayer, "header context above observed sticky navigation");
assert.ok(headerLayer < Math.min(...[...vehicleOverlays, confirmOverlay, mobileOverlay].map(layer)), "header remains below drawer/mobile/modal contexts");

const popovers = shellNodes.filter(n => has(n, "panel") && has(n, "top-full") && has(n, "right-0"));
assert.equal(popovers.length, 2, "only the profile and notification anchored shell popovers");
for (const panel of popovers) {
  let parent = panel.parent, withinHeader = false, anchoredWrapper = false;
  while (parent) {
    if (ts.isJsxElement(parent)) {
      if (parent.openingElement === header) withinHeader = true;
      const wrapperRef = attr(parent.openingElement, "ref")?.getText();
      if (["{profileRef}", "{notifRef}"].includes(wrapperRef)) anchoredWrapper = has(parent.openingElement, "relative");
    }
    parent = parent.parent;
  }
  assert.ok(withinHeader && anchoredWrapper, "popover remains in header and its own relative trigger wrapper");
  const style = attr(panel, "style");
  assert.ok(style && ts.isJsxExpression(style) && style.expression && ts.isObjectLiteralExpression(style.expression));
  const position = style.expression.properties.find(p => ts.isPropertyAssignment(p) && p.name.getText().replace(/["']/g, "") === "position");
  assert.ok(position && ts.isStringLiteral(position.initializer));
  assert.equal(position.initializer.text, "absolute", "inline absolute defeats normal unlayered panel position");
  assert.ok(has(panel, "absolute") && layer(panel) === 50, "existing anchored utility/internal layer retained");
}

// Declared-context model, not a browser CSS cascade or hit-test engine. A child's
// z-index cannot escape its ancestor context; this catches the old 20-vs-20 tie.
function compareStacks(a, b) {
  for (let i = 0; i < Math.min(a.length, b.length); i++) if (a[i] !== b[i]) return Math.sign(a[i] - b[i]);
  return 0;
}
assert.equal(compareStacks([headerLayer, 50], [stickyLayer]), 1);
assert.equal(compareStacks([headerLayer, 50], [layer(mobileOverlay)]), -1);
assert.equal(compareStacks([20, 9999], [20]), 0, "raising only child layer does not resolve equal parent context");
const panelRule = read("src/styles/index.css").match(/\.panel,\s*\.glass\s*\{([^}]+)\}/)?.[1];
assert.ok(panelRule, "known incumbent panel rule remains inspectable");
assert.doesNotMatch(panelRule, /position\s*:[^;]*!important/, "important global position would defeat normal inline fix");
assert.match(shell, /onClick=\{\(\) => \{ void logout\(\)\.finally\(\(\) => setProfileOpen\(false\)\); \}\}/);
assert.match(shell, /navigate\("\/settings"\); setProfileOpen\(false\)/);
assert.match(shell, /navigate\("\/user-management"\); setProfileOpen\(false\)/);
assert.match(shell, /const canViewSettings = hasPermission\(PERMISSIONS\.SETTINGS_VIEW\)/, "profile Settings visibility follows its route permission");
assert.match(shell, /const canViewUserManagement = \[[\s\S]*PERMISSIONS\.USERS_VIEW,[\s\S]*PERMISSIONS\.ROLES_VIEW,[\s\S]*PERMISSIONS\.SETTINGS_VIEW,[\s\S]*\]\.some\(hasDirectPermission\)/, "profile User Management visibility follows the route's direct grants");
assert.match(shell, /\{canViewSettings && \([\s\S]*navigate\("\/settings"\)/, "restricted users are not shown Settings");
assert.match(shell, /\{canViewUserManagement && \([\s\S]*navigate\("\/user-management"\)/, "restricted users are not shown User Management");
assert.match(shell, /onClick=\{\(\) => setNotifOpen\(\(v\) => !v\)\}/);
assert.match(shell, /profileRef\.current && !profileRef\.current\.contains\(e\.target as Node\)/);
assert.match(shell, /notifRef\.current && !notifRef\.current\.contains\(e\.target as Node\)/);
assert.match(shell, /label: "Safety",[\s\S]*items: \[[\s\S]*"safety-center", "dashcam", "incidents"[\s\S]*"evidence-packages", "traffic-violations"/, "customer-facing safety routes remain reachable from the sidebar");
assert.match(moduleConfig, /key: "dashcam",\s+title: "Camera Metadata",\s+route: "\/dashcam"/, "the camera metadata route has an honest customer-facing label");
assert.match(moduleConfig, /Stored, unverified camera event metadata\. Media, provider connectivity and automated assessments are unavailable\./, "navigation copy does not advertise unavailable video or provider capability");
assert.match(shell, /ref=\{profileTriggerRef\}[\s\S]*aria-label="Open account menu"[\s\S]*aria-expanded=\{profileOpen\}[\s\S]*aria-haspopup="dialog"[\s\S]*aria-controls="tenant-account-menu"/, "account trigger exposes popup state and relationship");
assert.match(shell, /id="tenant-account-menu"[\s\S]*role="dialog"[\s\S]*aria-label="Account menu"/, "account popup exposes an accessible dialog name");
assert.match(shell, /event\.key !== "Escape"[\s\S]*setProfileOpen\(false\)[\s\S]*profileTriggerRef\.current\?\.focus\(\)/, "Escape closes the account popup and restores trigger focus");
console.log("Shell menu visibility, positioning/stacking and handler contracts passed (static AST/model only; no browser geometry, hit-test, auth or accessibility claim).");
