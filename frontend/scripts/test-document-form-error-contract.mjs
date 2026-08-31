import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { apiErrorMessage } from "../src/utils/apiErrorMessage.ts";

// Bounded regression for the observed document upload correction journey.
// Helper execution + source wiring, not a mounted React/browser/persistence test.
const fallback = "The document could not be saved. Review the fields and try again.";
const reversed = "Document expiry date cannot be before issued date.";
const invalidIssued = "Document issued date is invalid.";
const invalidExpiry = "Document expiry date is invalid.";
const rejection = (data, status = 400) => ({
  message: `Request failed with status code ${status}`,
  response: { status, data },
});

// Multipart upload uses a direct message; JSON metadata edits use an errors array.
for (const message of [reversed, invalidIssued, invalidExpiry]) {
  assert.equal(apiErrorMessage(rejection({ success: false, message }), fallback), message);
  assert.equal(apiErrorMessage(rejection({
    success: false, message: "Document validation failed", errors: [message],
  }), fallback), `Document validation failed: ${message}`);
}
assert.equal(apiErrorMessage(rejection({
  message: "Document validation failed", errors: [invalidIssued, invalidExpiry],
  details: "PRIVATE DATABASE DETAILS", stack: "PRIVATE SERVER STACK",
}), fallback), `Document validation failed: ${invalidIssued} ${invalidExpiry}`);

// Do not expose unstructured upstream bodies or diagnostic-only fields.
// This does not assert blanket redaction of every possible server message.
for (const data of [null, "<html>PRIVATE UPSTREAM ERROR</html>",
  { details: "PRIVATE DATABASE DETAILS", stack: "PRIVATE SERVER STACK" },
  { message: "Request failed with status code 500", errors: ["Failure\n at Internal.save (private:1)"] },
  { message: " ", errors: [null, {}, " ", "x".repeat(1201)] }]) {
  assert.equal(apiErrorMessage(rejection(data, 500), fallback), fallback);
}
assert.equal(apiErrorMessage(new Error("Request failed with status code 400"), fallback), fallback);
assert.equal(apiErrorMessage(undefined, fallback), fallback);
console.log("Document API error envelope behavior passed.");

const page = readFileSync(new URL("../src/pages/Batch3OperationsPage.tsx", import.meta.url), "utf8");
const documentEditorCall = page.match(/<DocumentEditor\b[^\n]+/u)?.[0];
assert.ok(documentEditorCall, "the dedicated document editor wiring must be found");
assert.match(documentEditorCall,
  /error=\{save\.isError\s*\?\s*apiErrorMessage\(save\.error,/u,
  "document save errors must use the handled API envelope instead of generic Axios status text");
assert.match(page, /import\s*\{\s*apiErrorMessage\s*\}\s*from\s*["']@\/utils\/apiErrorMessage["']/u,
  "the form must reuse the existing error helper");
const modalCall = page.match(/<RecordModal\b[^\n]+/u)?.[0];
assert.ok(modalCall, "the non-document workflow RecordModal must remain wired");
assert.match(modalCall, /\(save\.error as Error\)\?\.message/u,
  "the bounded documents fix must preserve the other workflow error branch");
assert.match(page, /onSuccess:\s*async[\s\S]{0,400}setEditing\(null\);\s*await invalidate\(\);/u,
  "successful saves retain their existing close path");
const saveMutation = page.slice(page.indexOf("const save = useMutation("), page.indexOf("const action = useMutation("));
assert.doesNotMatch(saveMutation, /onError\s*:/u,
  "a rejection must not introduce a close/reset callback that discards correction input");
const form = readFileSync(new URL("../src/components/DocumentEditor.tsx", import.meta.url), "utf8");
assert.match(form, /\{error\s*\|\|\s*localError\s*\?\s*<p role="alert"[^>]*>\{localError\s*\|\|\s*error\}<\/p>/u,
  "the rejection must render as React text in the existing accessible alert");
assert.doesNotMatch(form, /dangerouslySetInnerHTML/u,
  "server text must not become executable/raw HTML");
assert.match(form, /useState<AnyRecord>\(initial\)/u,
  "the correction form retains its local input state");
assert.match(form, /onSave\(documentPayload\(form,\s*intent,\s*reason,\s*replaceQueue\)\)/u,
  "correction resubmits only the explicit document payload");
console.log("Document form error contract passed.");
