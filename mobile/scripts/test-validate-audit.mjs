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
};

function metadata(high, critical = 0) {
  return { vulnerabilities: { info: 0, low: 0, moderate: 0, high, critical, total: high + critical } };
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
    metadata: metadata(2),
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

test("unknown package and advisory fail closed", () => {
  const incompleteAudit = run({ error: { code: "EAUDITENDPOINT" } });
  assert.equal(incompleteAudit.status, 1);
  assert.match(incompleteAudit.stderr, /audit JSON is incomplete/);

  const unknownPackage = run({
    vulnerabilities: { surprise: { name: "surprise", severity: "high", via: ["missing-package"] } },
    metadata: metadata(1),
  });
  assert.equal(unknownPackage.status, 1);
  assert.match(unknownPackage.stderr, /Unresolved vulnerability provenance/);
  assert.match(unknownPackage.stderr, /outside reviewed advisory paths/);

  const rootlessCycle = run({
    vulnerabilities: {
      first: { name: "first", severity: "high", via: ["second"] },
      second: { name: "second", severity: "high", via: ["first"] },
    },
    metadata: metadata(2),
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
    metadata: metadata(1),
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
    metadata: metadata(1, 1),
  });
  assert.equal(result.status, 1);
  assert.match(result.stderr, /Critical mobile advisories are not allowed/);

  const inconsistent = run({
    vulnerabilities: {
      "image-size": { name: "image-size", severity: "high", via: [advisory1138808] },
    },
    metadata: metadata(2),
  });
  assert.equal(inconsistent.status, 1);
  assert.match(inconsistent.stderr, /metadata total 2 does not match 1 findings/);
});
