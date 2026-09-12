import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { requireCommercialModuleRecords } from "../src/services/commercialModulePayload.ts";

const record = { id: "crm-1", name: "Qualified account" };
assert.deepEqual(
  requireCommercialModuleRecords({ moduleKey: "leads", records: [record] }, "leads"),
  [record],
  "the standard commercial-module envelope exposes its records",
);

for (const payload of [
  [],
  { moduleKey: "leads" },
  { moduleKey: "leads", records: {} },
  { moduleKey: "opportunities", records: [] },
  { moduleKey: "leads", records: [null] },
]) {
  assert.throws(
    () => requireCommercialModuleRecords(payload, "leads"),
    /expected records payload|invalid record/,
    "malformed or mismatched payloads must error instead of presenting a false zero",
  );
}

for (const [page, moduleKey] of [
  ["LeadsPage.tsx", "leads"],
  ["OpportunitiesPage.tsx", "opportunities"],
  ["QuotationsPage.tsx", "quotations"],
  ["CampaignsPage.tsx", "campaigns"],
]) {
  const source = readFileSync(
    fileURLToPath(new URL(`../src/pages/${page}`, import.meta.url)),
    "utf8",
  );
  assert.ok(
    source.includes(`requireCommercialModuleRecords(payload, "${moduleKey}")`),
    `${page} must adapt the standard commercial-module envelope before mapping records`,
  );
}

console.log("Commercial module payload contract: envelopes adapted and malformed payloads fail closed.");
