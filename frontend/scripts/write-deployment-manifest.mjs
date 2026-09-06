import { mkdir, writeFile } from "node:fs/promises";
import { resolve } from "node:path";

const first = (...names) => names
  .map((name) => process.env[name]?.trim())
  .find(Boolean) ?? "";

const frontendSha = first(
  "RENDER_GIT_COMMIT",
  "VITE_DEPLOYMENT_SHA",
  "VERCEL_GIT_COMMIT_SHA",
  "GITHUB_SHA",
) || "unknown";
const frontendEnvironment = first(
  "VITE_APP_ENVIRONMENT",
  "VERCEL_ENV",
  "NODE_ENV",
) || "unknown";
const apiBaseUrl = first(
  "VITE_API_BASE_URL",
  "VITE_DOTNET_API_URL",
  "VITE_PLATFORM_API_BASE_URL",
).replace(/\/+$/, "");

if (frontendEnvironment === "production" && !/^[0-9a-f]{40}$/.test(frontendSha)) {
  throw new Error(
    "Production frontend builds require an exact lowercase 40-character source SHA. " +
    "Set VITE_DEPLOYMENT_SHA or deploy from a traceable Git integration.",
  );
}
if (frontendEnvironment === "production" && !apiBaseUrl) {
  throw new Error("Production frontend builds require an explicit API base URL.");
}

const outputDirectory = resolve(process.env.OPSTRAX_MANIFEST_OUTPUT_DIR || "dist");
await mkdir(outputDirectory, { recursive: true });
await writeFile(
  resolve(outputDirectory, "deployment.json"),
  `${JSON.stringify({
    schemaVersion: 1,
    frontendSha,
    frontendEnvironment,
    apiBaseUrl,
  }, null, 2)}\n`,
  "utf8",
);

console.log(`Wrote deployment manifest for frontend ${frontendSha} (${frontendEnvironment}).`);
