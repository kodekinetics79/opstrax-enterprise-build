import assert from "node:assert/strict";
import { createRequire } from "node:module";
import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { renderToStaticMarkup } from "react-dom/server";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const require = createRequire(import.meta.url);
const esbuild = require("esbuild");
const source = readFileSync(resolve(root, "src/pages/Batch4SafetyPage.tsx"), "utf8");
const inspectable = source
  .replace("function CoachingCompleteModal(", "export function CoachingCompleteModal(")
  .replace("  const coachingCompleteDialogRef = useDialogFocus<HTMLDivElement>(true, onClose);", "  const coachingCompleteDialogRef = null;")
  .replace('  const [completionNote, setCompletionNote] = useState("");', '  const completionNote = "Synthetic observed outcome"; const setCompletionNote = () => {};')
  .replace('  const [afterSafetyScore, setAfterSafetyScore] = useState("");', '  const afterSafetyScore = "0"; const setAfterSafetyScore = () => {};');
const built = await esbuild.build({
  stdin: { contents: inspectable, resolveDir: root, loader: "tsx" },
  bundle: true, platform: "node", format: "cjs", jsx: "automatic", write: false,
  nodePaths: process.env.NODE_PATH ? [process.env.NODE_PATH] : [],
  alias: { "@": resolve(root, "src") }, define: { "import.meta.env": "{}" }, logLevel: "silent",
});
const module = { exports: {} };
new Function("require", "module", "exports", built.outputFiles[0].text)(require, module, module.exports);
const { coachingCompletionAdmission, coachingCompletionModalAdmission, runCoachingCompletionAction, CoachingCompleteModal } = module.exports;
const ready = { selectedId: 17, isLoading: false, isFetching: false, isError: false };
const valid = { id: 17, status: "Driver Acknowledged", driverAcknowledged: true, acknowledgedAt: "2026-09-01T00:00:00Z" };
let cases = 0;
for (const status of ["Driver Acknowledged", "Escalated", " escalated "]) {
  assert.equal(coachingCompletionAdmission({ ...valid, status }, ready).allowed, true); cases++;
}
for (const driverAcknowledged of [false, null, undefined, "false", "true", 0, 1]) {
  assert.equal(coachingCompletionAdmission({ ...valid, driverAcknowledged }, ready).allowed, false); cases++;
}
for (const acknowledgedAt of [null, undefined, "", " ", "not-a-date", 0, {}, []]) {
  assert.equal(coachingCompletionAdmission({ ...valid, acknowledgedAt }, ready).allowed, false); cases++;
}
for (const status of ["Assigned", "Draft", "Completed", "Cancelled", null]) {
  assert.equal(coachingCompletionAdmission({ ...valid, status }, ready).allowed, false); cases++;
}
for (const state of [{ isLoading: true }, { isFetching: true }, { isError: true }, { selectedId: 18 }, { selectedId: undefined }]) {
  assert.equal(coachingCompletionAdmission(valid, { ...ready, ...state }).allowed, false); cases++;
}
assert.equal(coachingCompletionAdmission(undefined, ready).allowed, false); cases++;
assert.match(source, /coachingCompletionAdmission\(coachingDetailData\?\.record as AnyRecord \| undefined/);
assert.match(source, /const coachingDetailData = kind === "coaching" \? currentCoachingDetail\(\) : undefined/);
assert.match(source, /isLoading: detail\.isLoading, isFetching: detail\.isFetching, isError: detail\.isError/);
assert.match(source, /if \(type === "complete" && !coachingCompletionAccess\.allowed\) return false/);
assert.match(source, /const current = currentCoachingActionRecord\(type\);\s*if \(!current\) return;\s*row = current/);
assert.match(source, /if \(!canMutate\(type === "edit" \? updatePermission : ACTION_PERMISSIONS\.coaching\[type\] \|\| updatePermission\)\) return undefined/);
assert.match(source, /else if \(kind === "coaching" && type === "complete"\) \{ void actionSingleFlight\(async \(\) => \{ runCoachingCompletionAction\(coachingCompletionAdmission\(row, \{ selectedId: coachingDetailOwner\.current\.id, isLoading: false, isFetching: false, isError: false \}\), \(\) => setCoachingCompleteAction\(row\)\)/);
assert.match(source, /runCoachingCompletionAction\(coachingCompletionModalAccess, \(\) => \{ void actionSingleFlight/);
assert.match(source, /unavailableReason=\{coachingCompletionModalAccess\.reason\}/);
assert.match(source, /coachingCompletionModalAdmission\(coachingCompletionAccess, coachingCompleteAction\?\.id, selected\?\.id\)/);
let operations = 0;
for (const [modalId, selectedId, allowed] of [[17, 17, true], [17, 18, false], [undefined, 17, false], [17, undefined, false]]) {
  const admission = coachingCompletionModalAdmission({ allowed: true }, modalId, selectedId);
  assert.equal(admission.allowed, allowed);
  const before = operations;
  runCoachingCompletionAction(admission, () => operations++);
  assert.equal(operations - before, allowed ? 1 : 0); cases++;
}
runCoachingCompletionAction(coachingCompletionModalAdmission({ allowed: false, reason: "Unavailable" }, 17, 17), () => { throw new Error("Denied completion dispatched"); }); cases++;

function submitHandler(element) {
  if (!element || typeof element !== "object") return undefined;
  if (element.type === "form") return element.props.onSubmit;
  for (const child of [element.props?.children].flat()) { const found = submitHandler(child); if (found) return found; }
}
let submissions = 0;
for (const unavailableReason of ["A recorded driver acknowledgement is required before completion.", "Coaching details must finish loading successfully before completion.", undefined]) {
  const before = submissions;
  const modal = CoachingCompleteModal({ saving: false, error: null, unavailableReason, onClose() {}, onSubmit(payload) { submissions++; assert.equal(payload.afterSafetyScore, 0); } });
  const html = renderToStaticMarkup(modal);
  if (unavailableReason) {
    assert.match(html, /role="status"/);
    assert.match(html, /aria-disabled="true"/);
    assert.ok(html.includes(unavailableReason));
  } else assert.match(html, /aria-disabled="false"/);
  submitHandler(modal)({ preventDefault() {} });
  assert.equal(submissions - before, unavailableReason ? 0 : 1); cases++;
}
console.log(`Coaching completion acknowledgement: ${cases} local contract cases passed; no browser or real-driver evidence claimed.`);
