import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import ts from "typescript";
import { consolidateNavigation, navigationRouteActive, splitNavigationItems } from "../src/utils/navigationPresentation.ts";

const item = (key, route = `/${key}`) => ({ key, route, title: key, description: `${key} workflow` });
const aliases = consolidateNavigation([item("load-bookings"), item("jobs"), item("shipments"), item("sales-pipeline"), item("opportunities")]);
assert.equal(aliases.length, 3);
assert.equal(aliases[0].route, "/jobs");
assert.match(aliases[0].navigationSearch, /load-bookings/);
assert.ok(navigationRouteActive(aliases[0], "/load-bookings"));
assert.ok(!navigationRouteActive(aliases[0], "/jobs-other"));
assert.ok(navigationRouteActive(aliases[0], "/jobs/12/"));
// If the canonical route is not entitled, retain only the accessible alias.
const restricted = consolidateNavigation([item("load-bookings")]);
assert.equal(restricted[0].route, "/load-bookings");
assert.deepEqual(restricted[0].navigationRoutes, ["/load-bookings"]);
assert.ok(!restricted[0].navigationSearch.includes("/jobs"));
assert.equal(consolidateNavigation([item("cold-chain"), item("fleet-cold-chain"), item("map-view"), item("gps-tracking")]).length, 4, "distinct workflows remain separate");
const tools = Array.from({ length: 10 }, (_, index) => item(`tool-${index}`));
const { primary, more } = splitNavigationItems(tools, "/tool-8");
assert.equal(primary.length, 7, "active secondary tool is visible without opening More tools");
assert.ok(primary.includes(tools[8]));
assert.equal(new Set([...primary, ...more]).size, tools.length);

// Audit all declared navigation keys, including entries merged at presentation time.
const read = path => readFileSync(new URL(path, import.meta.url), "utf8");
function array(source, name) {
  const ast = ts.createSourceFile("source.tsx", source, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
  let value;
  const visit = node => {
    if (ts.isVariableDeclaration(node) && node.name.getText() === name) {
      value = node.initializer;
      while (ts.isAsExpression(value) || ts.isSatisfiesExpression(value)) value = value.expression;
    }
    ts.forEachChild(node, visit);
  };
  visit(ast); return value;
}
const property = (node, name) => node.properties.find(p => ts.isPropertyAssignment(p) && p.name.getText() === name)?.initializer;
const declared = array(read("../src/modules/moduleConfig.ts"), "modules").elements.map(node => property(node, "key").text);
const sections = array(read("../src/layouts/AppShell.tsx"), "NAV_SECTIONS").elements;
const navigation = sections.flatMap(node => property(node, "items").elements.map(key => key.text));
assert.deepEqual([...navigation].sort(), [...declared].sort(), "every module is assigned exactly once");
assert.equal(new Set(navigation).size, navigation.length, "no repeated menu entries across sections");
console.log(`Navigation audit passed: ${declared.length} modules, ${sections.length} groups, two verified duplicate destinations consolidated; access-filtered aliases and active secondary pages remain reachable.`);
