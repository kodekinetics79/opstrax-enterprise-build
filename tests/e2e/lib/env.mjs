import fs from "node:fs";
import path from "node:path";

function unquote(value) {
  const trimmed = value.trim();
  if (
    trimmed.length >= 2 &&
    ((trimmed.startsWith('"') && trimmed.endsWith('"')) ||
      (trimmed.startsWith("'") && trimmed.endsWith("'")))
  ) {
    return trimmed.slice(1, -1);
  }
  return trimmed;
}

export function loadLocalEnv(file = path.resolve(".env.e2e.local"), env = process.env) {
  if (!fs.existsSync(file)) return;
  const lines = fs.readFileSync(file, "utf8").split(/\r?\n/);
  for (const line of lines) {
    const trimmed = line.trim();
    if (!trimmed || trimmed.startsWith("#")) continue;
    const separator = trimmed.indexOf("=");
    if (separator <= 0) throw new Error(`Invalid environment entry in ${path.basename(file)}`);
    const key = trimmed.slice(0, separator).trim();
    if (!/^[A-Z][A-Z0-9_]*$/.test(key)) throw new Error(`Invalid environment key in ${path.basename(file)}`);
    if (env[key] === undefined) env[key] = unquote(trimmed.slice(separator + 1));
  }
}
