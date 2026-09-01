import assert from "node:assert/strict";
import { existsSync, readFileSync } from "node:fs";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../../..");
const configPath = path.join(repoRoot, "telematics", "fly.staging-certification.toml");

test("Fly certification manifest resolves its Dockerfile from the manifest directory", () => {
  const config = readFileSync(configPath, "utf8");
  const dockerfileEntry = config.match(/^\s*dockerfile\s*=\s*"([^"]+)"\s*$/m);

  assert.ok(dockerfileEntry, "manifest must declare one Dockerfile");
  const resolvedDockerfile = path.resolve(path.dirname(configPath), dockerfileEntry[1]);
  assert.equal(resolvedDockerfile, path.join(repoRoot, "telematics", "Dockerfile"));
  assert.ok(existsSync(resolvedDockerfile), "resolved Fly Dockerfile must exist");

  const dockerfile = readFileSync(resolvedDockerfile, "utf8");
  assert.match(
    dockerfile,
    /^COPY telematics\/src\//m,
    "Dockerfile must continue to use the repository root as its build context",
  );
});

test("Fly certification manifest retains the isolated fail-closed lane", () => {
  const config = readFileSync(configPath, "utf8");

  assert.match(config, /^app\s*=\s*"opstrax-telematics-staging-cert"$/m);
  assert.match(config, /^\s*Gateway__ListenPort\s*=\s*"5023"$/m);
  assert.match(config, /^\s*Gateway__Edge__Egress\s*=\s*"Https"$/m);
  assert.match(
    config,
    /^\s*Gateway__Edge__Forward__BaseUrl\s*=\s*"https:\/\/opstrax-staging-api\.onrender\.com"$/m,
  );
  assert.match(
    config,
    /^\s*Gateway__Edge__Forward__GatewayId\s*=\s*"wave1-g1b-staging-iad-1"$/m,
  );
  assert.match(config, /^\s*Gateway__Edge__Allowlist__Path\s*=\s*"\/var\/lib\/opstrax-gateway\/imei-allowlist\.txt"$/m);
  assert.match(config, /^\s*Gateway__Edge__Outbox__Path\s*=\s*"\/var\/lib\/opstrax-gateway\/outbox"$/m);
  assert.doesNotMatch(config, /Gateway__Edge__Allowlist__Inline|Gateway__Edge__Allowlist__Imeis/);
  assert.doesNotMatch(
    config,
    /Gateway__Edge__Forward__Secret|Gateway__StoreForwardEncryptionKey|ConnectionStrings__/,
    "protected values must be supplied by Fly secrets, never the manifest",
  );
  assert.match(config, /^\s*Gateway__Edge__Protocols__Gt06\s*=\s*"true"$/m);
  assert.match(config, /^\s*Gateway__Edge__Protocols__PacificTrack__Enabled\s*=\s*"false"$/m);
});
