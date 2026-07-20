const assert = require("node:assert/strict");
const test = require("node:test");

const db = require("../dist/lib/db");
const authService = require("../dist/modules/auth/auth.service");
const { buildPoolConfig, parseDotNetConnectionString } = db;
const { app } = require("../dist/app");

const originalGetSessionByToken = authService.getSessionByToken;
const originalQuery = db.query;
const originalQueryOne = db.queryOne;

function session(companyId, permissions) {
  return {
    session: {},
    user: {
      id: 1,
      companyId,
      companyCode: `T${companyId}`,
      companyName: `Tenant ${companyId}`,
      email: "security@example.test",
      fullName: "Security Test",
      roleId: 1,
      roleName: "Test",
      permissions,
    },
  };
}

async function withServer(run) {
  const server = app.listen(0);
  await new Promise((resolve) => server.once("listening", resolve));
  const address = server.address();
  try {
    await run(`http://127.0.0.1:${address.port}`);
  } finally {
    await new Promise((resolve, reject) =>
      server.close((error) => (error ? reject(error) : resolve()))
    );
  }
}

test.afterEach(() => {
  authService.getSessionByToken = originalGetSessionByToken;
  db.query = originalQuery;
  db.queryOne = originalQueryOne;
});

test("protected Node routes reject unauthenticated callers", async () => {
  authService.getSessionByToken = async () => null;
  await withServer(async (baseUrl) => {
    for (const [method, path, body] of [
      ["POST", "/api/devices/register", {}],
      ["POST", "/api/telemetry/ingest", {}],
      ["GET", "/api/telemetry/vehicle/vehicle-1"],
      ["POST", "/api/tenant/configure", {}],
    ]) {
      const response = await fetch(`${baseUrl}${path}`, {
        method,
        headers: body ? { "content-type": "application/json" } : undefined,
        body: method === "GET" ? undefined : JSON.stringify(body),
      });
      assert.equal(response.status, 401, `${method} ${path}`);
    }
  });
});

test("device registration ignores tenant headers and rejects cross-tenant bodies", async () => {
  authService.getSessionByToken = async () => session(7, ["telemetry.devices.manage"]);
  const validDevice = {
    vehicleId: "vehicle-1",
    deviceType: "gps_tracker",
    manufacturer: "Acme",
    model: "SecureTrack",
    imei: "123456789012345",
    approvalCountry: "US",
  };

  await withServer(async (baseUrl) => {
    const forbidden = await fetch(`${baseUrl}/api/devices/register`, {
      method: "POST",
      headers: {
        authorization: "Bearer valid",
        "content-type": "application/json",
        "x-tenant-id": "99",
      },
      body: JSON.stringify({ ...validDevice, tenantId: "99" }),
    });
    assert.equal(forbidden.status, 403);

    const accepted = await fetch(`${baseUrl}/api/devices/register`, {
      method: "POST",
      headers: {
        authorization: "Bearer valid",
        "content-type": "application/json",
        "x-tenant-id": "99",
      },
      body: JSON.stringify(validDevice),
    });
    assert.equal(accepted.status, 201);
    assert.equal((await accepted.json()).data.tenantId, "7");
  });
});

test("telemetry reads are permission checked and tenant isolated", async () => {
  authService.getSessionByToken = async () => session(7, ["telemetry.devices.manage"]);
  const event = {
    vehicleId: "vehicle-shared",
    deviceId: "device-1",
    deviceType: "gps_tracker",
    providerName: "Acme",
    countryCode: "US",
  };

  await withServer(async (baseUrl) => {
    const ingested = await fetch(`${baseUrl}/api/telemetry/ingest`, {
      method: "POST",
      headers: { authorization: "Bearer valid", "content-type": "application/json" },
      body: JSON.stringify(event),
    });
    assert.equal(ingested.status, 201);

    authService.getSessionByToken = async () => session(8, ["telemetry.live_state.read"]);
    const otherTenant = await fetch(`${baseUrl}/api/telemetry/vehicle/vehicle-shared`, {
      headers: { authorization: "Bearer valid" },
    });
    assert.equal(otherTenant.status, 200);
    assert.deepEqual((await otherTenant.json()).data, []);

    authService.getSessionByToken = async () => session(7, []);
    const forbidden = await fetch(`${baseUrl}/api/telemetry/vehicle/vehicle-shared`, {
      headers: { authorization: "Bearer valid" },
    });
    assert.equal(forbidden.status, 403);
  });
});

test("tenant configuration requires management permission and binds session tenant", async () => {
  const payload = {
    primaryCountry: "US",
    industries: ["logistics"],
    enabledDeviceTypes: ["gps_tracker"],
  };

  await withServer(async (baseUrl) => {
    authService.getSessionByToken = async () => session(7, []);
    const denied = await fetch(`${baseUrl}/api/tenant/configure`, {
      method: "POST",
      headers: { authorization: "Bearer valid", "content-type": "application/json" },
      body: JSON.stringify(payload),
    });
    assert.equal(denied.status, 403);

    authService.getSessionByToken = async () => session(7, ["settings:manage"]);
    const configured = await fetch(`${baseUrl}/api/tenant/configure`, {
      method: "POST",
      headers: { authorization: "Bearer valid", "content-type": "application/json" },
      body: JSON.stringify(payload),
    });
    assert.equal(configured.status, 200);
    assert.equal((await configured.json()).data.tenantId, "7");
  });
});

test("PostgreSQL TLS verifies certificates unless explicitly disabled in development", () => {
  const previous = {
    NODE_ENV: process.env.NODE_ENV,
    PGSSL_REJECT_UNAUTHORIZED: process.env.PGSSL_REJECT_UNAUTHORIZED,
    DATABASE_URL: process.env.DATABASE_URL,
    PG_CONNECTION: process.env.PG_CONNECTION,
  };

  try {
    process.env.NODE_ENV = "production";
    process.env.PGSSL_REJECT_UNAUTHORIZED = "false";
    assert.deepEqual(parseDotNetConnectionString("Host=db;SSL Mode=Require").ssl, {
      rejectUnauthorized: true,
    });

    process.env.NODE_ENV = "development";
    assert.deepEqual(parseDotNetConnectionString("Host=db;SSL Mode=Require").ssl, {
      rejectUnauthorized: false,
    });

    process.env.NODE_ENV = "production";
    process.env.DATABASE_URL = "postgresql://user:pass@db/app?sslmode=require";
    delete process.env.PG_CONNECTION;
    assert.deepEqual(buildPoolConfig().ssl, { rejectUnauthorized: true });
  } finally {
    for (const [key, value] of Object.entries(previous)) {
      if (value === undefined) delete process.env[key];
      else process.env[key] = value;
    }
  }
});

test("authentication rejects legacy plaintext demo credentials", async () => {
  let sessionCreated = false;
  db.queryOne = async () => ({
    id: 1,
    companyId: 7,
    companyCode: "T7",
    companyName: "Tenant 7",
    email: "legacy@example.test",
    fullName: "Legacy User",
    roleId: 1,
    roleName: "Admin",
    passwordHash: null,
    demoPassword: "plaintext-password",
    userPermissionsJson: [],
    rolePermissionsJson: [],
  });
  db.query = async () => {
    sessionCreated = true;
    return [];
  };

  const result = await authService.authenticateWithPassword(
    "legacy@example.test",
    "plaintext-password"
  );

  assert.equal(result, null);
  assert.equal(sessionCreated, false);
});
