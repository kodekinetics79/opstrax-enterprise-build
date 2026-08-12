#!/usr/bin/env node
import fs from "node:fs";
import path from "node:path";
import process from "node:process";
import { generateLaunchPlan, summarizePlan } from "./launch_plan.mjs";

function usage() {
  return "Usage: node generate_launch_plan.mjs [--count 10000] [--seed 20260811] [--dry-run] [--out generated/plan.json]";
}

function parseArgs(argv) {
  const options = { count: 10_000, seed: 20260811, dryRun: false, out: undefined };
  for (let index = 0; index < argv.length; index += 1) {
    const arg = argv[index];
    if (arg === "--dry-run") options.dryRun = true;
    else if (arg === "--count") options.count = Number(argv[++index]);
    else if (arg === "--seed") options.seed = Number(argv[++index]);
    else if (arg === "--out") options.out = argv[++index];
    else if (arg === "--help" || arg === "-h") options.help = true;
    else throw new Error(`Unknown argument: ${arg}`);
  }
  if (options.dryRun && options.out) throw new Error("--dry-run cannot be combined with --out");
  return options;
}

try {
  const options = parseArgs(process.argv.slice(2));
  if (options.help) {
    process.stdout.write(`${usage()}\n`);
    process.exit(0);
  }
  const plan = generateLaunchPlan(options);
  if (options.out) {
    const destination = path.resolve(options.out);
    fs.mkdirSync(path.dirname(destination), { recursive: true, mode: 0o700 });
    fs.writeFileSync(destination, `${JSON.stringify(plan)}\n`, { encoding: "utf8", mode: 0o600 });
  }
  process.stdout.write(`${JSON.stringify(summarizePlan(plan), null, 2)}\n`);
} catch (error) {
  process.stderr.write(`${error instanceof Error ? error.message : String(error)}\n${usage()}\n`);
  process.exit(2);
}
