import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

const repository = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const gate = path.join(repository, "tools/verify-dotnet-warning-baseline.sh");
const warning = (code = "CS8604", location = 1) =>
  `${repository}/example.cs(${location},1): warning ${code}: Fixture warning [fixture.csproj]`;
const completed = (warnings = [], { count = warnings.length, errors = 0, newline = "\n" } = {}) =>
  [...warnings, "", "Build succeeded.", "", ...warnings,
    `    ${count} Warning(s)`, `    ${errors} Error(s)`, "", "Time Elapsed 00:00:00.12", ""].join(newline);

// The gate is real; only its dotnet/capture subprocesses are replaced. No SDK,
// package install, network access, build server, or other process is used/killed.
function runGate({ output = completed(), exitCode = 0, canceled = false,
  baseline = "TOTAL\t0\n", teeMode, sortFailure, stderrOutput = false } = {}) {
  const fixture = fs.mkdtempSync(path.join(os.tmpdir(), "opstrax-warning-gate-"));
  try {
    const bin = path.join(fixture, "bin");
    fs.mkdirSync(bin);
    const executable = (name, source) => {
      fs.writeFileSync(path.join(bin, name), `#!${process.execPath}\n${source}`, { mode: 0o700 });
    };
    executable("dotnet", `
      const fs = require("node:fs");
      const invocation = {
        args: process.argv.slice(2), language: process.env.DOTNET_CLI_UI_LANGUAGE
      };
      fs.writeFileSync(process.env.FIXTURE_ARGUMENTS, JSON.stringify(invocation));
      if (process.env.FIXTURE_CANCELED === "1") process.on("SIGTERM", () => {
        fs.writeFileSync(process.env.FIXTURE_ARGUMENTS, JSON.stringify({ ...invocation, handledSignal: "SIGTERM" }));
        process.exit(0);
      });
      const stream = process.env.FIXTURE_STDERR === "1" ? process.stderr : process.stdout;
      stream.write(process.env.FIXTURE_OUTPUT, () => {
        if (process.env.FIXTURE_CANCELED === "1") {
          // Keep the owned child alive long enough to handle its signal, but
          // fail the fixture within one second if signal delivery breaks.
          setTimeout(() => process.exit(75), 1_000);
          process.kill(process.pid, "SIGTERM");
        }
        else process.exitCode = Number(process.env.FIXTURE_EXIT);
      });
    `);
    if (teeMode) {
      executable("tee", `
        const fs = require("node:fs");
        const chunks = [];
        process.stdin.on("data", chunk => chunks.push(chunk));
        process.stdin.on("end", () => {
          const output = Buffer.concat(chunks).toString("utf8");
          const mode = process.env.FIXTURE_TEE;
          const captured = mode === "empty" ? "" : mode === "truncated"
            ? output.split("    0 Error(s)")[0] : mode === "drop-warnings"
            ? output.split("\\n").filter(line => !/warning [A-Za-z]+[0-9]+:/.test(line)).join("\\n") : output;
          fs.writeFileSync(process.argv[2], captured);
          process.stdout.write(output, () => { process.exitCode = mode === "failed" ? 74 : 0; });
        });
      `);
    }
    if (sortFailure) {
      executable("sort", `
        const chunks = [];
        process.stdin.on("data", chunk => chunks.push(chunk));
        process.stdin.on("end", () => {
          if (process.env.FIXTURE_SORT_FAILURE === "count" && process.argv.includes("-u")) {
            const lines = Buffer.concat(chunks).toString("utf8").trimEnd().split("\\n");
            process.stdout.write([...new Set(lines)].sort().join("\\n") + "\\n");
          } else process.exitCode = 74;
        });
      `);
    }
    const baselinePath = path.join(fixture, "baseline.tsv");
    const argumentsPath = path.join(fixture, "arguments.json");
    fs.writeFileSync(baselinePath, baseline);
    const result = spawnSync("bash", [gate], {
      cwd: repository,
      env: { ...process.env, PATH: `${bin}${path.delimiter}${process.env.PATH}`,
        DOTNET_WARNING_BASELINE: baselinePath, DOTNET_WARNING_PROJECT: path.join(fixture, "fixture.csproj"),
        DOTNET_WARNING_CONFIGURATION: "Release", DOTNET_CLI_UI_LANGUAGE: "fr",
        FIXTURE_ARGUMENTS: argumentsPath, FIXTURE_OUTPUT: output, FIXTURE_EXIT: String(exitCode),
        FIXTURE_CANCELED: canceled ? "1" : "0", FIXTURE_TEE: teeMode ?? "",
        FIXTURE_SORT_FAILURE: sortFailure ?? "", FIXTURE_STDERR: stderrOutput ? "1" : "0" },
      encoding: "utf8", timeout: 5_000, maxBuffer: 1_048_576,
    });
    assert.ifError(result.error);
    assert.equal(result.signal, null, "the bounded fixture must finish without killing the gate");
    const invocation = JSON.parse(fs.readFileSync(argumentsPath, "utf8"));
    if (canceled) assert.equal(invocation.handledSignal, "SIGTERM", "the owned fake child actually handled cancellation");
    return { ...result, invocation };
  } finally {
    // fixture is the exact directory allocated above, never a caller-supplied path.
    fs.rmSync(fixture, { recursive: true, force: true });
  }
}

