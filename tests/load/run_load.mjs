#!/usr/bin/env node
import path from "node:path";
import process from "node:process";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import { loadSecureEnvFile, resolveLoadConfig, sanitizedConfig } from "./load_guard.mjs";

const directory = path.dirname(fileURLToPath(import.meta.url));
const argumentsSet = new Set(process.argv.slice(2));

try {
  if ((argumentsSet.has("--dry-run") ? 1 : 0) + (argumentsSet.has("--execute") ? 1 : 0) !== 1 || argumentsSet.size !== 1) {
    throw new Error("Usage: node tests/load/run_load.mjs (--dry-run | --execute)");
  }
  loadSecureEnvFile(path.join(directory, ".env.local"));
  const config = resolveLoadConfig(process.env);
  if (argumentsSet.has("--dry-run")) {
    process.stdout.write(`${JSON.stringify({ ...sanitizedConfig(config), networkCalls: 0 }, null, 2)}\n`);
    process.exit(0);
  }

  const result = spawnSync("k6", ["run", path.join(directory, "readonly.js")], {
    cwd: directory,
    stdio: "inherit",
    env: {
      ...process.env,
      K6_API_BASE_URL: config.apiBaseUrl,
      K6_BEARER_TOKEN: config.bearerToken,
      K6_PUBLIC_PATH: config.publicPath,
      K6_AUTHENTICATED_PATH: config.authenticatedPath,
      K6_ITERATIONS_PER_SECOND: String(config.iterationsPerSecond),
      K6_DURATION_SECONDS: String(config.durationSeconds),
      K6_MAX_VUS: String(config.maxVus),
    },
  });
  if (result.error?.code === "ENOENT") throw new Error("k6 is not installed; install an approved pinned k6 release before execution");
  process.exit(result.status ?? 2);
} catch (error) {
  process.stderr.write(`${error instanceof Error ? error.message : String(error)}\n`);
  process.exit(2);
}
