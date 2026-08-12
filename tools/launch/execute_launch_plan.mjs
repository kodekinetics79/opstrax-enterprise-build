#!/usr/bin/env node
import fs from "node:fs";
import path from "node:path";
import process from "node:process";
import { fileURLToPath } from "node:url";
import { executeLaunchPlan, dryRunExecutablePlan, loadSecureEnvFile, resolveLaunchExecution } from "./launch_execution.mjs";
import { generateLaunchPlan, planSha256 } from "./launch_plan.mjs";

const directory = path.dirname(fileURLToPath(import.meta.url));

function usage() {
  return "Usage: node execute_launch_plan.mjs (--dry-run | --execute) [--plan generated/plan.json]";
}

function argumentsFrom(argv) {
  const result = { dryRun: false, execute: false, plan: undefined };
  for (let index = 0; index < argv.length; index += 1) {
    const argument = argv[index];
    if (argument === "--dry-run") result.dryRun = true;
    else if (argument === "--execute") result.execute = true;
    else if (argument === "--plan") result.plan = argv[++index];
    else if (argument === "--help" || argument === "-h") result.help = true;
    else throw new Error(`Unknown argument: ${argument}`);
  }
  if (result.dryRun === result.execute && !result.help) throw new Error("Choose exactly one of --dry-run or --execute");
  return result;
}

function readPlan(planPath) {
  return planPath ? JSON.parse(fs.readFileSync(path.resolve(planPath), "utf8")) : generateLaunchPlan();
}

try {
  const options = argumentsFrom(process.argv.slice(2));
  if (options.help) {
    process.stdout.write(`${usage()}\n`);
    process.exit(0);
  }
  const plan = readPlan(options.plan);
  if (options.dryRun) {
    const outcome = dryRunExecutablePlan(plan);
    process.stdout.write(`${JSON.stringify({ ...outcome, planSha256: planSha256(plan) }, null, 2)}\n`);
  } else {
    loadSecureEnvFile(path.join(directory, ".env.local"));
    const config = resolveLaunchExecution(process.env);
    const outcome = await executeLaunchPlan(plan, config, {
      onProgress: ({ completed, total }) => process.stderr.write(`launch progress ${completed}/${total}\n`),
    });
    process.stdout.write(`${JSON.stringify(outcome, null, 2)}\n`);
  }
} catch (error) {
  process.stderr.write(`${error instanceof Error ? error.message : String(error)}\n${usage()}\n`);
  process.exit(2);
}
