import assert from "node:assert/strict";
import axios from "axios";
import {
  documentExport,
  documentPayload,
  documentScore,
  documentVersion,
  presentDocument,
  previewDocumentDate,
} from "../src/utils/documentLifecycle.ts";
import {
  consumeRequestSessionGuard,
  createRequestSessionGuard,
  enforceRequestSessionGuard,
  SessionChangedBeforeRequestError,
  sessionBoundRequest,
} from "../src/auth/requestSessionGuard.ts";

for (const version of ["1", "4294967295"]) assert.equal(documentVersion(version), version);
for (const version of [0, "0", "01", " 1", "4294967296", null, undefined]) {
  assert.throws(() => documentVersion(version), /current version is unavailable/u);
}

const assessment = {
  status: "Expired", riskScore: 90, renewalStatus: "Renewal Required",
  recommendedAction: "Renew document", assessmentDate: "2026-08-31", policyVersion: "expiry-utc-30d-v1",
};
const automatic = presentDocument({
  id: 7, lifecycleMode: "automatic", status: "Active", riskScore: 0,
  renewalStatus: "Current", recommendedAction: "Stored action", currentDateAssessment: assessment,
});
assert.equal(automatic.displayedState, "Expired");
assert.equal(automatic.assessmentScore, 90);
const manual = presentDocument({
  id: 8, lifecycleMode: "manual", status: "Active", riskScore: 0,
  renewalStatus: "Current", recommendedAction: "Recorded override", currentDateAssessment: assessment,
});
assert.equal(manual.displayedState, "Active");
assert.equal(manual.assessmentScore, 0, "a recorded zero must not become Unknown");
assert.equal(documentScore(null), "Unknown");

const exported = documentExport({ ...manual, documentNumber: "D-8", rowVersion: "99", fileUrl: "private" });
assert.equal(Object.keys(exported).length, 23, "the existing CSV helper accepts at most 24 columns");
assert.equal(exported.storedStatus, "Active");
assert.equal(exported.currentDateStatus, "Expired");
assert.ok(!Object.hasOwn(exported, "rowVersion") && !Object.hasOwn(exported, "fileUrl"));

const create = documentPayload({
  title: "Policy", documentNumber: "D-1", entityType: "vehicle", entityId: "9",
  file: { name: "policy.pdf" }, status: "Expired", rowVersion: "123", lifecycleMode: "manual",
}, "manual", "must not escape", true);
assert.deepEqual(Object.keys(create).sort(), ["documentNumber", "entityId", "entityType", "file", "title"]);

const preserved = documentPayload({ id: 8, rowVersion: "22", title: "Retained", expiresAt: "" }, "preserve", "", false);
assert.deepEqual(preserved, { title: "Retained", expiresAt: "", expectedVersion: "22", lifecycleIntent: "preserve" });
const automaticUpdate = documentPayload({ id: 8, rowVersion: "23", expiresAt: "2026-09-30" }, "automatic", "Reassess from source date", true);
assert.equal(automaticUpdate.expectedVersion, "23");
assert.equal(automaticUpdate.lifecycleIntent, "automatic");
assert.equal(automaticUpdate.replaceQueuedRenewal, true);
assert.ok(!Object.hasOwn(automaticUpdate, "status"));
const manualUpdate = documentPayload({
  id: 8, rowVersion: "24", status: "Unknown", renewalStatus: "Unknown",
  riskScore: null, recommendedAction: "Verify with issuer",
}, "manual", "Origin could not be established", false);
assert.equal(manualUpdate.riskScore, null);
assert.throws(() => documentPayload({ id: 8, rowVersion: "24", status: "Unknown", renewalStatus: "Unknown", riskScore: null, recommendedAction: "" }, "manual", "reason", false), /recommended action/u);
assert.throws(() => documentPayload({ id: 8, rowVersion: "24" }, "automatic", " ", false), /reason of 1–500/u);

assert.deepEqual(previewDocumentDate("2026-08-30", "2026-08-31"), { status: "Expired", riskScore: 90, renewalStatus: "Renewal Required", assessmentDate: "2026-08-31" });
assert.equal(previewDocumentDate("2026-09-30", "2026-08-31").status, "Expiring");
assert.equal(previewDocumentDate("2026-10-01", "2026-08-31").status, "Active");
assert.equal(previewDocumentDate("", "2026-08-31").status, "Unknown");

