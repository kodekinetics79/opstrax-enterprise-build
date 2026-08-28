import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { fileURLToPath } from "node:url";

const root = resolve(fileURLToPath(new URL("..", import.meta.url)));
const loginPage = readFileSync(resolve(root, "src/pages/LoginPage.tsx"), "utf8");

// Standards-compliant field metadata lets Chrome/password managers associate
// the tenant login identity and current password without app-specific guessing.
assert.match(loginPage, /name="organization"[\s\S]*autoComplete="organization"/);
assert.match(loginPage, /name="username"[\s\S]*autoComplete="username"/);
assert.match(loginPage, /name="password"[\s\S]*autoComplete="current-password"/);
assert.match(loginPage, /<form[\s\S]*autoComplete="on"/);

// The identifier-first second step must keep a standards-compliant username in
// the same form as the password, while remaining non-interactive and read-only.
assert.match(loginPage, /ref=\{emailRef\}[\s\S]*name="username"[\s\S]*readOnly[\s\S]*tabIndex=\{-1\}/);

// Chrome can visibly fill a controlled input without emitting React's change
// event. Detection must be bounded and event-assisted, and must never log or
// persist the credential.
assert.match(loginPage, /const syncBrowserFilledFields = useCallback/);
assert.match(loginPage, /passwordRef\.current\?\.value/);
assert.match(loginPage, /setPassword\(\(current\) => current === nextPassword/);
assert.match(loginPage, /setInterval\(syncBrowserFilledFields, 200\)/);
assert.match(loginPage, /setTimeout\(\(\) => window\.clearInterval\(interval\), 2_000\)/);
assert.match(loginPage, /onInputCapture=\{syncBrowserFilledFields\}/);
assert.doesNotMatch(loginPage, /console\.(?:log|debug|info|warn|error)\([^\n]*(?:password|nextPassword)/i);
assert.doesNotMatch(loginPage, /localStorage[^\n]*(?:password|nextPassword)/i);
assert.doesNotMatch(loginPage, /sessionStorage[^\n]*(?:password|nextPassword)/i);

console.log("Login browser-autofill behavior contract passed.");
