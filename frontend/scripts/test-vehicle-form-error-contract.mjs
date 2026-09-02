import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { apiErrorMessage } from "../src/utils/apiErrorMessage.ts";

const fallback = "The vehicle could not be saved. Please try again.";
const invalidVin = "VIN must contain exactly 17 characters.";
assert.equal(apiErrorMessage({
  message: "Request failed with status code 400",
  response: { status: 400, data: { success: false, message: "Vehicle validation failed", errors: [invalidVin] } },
}, fallback), `Vehicle validation failed: ${invalidVin}`);

const duplicate = "Vehicle code 'W1CERT-20260831-001' already exists in this fleet.";
assert.equal(apiErrorMessage({
  message: "Request failed with status code 409",
  response: { status: 409, data: { message: "Vehicle validation failed", errors: [duplicate] } },
}, fallback), `Vehicle validation failed: ${duplicate}`);

assert.equal(apiErrorMessage({ response: { data: {
  message: "Vehicle validation failed", errors: ["Correct year.", "Correct odometer."],
  details: "private connection diagnostics", stack: "private stack",
} } }, fallback), "Vehicle validation failed: Correct year. Correct odometer.");
for (const data of [null, "upstream HTML", { details: "private diagnostics" },
  { message: "Request failed with status code 500", errors: ["failure\n at Internal.save (private:1)"] }]) {
  assert.equal(apiErrorMessage({ message: "Request failed with status code 500", response: { data } }, fallback), fallback);
}
assert.equal(apiErrorMessage(new Error("Request failed with status code 400"), fallback), fallback);

const page = readFileSync(new URL("../src/pages/VehiclesPage.tsx", import.meta.url), "utf8");
assert.match(page, /serverError=\{save\.error \? apiErrorMessage\(save\.error,/,
  "vehicle save failures must render the safe API rejection envelope");
assert.doesNotMatch(page, /serverError=\{save\.error instanceof Error \? save\.error\.message/);
assert.match(page, /apiErrorMessage\(actionError,/, "the roster must not repeat generic Axios status text");
const form = page.slice(page.indexOf("function VehicleFormModal("));
assert.match(form, /role="alert" aria-live="assertive"/, "the correction message must be announced");
assert.match(page, /onSuccess: async \(\) => \{ setEditing\(null\)/,
  "the form must only close automatically after a successful save");
assert.match(page, /onClose=\{\(\) => \{ if \(save\.isPending\) return; save\.reset\(\); setEditing\(null\); setIsCreating\(false\); \}\}/,
  "dismissal must block while saving, then clear settled errors before another form can open");
assert.match(form, /aria-label="Close" disabled=\{saving\}/, "Close must visibly disable during save");
assert.match(form, /disabled=\{saving\} onClick=\{onClose\}[^>]*>Cancel/, "Cancel must visibly disable during save");
assert.match(form, /max-h-\[calc\(100dvh-2rem\)\]/,
  "the form must fit the dynamic viewport with its outer padding");
assert.match(form, /overflow-y-auto overscroll-contain/,
  "long mobile forms need their own scroll container so footer controls remain reachable");
console.log("Vehicle form error contract passed.");
