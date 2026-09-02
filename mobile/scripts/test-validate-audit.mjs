import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { spawnSync } from "node:child_process";
import test from "node:test";
import { fileURLToPath } from "node:url";

const validator = path.join(path.dirname(fileURLToPath(import.meta.url)), "validate-audit.mjs");
const expandedFixture = path.join(
  path.dirname(fileURLToPath(import.meta.url)),
  "fixtures/npm-audit-expanded-dependents.json",
);
const advisory1138808 = {
  source: 1138808,
  name: "image-size",
  dependency: "image-size",
  severity: "high",
  url: "https://github.com/advisories/GHSA-w3rx-r6r6-pgpr",
  range: "<=2.0.2",
};
const advisory1147955 = {
  source: 1147955,
  name: "decode-uri-component",
  dependency: "decode-uri-component",
  severity: "moderate",
  url: "https://github.com/advisories/GHSA-vcc3-ghjq-m6fr",
  range: "<=0.4.2",
};

function metadata({ info = 0, low = 0, moderate = 0, high = 0, critical = 0 } = {}) {
  return { vulnerabilities: { info, low, moderate, high, critical, total: info + low + moderate + high + critical } };
}

function run(payload) {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), "opstrax-mobile-audit-"));
  const filename = path.join(directory, "audit.json");
  try {
    fs.writeFileSync(filename, JSON.stringify(payload));
    return spawnSync(process.execPath, [validator, filename], { encoding: "utf8" });
  } finally {
    fs.rmSync(directory, { recursive: true });
  }
}

test("reviewed Expo 56 upstream advisory chain is accepted", () => {
  const result = run({
    vulnerabilities: {
      "image-size": { name: "image-size", severity: "high", via: [advisory1138808] },
      metro: { name: "metro", severity: "high", via: ["image-size"] },
    },
    metadata: metadata({ high: 2 }),
  });
  assert.equal(result.status, 0, result.stderr);
});

test("npm dependent expansion is derived from approved recursive via paths", () => {
  const payload = JSON.parse(fs.readFileSync(expandedFixture, "utf8"));
  const result = run(payload);
  assert.equal(result.status, 0, result.stderr);
  assert.match(result.stdout, /accepted 32 finding\(s\)/);
  assert.match(result.stdout, /1138808,1138809/);
});

test("reviewed React Navigation availability advisory chain is accepted only at moderate severity", () => {
  const result = run({
    vulnerabilities: {
      "decode-uri-component": { name: "decode-uri-component", severity: "moderate", via: [advisory1147955], fixAvailable: false },
      "query-string": { name: "query-string", severity: "moderate", via: ["decode-uri-component"], fixAvailable: false },
      "@react-navigation/core": { name: "@react-navigation/core", severity: "moderate", via: ["query-string"], fixAvailable: false },
      "@react-navigation/native": { name: "@react-navigation/native", severity: "moderate", via: ["@react-navigation/core"], fixAvailable: false },
      "@react-navigation/elements": { name: "@react-navigation/elements", severity: "moderate", via: ["@react-navigation/native"], fixAvailable: false },
      "@react-navigation/bottom-tabs": { name: "@react-navigation/bottom-tabs", severity: "moderate", via: ["@react-navigation/elements", "@react-navigation/native"], fixAvailable: false },
      "@react-navigation/native-stack": { name: "@react-navigation/native-stack", severity: "moderate", via: ["@react-navigation/elements", "@react-navigation/native"], fixAvailable: { name: "@react-navigation/native-stack", version: "5.0.5", isSemVerMajor: true } },
    },
    metadata: metadata({ moderate: 7 }),
  });
  assert.equal(result.status, 0, result.stderr);
  assert.match(result.stdout, /1147955/);

  const escalated = run({
    vulnerabilities: {
      "decode-uri-component": { name: "decode-uri-component", severity: "critical", via: [advisory1147955] },
    },
    metadata: metadata({ critical: 1 }),
  });
  assert.equal(escalated.status, 1);
  assert.match(escalated.stderr, /unexpected severity provenance|Critical mobile advisories are not allowed/i);
});

