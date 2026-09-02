import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { fileURLToPath } from "node:url";

const root = resolve(fileURLToPath(new URL("..", import.meta.url)));
const page = readFileSync(resolve(root, "src/pages/CustomerPortalPage.tsx"), "utf8");

assert.match(page, /name="feedback-shipment"[\s\S]*aria-label="Shipment for feedback"/);
assert.match(page, /name="feedback-rating"[\s\S]*aria-label="Feedback rating"/);
assert.match(page, /name="feedback-subject"[\s\S]*aria-label="Feedback subject"[\s\S]*autoComplete="off"/);
assert.match(page, /name="feedback-comment"[\s\S]*aria-label="Feedback details"[\s\S]*autoComplete="off"/);

console.log("Customer portal feedback controls expose stable accessible names and autofill boundaries.");
