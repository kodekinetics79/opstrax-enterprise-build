#!/usr/bin/env node

const required = (name) => {
  const value = process.env[name]?.trim();
  if (!value) throw new Error(`${name} is required`);
  return value;
};

const action = process.argv[2];
if (!new Set(["suspend", "resume"]).has(action)) {
  throw new Error("Usage: render-service-control.mjs <suspend|resume>");
}

const apiKey = required("RENDER_API_KEY");
const serviceId = required("RENDER_SERVICE_ID");
if (!/^srv-[a-z0-9]+$/.test(serviceId)) throw new Error("RENDER_SERVICE_ID is invalid");

const expectedState = action === "suspend" ? "suspended" : "not_suspended";

const renderRequest = async (path, init = {}) => {
  const response = await fetch(`https://api.render.com/v1${path}`, {
    ...init,
    headers: {
      Accept: "application/json",
      Authorization: `Bearer ${apiKey}`,
    },
  });
  const body = await response.json().catch(() => null);
  if (!response.ok) {
    throw new Error(`Render API ${response.status}: ${body?.message ?? "request failed"}`);
  }
  return body;
};

const suspendedState = (body) => body?.suspended ?? body?.service?.suspended;
const readState = async () => suspendedState(await renderRequest(`/services/${serviceId}`));

let state = await readState();
if (state === expectedState) {
  console.log(`Render service is already ${expectedState}`);
  process.exit(0);
}

await renderRequest(`/services/${serviceId}/${action}`, { method: "POST" });
console.log(`Requested Render service ${action}`);

const deadline = Date.now() + 6 * 60 * 1000;
while (Date.now() < deadline) {
  state = await readState();
  console.log(`Render service state: ${state ?? "unknown"}`);
  if (state === expectedState) {
    console.log(`Render service is ${expectedState}`);
    process.exit(0);
  }
  await new Promise((resolve) => setTimeout(resolve, 5_000));
}

throw new Error(`Timed out waiting for Render service state ${expectedState}`);