test("reviewed React Navigation exception fails on topology drift or a compatible fix", () => {
  const base = {
    "decode-uri-component": { name: "decode-uri-component", severity: "moderate", via: [advisory1147955], fixAvailable: false },
    "query-string": { name: "query-string", severity: "moderate", via: ["decode-uri-component"], fixAvailable: false },
    "@react-navigation/core": { name: "@react-navigation/core", severity: "moderate", via: ["query-string"], fixAvailable: false },
    "@react-navigation/native": { name: "@react-navigation/native", severity: "moderate", via: ["@react-navigation/core"], fixAvailable: false },
    "@react-navigation/elements": { name: "@react-navigation/elements", severity: "moderate", via: ["@react-navigation/native"], fixAvailable: false },
    "@react-navigation/bottom-tabs": { name: "@react-navigation/bottom-tabs", severity: "moderate", via: ["@react-navigation/elements"], fixAvailable: false },
    "@react-navigation/native-stack": { name: "@react-navigation/native-stack", severity: "moderate", via: ["@react-navigation/elements"], fixAvailable: false },
  };
  const compatible = structuredClone(base);
  compatible["decode-uri-component"].fixAvailable = { name: "decode-uri-component", version: "0.5.0", isSemVerMajor: false };
  const compatibleResult = run({ vulnerabilities: compatible, metadata: metadata({ moderate: 7 }) });
  assert.equal(compatibleResult.status, 1);
  assert.match(compatibleResult.stderr, /now has a compatible fix/);

  const drifted = { ...base, newcomer: { name: "newcomer", severity: "moderate", via: ["query-string"], fixAvailable: false } };
  const driftedResult = run({ vulnerabilities: drifted, metadata: metadata({ moderate: 8 }) });
  assert.equal(driftedResult.status, 1);
  assert.match(driftedResult.stderr, /dependency topology changed/);
});

test("unknown package and advisory fail closed", () => {
  const incompleteAudit = run({ error: { code: "EAUDITENDPOINT" } });
  assert.equal(incompleteAudit.status, 1);
  assert.match(incompleteAudit.stderr, /audit JSON is incomplete/);

  const unknownPackage = run({
    vulnerabilities: { surprise: { name: "surprise", severity: "high", via: ["missing-package"] } },
    metadata: metadata({ high: 1 }),
  });
  assert.equal(unknownPackage.status, 1);
  assert.match(unknownPackage.stderr, /Unresolved vulnerability provenance/);
  assert.match(unknownPackage.stderr, /outside reviewed advisory paths/);

  const rootlessCycle = run({
    vulnerabilities: {
      first: { name: "first", severity: "high", via: ["second"] },
      second: { name: "second", severity: "high", via: ["first"] },
    },
    metadata: metadata({ high: 2 }),
  });
  assert.equal(rootlessCycle.status, 1);
  assert.match(rootlessCycle.stderr, /outside reviewed advisory paths/);

  const unknownAdvisory = run({
    vulnerabilities: {
      "image-size": {
        name: "image-size",
        severity: "high",
        via: [{ ...advisory1138808, source: 9999999 }],
      },
    },
    metadata: metadata({ high: 1 }),
  });
  assert.equal(unknownAdvisory.status, 1);
  assert.match(unknownAdvisory.stderr, /Unexpected advisory/);
});

test("critical findings fail even in reviewed packages", () => {
  const result = run({
    vulnerabilities: {
      "image-size": { name: "image-size", severity: "high", via: [advisory1138808] },
      metro: { name: "metro", severity: "critical", via: ["image-size"] },
    },
    metadata: metadata({ high: 1, critical: 1 }),
  });
  assert.equal(result.status, 1);
  assert.match(result.stderr, /Critical mobile advisories are not allowed/);

  const inconsistent = run({
    vulnerabilities: {
      "image-size": { name: "image-size", severity: "high", via: [advisory1138808] },
    },
    metadata: metadata({ high: 2 }),
  });
  assert.equal(inconsistent.status, 1);
  assert.match(inconsistent.stderr, /metadata total 2 does not match 1 findings/);
});
