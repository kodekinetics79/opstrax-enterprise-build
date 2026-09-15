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
assert.match(page, /responseStatus\(invoicesQ\.error\) === 403/);
assert.match(page, /responseStatus\(jobsQ\.error\) === 403/);
assert.match(page, /portalAccessDeniedOnly[\s\S]*One or more portal data sets could not be loaded/);
assert.match(page, /invoiceAccessDenied \? "Requires customer-linked account" : "Load failed"/);
assert.match(page, /shipmentAccessDenied \? "Requires customer-linked account" : "Load failed"/);

console.log("Customer portal accessibility and truthful failure-state contracts verified.");