function assertRejected(result) {
  assert.notEqual(result.status, 0, `${result.stdout}\n${result.stderr}`);
  assert.doesNotMatch(result.stdout, /Distinct \.NET warnings:/, "invalid evidence must not be reported as a warning measurement");
}

for (const [name, output] of [
  ["empty log", ""],
  ["whitespace-only log", " \n\t\r\n"],
  ["progress-only log", "  Determining projects to restore...\n"],
  ["warnings-only log", `${warning()}\n`],
  ["failed build", "Build FAILED.\n    0 Warning(s)\n    1 Error(s)\nTime Elapsed 00:00:00.12\n"],
  ["compiler error without completion", "example.cs(1,1): error CS1002: ; expected\n"],
  ["compiler error contradicting a success trailer", `example.cs(1,1): error CS1002: ; expected\n${completed()}`],
  ["cancellation contradicting a success trailer", `Build canceled.\n${completed()}`],
  ["success marker only", "Build succeeded.\n"],
  ["missing zero-error count", "Build succeeded.\n    0 Warning(s)\nTime Elapsed 00:00:00.12\n"],
  ["missing elapsed trailer", "Build succeeded.\n    0 Warning(s)\n    0 Error(s)\n"],
  ["nonzero error count despite success marker", completed([], { errors: 1 })],
  ["diagnostic text containing a success marker", "message: Build succeeded.\n    0 Warning(s)\n    0 Error(s)\nTime Elapsed 00:00:00.12\n"],
  ["summary with missing warning diagnostics", completed([], { count: 1 })],
  ["diagnostics contradicting zero-warning summary", completed([warning()], { count: 0 })],
  ["output continuing after a success trailer", `${completed()}Build canceled.\n`],
  ["multiple success trailers", completed() + completed()],
]) {
  test(`warning gate rejects exit-zero ${name}`, () => {
    assertRejected(runGate({ output, baseline: "CS8604\t10\nTOTAL\t10\n" }));
  });
}

test("warning gate rejects its own canceled child even when the child handles SIGTERM with exit zero", () => {
  assertRejected(runGate({ canceled: true, output: "  Determining projects to restore...\n" }));
});

test("warning gate preserves a nonzero build exit even with complete success text", () => {
  const result = runGate({ exitCode: 42 });
  assertRejected(result);
  assert.equal(result.status, 42);
});

