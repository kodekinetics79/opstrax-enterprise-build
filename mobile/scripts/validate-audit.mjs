#!/usr/bin/env node
import fs from "node:fs";
import process from "node:process";

const allowedAdvisories = new Map([
  [1138808, Object.freeze({
    name: "image-size",
    dependency: "image-size",
    severity: "high",
    url: "https://github.com/advisories/GHSA-w3rx-r6r6-pgpr",
  })],
  [1138809, Object.freeze({
    name: "image-size",
    dependency: "image-size",
    severity: "high",
    url: "https://github.com/advisories/GHSA-5p2g-fcmc-qvqq",
  })],
]);

function isRecord(value) {
  return value !== null && !Array.isArray(value) && typeof value === "object";
}

const input = process.argv[2];
if (!input) {
  process.stderr.write("Usage: node scripts/validate-audit.mjs <npm-audit.json>\n");
  process.exit(2);
}

let audit;
try {
  audit = JSON.parse(fs.readFileSync(input, "utf8"));
} catch (error) {
  process.stderr.write(`Unable to parse npm audit JSON: ${error instanceof Error ? error.message : String(error)}\n`);
  process.exit(2);
}

if (!isRecord(audit.vulnerabilities) || !isRecord(audit.metadata?.vulnerabilities)) {
  process.stderr.write("npm audit JSON is incomplete; advisory policy cannot be evaluated\n");
  process.exit(1);
}

const vulnerabilities = audit.vulnerabilities;
const findings = Object.entries(vulnerabilities);
const findingNames = new Set(findings.map(([name]) => name));
const errors = [];
const report = (message) => errors.push(message);
const reverseVia = new Map(findings.map(([name]) => [name, new Set()]));
const approvedRoots = new Set();
const observedAdvisories = new Set();

for (const [name, finding] of findings) {
  if (!isRecord(finding)) {
    report(`Malformed mobile vulnerability finding: ${name}`);
    continue;
  }
  if (finding.name !== name) report(`Finding name mismatch for ${name}`);
  if (finding.severity !== "high") report(`Unexpected ${String(finding.severity)} mobile finding for ${name}`);
  if (!Array.isArray(finding.via) || finding.via.length === 0) {
    report(`Finding ${name} has no advisory provenance`);
    continue;
  }

  for (const via of finding.via) {
    if (typeof via === "string") {
      if (!findingNames.has(via)) {
        report(`Unresolved vulnerability provenance ${name} -> ${via}`);
      } else {
        reverseVia.get(via).add(name);
      }
      continue;
    }
    if (!isRecord(via)) {
      report(`Malformed advisory provenance for ${name}`);
      continue;
    }

    const expected = allowedAdvisories.get(via.source);
    if (!expected) {
      report(`Unexpected advisory ${String(via.source)} in the Expo 56 upstream chain`);
      continue;
    }
    for (const field of ["name", "dependency", "severity", "url"]) {
      if (via[field] !== expected[field]) {
        report(`Advisory ${via.source} has unexpected ${field} provenance`);
      }
    }
    if (name !== expected.dependency) {
      report(`Advisory ${via.source} is attached to unexpected package ${name}`);
    }
    approvedRoots.add(name);
    observedAdvisories.add(via.source);
  }
}

// npm may expand a vulnerable dependency into every affected dependent. Derive that
// dependent closure from the audit's `via` graph instead of hard-coding package names.
// Cycles are harmless only when their component is reachable from an approved terminal
// image-size advisory; unresolved strings and rootless cycles remain outside the closure.
const approvedClosure = new Set(approvedRoots);
const queue = [...approvedRoots];
while (queue.length > 0) {
  const dependency = queue.shift();
  for (const dependent of reverseVia.get(dependency) || []) {
    if (!approvedClosure.has(dependent)) {
      approvedClosure.add(dependent);
      queue.push(dependent);
    }
  }
}
for (const name of findingNames) {
  if (!approvedClosure.has(name)) {
    report(`Unexpected mobile vulnerability package outside reviewed advisory paths: ${name}`);
  }
}

const counts = audit.metadata.vulnerabilities;
for (const key of ["info", "low", "moderate", "high", "critical", "total"]) {
  if (!Number.isSafeInteger(counts[key]) || counts[key] < 0) {
    report(`npm audit metadata has invalid ${key} count`);
  }
}
if (counts.total !== findings.length) {
  report(`npm audit metadata total ${String(counts.total)} does not match ${findings.length} findings`);
}
if (counts.high !== findings.length || counts.info !== 0 || counts.low !== 0 || counts.moderate !== 0) {
  report("Only high-severity findings in the reviewed advisory paths are allowed");
}
if (counts.critical !== 0) report("Critical mobile advisories are not allowed");

if (errors.length > 0) {
  for (const message of [...new Set(errors)]) process.stderr.write(`${message}\n`);
  process.exit(1);
}

process.stdout.write(
  `Mobile audit policy accepted ${findings.length} finding(s) resolving to reviewed advisory source(s) ${[...observedAdvisories].sort().join(",") || "none"}; zero critical.\n`,
);
