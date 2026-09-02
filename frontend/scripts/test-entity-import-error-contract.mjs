import assert from "node:assert/strict";
import { createRequire } from "node:module";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, resolve } from "node:path";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const require = createRequire(import.meta.url);
const esbuild = require("esbuild");
const built = await esbuild.build({
  stdin: {
    contents: 'export { importErrorMessage } from "@/utils/importErrorMessage";',
    resolveDir: root,
    loader: "ts",
  },
  bundle: true,
  format: "esm",
  write: false,
  alias: { "@": resolve(root, "src") },
  logLevel: "silent",
});
const { importErrorMessage } = await import(
  "data:text/javascript;base64," + Buffer.from(built.outputFiles[0].text).toString("base64")
);

const rowError = "Import stopped at row 2 ('CLHQ-TRL-0001'): Asset has active custody; status must remain 'InUse' until it is checked in. No rows were changed.";
assert.equal(
  importErrorMessage({ response: { data: { success: false, message: rowError, details: "private diagnostic" } } }, "fallback"),
  rowError,
  "the API envelope's safe row-level message must be shown verbatim",
);
assert.equal(
  importErrorMessage({ response: { data: { error: rowError } } }, "fallback"),
  rowError,
  "the alternate { error } rejection contract must be supported",
);
assert.equal(
  importErrorMessage({ response: { data: { errors: ["Correct branchCode and retry."] } } }, "fallback"),
  "Correct branchCode and retry.",
  "the first safe envelope validation error must be supported",
);
assert.equal(
  importErrorMessage({ message: "Request failed with status code 400", response: { data: { details: "stack/private" } } }, "Safe fallback"),
  "Safe fallback",
  "generic HTTP text and diagnostic-only payloads must use the safe fallback",
);
assert.equal(
  importErrorMessage(new Error("failure\n at InternalImport.commit (secret.ts:42)"), "Safe fallback"),
  "Safe fallback",
  "stack-shaped details must never render in the customer modal",
);

const wizard = readFileSync(resolve(root, "src/components/EntityImportExport.tsx"), "utf8");
const commit = wizard.slice(wizard.indexOf("const commit = async"), wizard.indexOf("const previewRows"));
assert.match(commit, /setError\(importErrorMessage\(/, "commit failures must use the safe server-envelope extractor");
assert.doesNotMatch(commit, /catch[\s\S]*setStep\("select"\)/, "commit failure must preserve preview state for correction/retry");
assert.match(wizard, /role="alert" aria-live="assertive"/, "the handled rejection must be announced in the visible modal");

console.log("Entity import error contract passed.");
