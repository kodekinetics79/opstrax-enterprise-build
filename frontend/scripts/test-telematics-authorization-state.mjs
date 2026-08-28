import assert from "node:assert/strict";
import fs from "node:fs";

const page = fs.readFileSync(new URL("../src/pages/TelematicsCommandPage.tsx", import.meta.url), "utf8");

assert.match(page, /requiredViewPermission: PERMISSIONS\.TELEMATICS_DIAGNOSTICS_VIEW/, "diagnostics declares its read boundary");
assert.match(page, /const canView = hasPermission\(config\.requiredViewPermission\)/, "the page uses the semantic read policy shared by the route and API");
assert.match(page, /enabled: canView,[\s\S]*staleTime: 20_000/, "known-forbidden roles do not issue the cluster query");
assert.match(page, /!canView \|\| \(recordsQ\.isError && isForbidden\(recordsQ\.error\)\)/, "a server 403 also resolves to the intentional restricted state");
assert.match(page, /role="status"[\s\S]*\{config\.title\} access restricted[\s\S]*not available for the current role/, "the restriction is visible, accessible, and useful");

const restrictedState = page.slice(page.indexOf("if (!canView ||"), page.indexOf("if (recordsQ.isLoading)"));
assert.doesNotMatch(restrictedState, /apiErrorMessage|recordsQ\.refetch|Retry|Missing permission|config\.requiredViewPermission/, "the restricted state has no retry or raw permission detail");

console.log("Telematics negative-authorization UX contract passed.");