for (const teeMode of ["failed", "empty", "truncated", "drop-warnings"]) {
  test(`warning gate rejects ${teeMode} capture evidence`, () => {
    assertRejected(runGate({ teeMode, output: completed([warning()]), baseline: "CS8604\t1\nTOTAL\t1\n" }));
  });
}

for (const sortFailure of ["extraction", "count"]) {
  test(`warning gate rejects ${sortFailure} failure instead of swallowing it`, () => {
    assertRejected(runGate({ sortFailure, output: completed([warning()]), baseline: "CS8604\t1\nTOTAL\t1\n" }));
  });
}

for (const newline of ["\n", "\r\n"]) {
  test(`warning gate accepts a completed zero-warning build (${JSON.stringify(newline)})`, () => {
    const result = runGate({ output: completed([], { newline }) });
    assert.equal(result.status, 0, result.stderr);
    assert.match(result.stdout, /Distinct \.NET warnings: 0 \(baseline ceiling: 0\)/);
  });
}

test("warning gate includes completed stderr output in the capture", () => {
  const result = runGate({ stderrOutput: true });
  assert.equal(result.status, 0, result.stderr);
  assert.match(result.stdout, /Distinct \.NET warnings: 0 \(baseline ceiling: 0\)/);
});

test("warning gate fixes summary language and logger format without changing rebuild/restore policy", () => {
  const result = runGate();
  assert.equal(result.status, 0, result.stderr);
  assert.equal(result.invocation.language, "en");
  assert.ok(result.invocation.args.includes("--no-restore"));
  assert.ok(result.invocation.args.includes("--target:Rebuild"));
  assert.ok(result.invocation.args.includes("--verbosity:minimal"));
  assert.ok(result.invocation.args.includes("--tl:off"));
  assert.ok(result.invocation.args.includes("--consoleLoggerParameters:Summary;DisableConsoleColor"));
  assert.ok(!result.invocation.args.some(argument => argument.includes("NoSummary")));
});

test("warning gate accepts per-code/total ceilings and de-duplicates repeated locations", () => {
  const result = runGate({ output: completed([warning(), warning(), warning("xUnit2031", 2)]),
    baseline: "CS8604\t1\nxUnit2031\t1\nTOTAL\t2\n" });
  assert.equal(result.status, 0, result.stderr);
  assert.match(result.stdout, /Distinct \.NET warnings: 2 \(baseline ceiling: 2\)/);
});

test("warning gate retains ratchet notice for a completed lower-warning build", () => {
  const result = runGate({ baseline: "CS8604\t1\nTOTAL\t1\n" });
  assert.equal(result.status, 0, result.stderr);
  assert.match(result.stderr, /Warning debt decreased/);
});

for (const [name, warnings, baseline, message] of [
  ["new code", [warning("CS9999")], "TOTAL\t10\n", /New warning code is not baselined/],
  ["per-code growth", [warning(), warning("CS8604", 2)], "CS8604\t1\nTOTAL\t10\n", /Warning debt increased for CS8604/],
  ["total growth", [warning(), warning("CS8604", 2)], "CS8604\t2\nTOTAL\t1\n", /Total warning debt increased/],
]) {
  test(`warning gate still rejects ${name}`, () => {
    const result = runGate({ output: completed(warnings), baseline });
    assert.equal(result.status, 1, result.stderr);
    assert.match(result.stderr, message);
  });
}

test("warning-gate regressions are enrolled in the zero-install CI launch path and mandatory build gate", () => {
  const workflow = fs.readFileSync(path.join(repository, ".github/workflows/ci.yml"), "utf8");
  const launch = workflow.slice(workflow.indexOf("  launch-tooling-tests:"), workflow.indexOf("  playwright-public-tests:"));
  const dotnet = workflow.slice(workflow.indexOf("  dotnet-build-test:"), workflow.indexOf("  dotnet-integration-tests:"));
  assert.match(launch, /node --test tools\/launch\/test_\*\.mjs/);
  assert.match(dotnet, /run: tools\/verify-dotnet-warning-baseline\.sh/);
});
