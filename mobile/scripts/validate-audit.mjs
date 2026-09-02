#!/usr/bin/env node
import fs from "node:fs";
import process from "node:process";

const allowedAdvisories = new Map([
  [1138808, Object.freeze({
    name: "image-size",
    dependency: "image-size",
    severity: "high",
    url: "https://github.com/advisories/GHSA-w3rx-r6r6-pgpr",
    range: "<=2.0.2",
  })],
  [1138809, Object.freeze({
    name: "image-size",
    dependency: "image-size",
    severity: "high",
    url: "https://github.com/advisories/GHSA-5p2g-fcmc-qvqq",
    range: "<=2.0.2",
  })],
  [1147955, Object.freeze({
    name: "decode-uri-component",
    dependency: "decode-uri-component",
    severity: "moderate",
    url: "https://github.com/advisories/GHSA-vcc3-ghjq-m6fr",
    range: "<=0.4.2",
    owner: "Mobile Security / Release Assurance",
    expiresOn: "2026-09-30",
    expectedClosure: Object.freeze([
      "@react-navigation/bottom-tabs",
      "@react-navigation/core",
      "@react-navigation/elements",
      "@react-navigation/native",
      "@react-navigation/native-stack",
      "decode-uri-component",
      "query-string",
    ]),
  })],
]);

const severityRank = Object.freeze({ info: 0, low: 1, moderate: 2, high: 3, critical: 4 });

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
const approvedRootSeverity = new Map();
const observedAdvisories = new Set();

for (const [name, finding] of findings) {
  if (!isRecord(finding)) {
    report(`Malformed mobile vulnerability finding: ${name}`);
    continue;
  }
  if (finding.name !== name) report(`Finding name mismatch for ${name}`);
  if (!(finding.severity in severityRank)) report(`Unexpected ${String(finding.severity)} mobile finding for ${name}`);
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
    for (const field of ["name", "dependency", "severity", "url", "range"]) {
      if (via[field] !== expected[field]) {
        report(`Advisory ${via.source} has unexpected ${field} provenance`);
      }
    }
    if (name !== expected.dependency) {
      report(`Advisory ${via.source} is attached to unexpected package ${name}`);
    }
    approvedRoots.add(name);
    approvedRootSeverity.set(name, expected.severity);
    observedAdvisories.add(via.source);
  }
}

function dependentClosure(root) {
  const closure = new Set([root]);
  const pending = [root];
  while (pending.length > 0) {
    const dependency = pending.shift();
    for (const dependent of reverseVia.get(dependency) || []) {
      if (!closure.has(dependent)) {
        closure.add(dependent);
        pending.push(dependent);
      }
    }
  }
  return closure;
}

for (const [source, expected] of allowedAdvisories) {
  if (!observedAdvisories.has(source) || !expected.expiresOn) continue;
  const expiry = new Date(`${expected.expiresOn}T23:59:59.999Z`);
  if (!Number.isFinite(expiry.valueOf()) || new Date() > expiry) {
    report(`Reviewed advisory ${source} exception owned by ${expected.owner} expired on ${expected.expiresOn}`);
  }

  const closure = [...dependentClosure(expected.dependency)].sort();
  const required = [...expected.expectedClosure].sort();
  if (JSON.stringify(closure) !== JSON.stringify(required)) {
    report(`Reviewed advisory ${source} dependency topology changed: observed ${closure.join(",")}`);
  }
  for (const name of closure) {
    const fix = vulnerabilities[name]?.fixAvailable;
    if (fix !== false && !(isRecord(fix) && fix.isSemVerMajor === true)) {
      report(`Reviewed advisory ${source} now has a compatible fix through ${name}`);
    }
  }
}

// npm may expand a vulnerable dependency into every affected dependent. Derive that
// dependent closure from the audit's `via` graph instead of hard-coding package names.
// Cycles are harmless only when their component is reachable from an approved terminal
// image-size advisory; unresolved strings and rootless cycles remain outside the closure.
const approvedClosure = new Set(approvedRoots);
const expectedSeverity = new Map(approvedRootSeverity);
const queue = [...approvedRoots];
while (queue.length > 0) {
  const dependency = queue.shift();
  for (const dependent of reverseVia.get(dependency) || []) {
    const inherited = expectedSeverity.get(dependency);
    const current = expectedSeverity.get(dependent);
    if (inherited && (!current || severityRank[inherited] > severityRank[current])) {
      expectedSeverity.set(dependent, inherited);
    }
    if (!approvedClosure.has(dependent)) {
      approvedClosure.add(dependent);
      queue.push(dependent);
    }
  }
}
for (const name of findingNames) {
  if (!approvedClosure.has(name)) {
    report(`Unexpected mobile vulnerability package outside reviewed advisory paths: ${name}`);
  } else if (vulnerabilities[name]?.severity !== expectedSeverity.get(name)) {
    report(`Unexpected ${String(vulnerabilities[name]?.severity)} mobile finding for ${name}`);
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
const actualCounts = { info: 0, low: 0, moderate: 0, high: 0, critical: 0 };
for (const [, finding] of findings) {
  if (isRecord(finding) && finding.severity in actualCounts) actualCounts[finding.severity] += 1;
}
for (const severity of Object.keys(actualCounts)) {
  if (counts[severity] !== actualCounts[severity]) {
    report(`npm audit metadata ${severity} count ${String(counts[severity])} does not match ${actualCounts[severity]} findings`);
  }
}
if (counts.critical !== 0) report("Critical mobile advisories are not allowed");

if (errors.length > 0) {
  for (const message of [...new Set(errors)]) process.stderr.write(`${message}\n`);
  process.exit(1);
}

process.stdout.write(
  `Mobile audit policy accepted ${findings.length} finding(s) resolving to reviewed advisory source(s) ${[...observedAdvisories].sort().join(",") || "none"}; zero critical; time-boxed exceptions remain within expiry.\n`,
);