const sessionA = { token: "token-a", csrfToken: "csrf-a", company: { id: 4 }, user: { id: 2 }, role: "admin", permissions: ["compliance:manage"] };
const sessionB = { token: "token-b", csrfToken: "csrf-b", company: { id: 5 }, user: { id: 3 }, role: "admin", permissions: ["compliance:manage"] };
const stored = (session) => JSON.stringify({ session, expiresAt: Date.now() + 60_000 });
let rawSession = stored(sessionA);
let release;
const delayed = new Promise(resolve => { release = resolve; });
let dispatches = 0;
let leakedGuard = false;
const client = axios.create({ adapter: async config => {
  dispatches += 1;
  leakedGuard = Object.keys(config.headers.toJSON()).some(key => /expected-session-guard/i.test(key));
  return { data: { success: true }, status: 200, statusText: "OK", headers: {}, config };
} });
client.interceptors.request.use(async config => {
  await delayed;
  enforceRequestSessionGuard(config, rawSession);
  return config;
});
const swapped = client.put("/api/documents/7", {}, sessionBoundRequest(sessionA));
rawSession = stored(sessionB);
release();
await assert.rejects(swapped, SessionChangedBeforeRequestError);
assert.equal(dispatches, 0, "a delayed interceptor must not dispatch under replacement credentials");

let releaseRead;
const delayedRead = new Promise(resolve => { releaseRead = resolve; });
let readDispatches = 0;
rawSession = stored(sessionA);
const readClient = axios.create({ adapter: async config => {
  readDispatches += 1;
  return { data: { success: true }, status: 200, statusText: "OK", headers: {}, config };
} });
readClient.interceptors.request.use(async config => {
  await delayedRead;
  enforceRequestSessionGuard(config, rawSession);
  return config;
});
const swappedRead = readClient.get("/api/documents/7", sessionBoundRequest(sessionA));
rawSession = stored(sessionB);
releaseRead();
await assert.rejects(swappedRead, SessionChangedBeforeRequestError);
assert.equal(readDispatches, 0, "a delayed document GET must not dispatch under replacement credentials");

rawSession = stored(sessionA);
await client.put("/api/documents/7", {}, sessionBoundRequest(sessionA));
assert.equal(dispatches, 1);
assert.equal(leakedGuard, false, "the local-only session marker must be removed before dispatch");

const sameIdentityNewToken = { ...sessionA, token: "token-a-replaced" };
assert.throws(() => consumeRequestSessionGuard(createRequestSessionGuard(sessionA), stored(sameIdentityNewToken)), SessionChangedBeforeRequestError);
assert.throws(() => consumeRequestSessionGuard(createRequestSessionGuard(sessionA), null), SessionChangedBeforeRequestError);
assert.throws(() => consumeRequestSessionGuard(createRequestSessionGuard(sessionA), "{malformed"), SessionChangedBeforeRequestError);
const replayGuard = createRequestSessionGuard(sessionA);
consumeRequestSessionGuard(replayGuard, stored(sessionA));
assert.throws(() => consumeRequestSessionGuard(replayGuard, stored(sessionA)), SessionChangedBeforeRequestError);

const apiClientSource = (await import("node:fs/promises")).readFile(new URL("../src/services/apiClient.ts", import.meta.url), "utf8");
assert.match(await apiClientSource, /enforceRequestSessionGuard\(config,\s*session\);[\s\S]*if\s*\(session\)/u,
  "the shared transport must enforce the guard before attaching browser credentials");
const fenceSource = await (await import("node:fs/promises")).readFile(new URL("../src/utils/documentWriteFence.ts", import.meta.url), "utf8");
assert.match(fenceSource, /error instanceof SessionChangedBeforeRequestError\) return false/u,
  "a proven pre-dispatch rejection must not be mislabeled as an uncertain write");
const documentsApiSource = await (await import("node:fs/promises")).readFile(new URL("../src/services/documentsApi.ts", import.meta.url), "utf8");
for (const method of ["list", "summary", "detail"]) {
  assert.match(documentsApiSource, new RegExp(`${method}:[\\s\\S]{0,220}sessionBoundRequest\\(session\\)`, "u"),
    `${method} must bind document reads to the captured exact session`);
}
const documentHooksSource = await (await import("node:fs/promises")).readFile(new URL("../src/hooks/useBatch3.ts", import.meta.url), "utf8");
assert.match(documentHooksSource, /queryFn:\s*\(\)\s*=>\s*documentsApi\.list\(scope\.session!\)/u);
assert.match(documentHooksSource, /queryFn:\s*\(\)\s*=>\s*documentsApi\.summary\(scope\.session!\)/u);
assert.match(documentHooksSource, /queryFn:\s*\(\)\s*=>\s*documentsApi\.detail\(id!,\s*scope\.session!\)/u);

console.log("Document lifecycle provenance and exact-session transport contract passed.");
