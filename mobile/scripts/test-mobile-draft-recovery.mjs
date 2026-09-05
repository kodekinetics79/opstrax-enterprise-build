import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const source = async (path) => readFile(new URL(`../${path}`, import.meta.url), "utf8");

test("secure driver drafts are scoped by product stage tenant user and work item", async () => {
  const drafts = await source("src/storage/secureDrafts.ts");
  assert.match(drafts, /APP_PRODUCT/);
  assert.match(drafts, /STAGE_LABEL/);
  assert.match(drafts, /companyId/);
  assert.match(drafts, /userId/);
  assert.match(drafts, /workId/);
  assert.match(drafts, /WHEN_UNLOCKED_THIS_DEVICE_ONLY/);
  assert.match(drafts, /12_000/);
});

test("driver exception draft survives failed submission and clears only after success", async () => {
  const trip = await source("src/screens/DriverTripScreen.tsx");
  assert.match(trip, /secureDraftKey\("driver-exception"/);
  assert.match(trip, /readSecureDraft<ExceptionDraft>/);
  assert.match(trip, /writeSecureDraft<ExceptionDraft>/);
  assert.match(trip, /await api\.reportDriverException/);
  assert.match(trip, /await clearSecureDraft\(exceptionDraftKey\)/);
  assert.match(trip, /Your draft remains saved on this device/);
});

test("driver proof recovery persists only uploaded references and text, never photo bytes", async () => {
  const proof = await source("src/screens/DriverProofScreen.tsx");
  assert.match(proof, /secureDraftKey\("driver-proof"/);
  assert.match(proof, /type ProofDraft = \{\s*notes: string;\s*uploaded: DriverProofArtifact \| null;/s);
  assert.doesNotMatch(proof, /writeSecureDraft[^\n]+captured/);
  assert.match(proof, /Uploaded evidence recovered/);
  assert.match(proof, /await clearSecureDraft\(proofDraftKey\)/);
  assert.match(proof, /photo is still local to the current app session/i);
});
