import assert from "node:assert/strict";
import fs from "node:fs";

const integrations = fs.readFileSync(new URL("../src/pages/IntegrationsPage.tsx", import.meta.url), "utf8");

assert.match(
  integrations,
  /type=\{secret \? "password" : field\.type\}/,
  "Built-in provider credential inputs must mask every key, token, secret, password, auth, and credential field",
);
assert.match(
  integrations,
  /type=\{isSecretField\(row\.key\) \? "password" : "text"\}/,
  "Tenant-created connector secret values must be masked as soon as their key identifies a secret",
);
assert.doesNotMatch(
  integrations,
  /Operational alerts and customer notifications are routed through this connector live/,
  "Messaging connectors must not claim live routing merely because an adapter exists",
);
assert.match(
  integrations,
  /status === "Connected"[\s\S]*record\.lastTestOk === true[\s\S]*Boolean\(record\.lastTestedAt\)/,
  "A live-routing availability statement must require Connected status and a recorded successful handshake",
);
assert.match(
  integrations,
  /record\.status !== "Connected"[\s\S]*record\.lastTestOk === false[\s\S]*record\.lastTestOk === true && Boolean\(record\.lastTestedAt\)/,
  "Every connector card and summary must downgrade a stored Connected label unless a successful provider handshake is recorded",
);
assert.doesNotMatch(
  integrations,
  /label: "Connected"[^\n]*note: "live"/,
  "The connector summary must not describe a verified credential handshake as live data flow",
);
assert.match(
  integrations,
  /label: "Connected"[^\n]*note: "verified"/,
  "The connector summary must describe evidence-backed provider handshakes as verified",
);
assert.match(
  integrations,
  /Connected status is stored, but no successful current handshake is recorded\. Live notification routing and delivery are not claimed\./,
  "A stored Connected label without handshake evidence must fail closed",
);
assert.match(
  integrations,
  /Configuration is pending\. No live notification routing or delivery is claimed\./,
  "Pending messaging connectors must disclose that routing and delivery are unproven",
);
assert.match(
  integrations,
  /This connector is disconnected\. No live notification routing or delivery is claimed\./,
  "Disconnected messaging connectors must never imply routing or delivery",
);
assert.match(
  integrations,
  /end-to-end delivery is not claimed until a delivery record succeeds/,
  "A successful handshake must remain distinct from recipient delivery evidence",
);

console.log("Integrations POC truth contract passed.");
