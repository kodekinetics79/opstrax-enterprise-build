import assert from "node:assert/strict";
import { createHash, createHmac } from "node:crypto";
import { spawnSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";
import {
  ENDPOINT_CONTRACTS,
  captureOperationResult,
  generateLaunchPlan,
  isValidImei,
  isValidVin,
  materializeOperation,
  planSha256,
  summarizePlan,
  syntheticImei,
  syntheticVin,
  validateOperationContract,
} from "./launch_plan.mjs";
import {
  dryRunExecutablePlan,
  executeLaunchPlan,
  resolveLaunchExecution,
  validateExecutablePlan,
} from "./launch_execution.mjs";

const directory = path.dirname(fileURLToPath(import.meta.url));
const defaultPlan = generateLaunchPlan();

test("default plan contains exactly 10,000 authorized synthetic operations", () => {
  assert.equal(defaultPlan.operationCount, 10_000);
  assert.equal(defaultPlan.operations.length, 10_000);
  assert.equal(defaultPlan.authorization.productionAllowed, false);
  assert.equal(defaultPlan.authorization.networkCallsDuringGeneration, 0);
});

test("same seed produces the same plan hash", () => {
  assert.equal(planSha256(generateLaunchPlan({ seed: 77 })), planSha256(generateLaunchPlan({ seed: 77 })));
});

test("different seeds produce different plan hashes", () => {
  assert.notEqual(planSha256(generateLaunchPlan({ seed: 77 })), planSha256(generateLaunchPlan({ seed: 78 })));
});

test("operation and client references are unique", () => {
  assert.equal(new Set(defaultPlan.operations.map((item) => item.operationId)).size, 10_000);
  assert.equal(new Set(defaultPlan.operations.map((item) => item.clientReference)).size, 10_000);
});

test("plan covers every core launch endpoint", () => {
  for (const kind of Object.keys(ENDPOINT_CONTRACTS)) assert.ok(defaultPlan.entityCounts[kind] > 0, `${kind} missing`);
  assert.equal(Object.values(defaultPlan.entityCounts).reduce((sum, value) => sum + value, 0), 10_000);
});

test("all generated operations satisfy their current binder contract", () => {
  for (const operation of defaultPlan.operations) assert.equal(validateOperationContract(operation), true);
});

test("contract table points at real current routes and source binders", () => {
  const source = fs.readFileSync(path.resolve(directory, "../../backend-dotnet/Controllers/EndpointMappings.cs"), "utf8");
  for (const contract of Object.values(ENDPOINT_CONTRACTS)) {
    assert.ok(source.includes(`MapPost(\"${contract.path}\"`), `missing backend route ${contract.path}`);
    for (const sourceName of contract.source.split("/")) {
      assert.ok(source.includes(sourceName), `missing source contract ${sourceName}`);
    }
  }
  for (const key of ["vehicleCode", "plateNumber", "odometerMiles", "driverCode", "fullName", "jobNumber", "customerId", "assignedVehicleId", "assignedDriverId", "eventNumber", "occurredAt"]) {
    assert.ok(source.includes(`\"${key}\"`), `backend binder no longer contains ${key}`);
  }
  for (const dtoField of ["DeviceSerial", "Imei", "VehicleId", "DriverId", "InspectionType", "ChecklistItems", "ServiceType", "EstimatedCost", "Lat", "Lng", "SpeedMph", "EventTime"]) {
    assert.ok(source.includes(dtoField), `backend DTO no longer contains ${dtoField}`);
  }
});

test("legacy ignored field names are absent from positive request bodies", () => {
  const forbidden = new Set([
    "licensePlate", "odometer", "firstName", "lastName", "licenseState", "customerName", "pickupScheduledAtUtc",
    "deliveryScheduledAtUtc", "vehicleRef", "driverRef", "eventCode", "occurredAtUtc", "capturedAtUtc", "latitude",
    "longitude", "speedKph", "odometerKm", "source",
  ]);
  for (const operation of defaultPlan.operations) {
    for (const key of Object.keys(operation.request.body)) assert.equal(forbidden.has(key), false, `${operation.kind}.${key}`);
  }
});

test("positive synthetic VINs have valid length, charset, and check digit", () => {
  const vins = defaultPlan.operations.filter((item) => item.kind === "vehicle").map((item) => item.request.body.vin);
  assert.ok(vins.length > 0);
  assert.ok(vins.every(isValidVin));
  assert.ok(vins.every((vin) => vin.length === 17 && !/[IOQ]/.test(vin)));
  assert.equal(new Set(vins).size, vins.length);
  assert.equal(isValidVin("1QA00000000000000"), false);
  assert.equal(isValidVin(`${syntheticVin(1).slice(0, 16)}I`), false);
});

test("positive synthetic IMEIs are 15 digits with a valid Luhn check digit", () => {
  const imeis = defaultPlan.operations.filter((item) => item.kind === "device").map((item) => item.request.body.imei);
  assert.ok(imeis.length > 0);
  assert.ok(imeis.every(isValidImei));
  assert.equal(new Set(imeis).size, imeis.length);
  assert.equal(isValidImei(`${syntheticImei(1).slice(0, 14)}${(Number(syntheticImei(1)[14]) + 1) % 10}`), false);
});

test("job materialization requires and resolves an authorized customer fixture", () => {
  const operation = defaultPlan.operations.find((item) => item.kind === "job");
  assert.throws(() => materializeOperation(operation), /fixture customerId/);
  const request = materializeOperation(operation, { fixtures: { customerId: 42 }, bearerToken: "unit-test-token" });
  assert.equal(request.body.customerId, 42);
  assert.equal(request.body.status, "Unassigned");
  assert.equal(request.headers.Authorization, "Bearer unit-test-token");
});

test("dependent entity references materialize to captured positive IDs", () => {
  const operation = defaultPlan.operations.find((item) => item.kind === "inspection");
  const [vehicleRef, driverRef] = operation.dependsOn;
  const request = materializeOperation(operation, {
    resources: { [vehicleRef]: { id: 101 }, [driverRef]: { id: 202 } },
  });
  assert.equal(request.body.vehicleId, 101);
  assert.equal(request.body.driverId, 202);
  assert.equal(request.headers["Idempotency-Key"].startsWith("qa-dvir-"), true);
});

test("telemetry materialization emits the backend canonical HMAC headers", () => {
  const operation = defaultPlan.operations.find((item) => item.kind === "telemetry");
  const deviceRef = operation.request.auth.deviceRef;
  const secret = "unit-test-hmac-secret";
  const request = materializeOperation(operation, {
    resources: { [deviceRef]: { id: 303, apiKey: "unit-test-api-key", hmacSecret: secret } },
    timestamp: 1_700_000_000,
    runId: "contract-test",
  });
  const bodyHash = createHash("sha256").update(request.rawBody).digest("hex");
  const canonical = `POST\n/api/telemetry/ingest\n1700000000\ncontract-test-${operation.operationId}\n${bodyHash}`;
  const expected = createHmac("sha256", Buffer.from(secret, "utf8")).update(canonical).digest("hex");
  assert.equal(request.headers["X-Device-Key"], "unit-test-api-key");
  assert.equal(request.headers["X-Timestamp"], "1700000000");
  assert.equal(request.headers["X-Signature"], expected);
  assert.deepEqual(Object.keys(request.body).sort(), ["clientGeneratedId", "eventType", "heading", "lat", "lng", "odometerMiles", "sourceChannel", "speedMph"].sort());
});

test("provision results bind IDs and one-time device credentials in memory", () => {
  const operation = defaultPlan.operations.find((item) => item.kind === "device");
  const resources = {};
  const captured = captureOperationResult(operation, { data: { id: 9, apiKey: "key", hmacSecret: "secret" } }, resources);
  assert.deepEqual(captured, { id: 9, apiKey: "key", hmacSecret: "secret" });
  assert.deepEqual(resources[operation.clientReference], captured);
  assert.throws(() => captureOperationResult(operation, { data: { id: 9 } }, {}), /omitted one-time device credentials/);
});

test("unsupported body fields fail contract validation", () => {
  const original = defaultPlan.operations.find((item) => item.kind === "driver");
  const invalid = { ...original, request: { ...original.request, body: { ...original.request.body, firstName: "ignored" } } };
  assert.throws(() => validateOperationContract(invalid), /unsupported key firstName/);
});

test("negative pack covers tenant, validation, duplicate, replay, and stale boundaries", () => {
  assert.equal(defaultPlan.negativeCases.length, 10);
  const names = defaultPlan.negativeCases.map((item) => item.name).join(" ");
  for (const fragment of ["cross-tenant", "missing", "duplicate", "outside range", "replayed", "stale"]) {
    assert.match(names, new RegExp(fragment, "i"));
  }
  assert.ok(defaultPlan.negativeCases.every((item) => [400, 404, 409, 422].includes(item.expectedStatus)));
});

test("synthetic email addresses use the reserved test domain", () => {
  const emails = defaultPlan.operations.map((item) => item.request.body.email).filter(Boolean);
  assert.ok(emails.length > 0);
  assert.ok(emails.every((email) => email.endsWith("@example.test")));
});

test("plan contains no credential values", () => {
  const serialized = JSON.stringify(defaultPlan).toLowerCase();
  for (const forbidden of ["bearer ", "password", "clientsecret", "hmacsecret\":", "apikey\":"]) {
    assert.equal(serialized.includes(forbidden), false, `found ${forbidden}`);
  }
});

test("operation count below launch minimum is rejected", () => {
  assert.throws(() => generateLaunchPlan({ count: 9_999 }), /count must be an integer >= 10000/);
});

test("seed must be a positive integer", () => {
  assert.throws(() => generateLaunchPlan({ seed: 0 }), /seed must be an integer/);
  assert.throws(() => generateLaunchPlan({ seed: 1.5 }), /seed must be an integer/);
});

test("summary reconciles plan counts and fingerprint", () => {
  const summary = summarizePlan(defaultPlan);
  assert.equal(summary.operationCount, defaultPlan.operations.length);
  assert.equal(summary.negativeCaseCount, defaultPlan.negativeCases.length);
  assert.match(summary.sha256, /^[a-f0-9]{64}$/);
  assert.equal(summary.networkCalls, 0);
});

test("dry-run CLI succeeds without writing a plan", () => {
  const result = spawnSync(process.execPath, [path.join(directory, "generate_launch_plan.mjs"), "--dry-run"], {
    encoding: "utf8",
    timeout: 30_000,
  });
  assert.equal(result.status, 0, result.stderr);
  const summary = JSON.parse(result.stdout);
  assert.equal(summary.operationCount, 10_000);
  assert.equal(summary.networkCalls, 0);
});

test("executable dry-run materializes all 10,000 operations with zero network", () => {
  assert.equal(validateExecutablePlan(defaultPlan), true);
  assert.deepEqual(dryRunExecutablePlan(defaultPlan), {
    materializedOperations: 10_000,
    networkCalls: 0,
    unresolvedReferences: 0,
  });
});

test("execution safety refuses both known production hosts", () => {
  for (const hostname of ["opstrax.vercel.app", "osptrax-fleet-management.onrender.com"]) {
    assert.throws(() => resolveLaunchExecution({
      LAUNCH_TARGET_ENV: "staging",
      LAUNCH_API_URL: `https://${hostname}`,
      LAUNCH_STAGING_HOSTS: hostname,
      LAUNCH_DISPOSABLE_TENANT_ACK: "I_UNDERSTAND_THIS_WRITES_TEST_DATA",
      LAUNCH_BEARER_TOKEN: "unit-test",
      LAUNCH_CUSTOMER_ID: "1",
      LAUNCH_OPERATION_CAP: "10000",
    }), /production host/);
  }
});

test("execution safety requires exact staging host membership and acknowledgement", () => {
  const base = {
    LAUNCH_TARGET_ENV: "staging",
    LAUNCH_API_URL: "https://api-staging.example.test",
    LAUNCH_STAGING_HOSTS: "api-staging.example.test",
    LAUNCH_DISPOSABLE_TENANT_ACK: "I_UNDERSTAND_THIS_WRITES_TEST_DATA",
    LAUNCH_BEARER_TOKEN: "unit-test",
    LAUNCH_CUSTOMER_ID: "42",
    LAUNCH_OPERATION_CAP: "10000",
  };
  assert.equal(resolveLaunchExecution(base).fixtures.customerId, 42);
  assert.throws(() => resolveLaunchExecution({ ...base, LAUNCH_STAGING_HOSTS: "other.example.test" }), /explicitly listed/);
  assert.throws(() => resolveLaunchExecution({ ...base, LAUNCH_DISPOSABLE_TENANT_ACK: "yes" }), /acknowledgement/);
  assert.throws(() => resolveLaunchExecution({ ...base, LAUNCH_TARGET_ENV: "production" }), /exactly staging/);
  assert.throws(() => resolveLaunchExecution({ ...base, LAUNCH_OPERATION_CAP: "20001" }), /hard cap/);
});

test("mock execution uses every materialized request and captures dependencies", async () => {
  let calls = 0;
  const fetchImpl = async (_url, options) => {
    calls += 1;
    assert.equal(options.method, "POST");
    const body = JSON.parse(options.body);
    const provision = body.deviceSerial?.startsWith("QA-DEVICE-");
    return {
      ok: true,
      status: 200,
      text: async () => JSON.stringify({ data: {
        id: calls,
        ...(provision ? { apiKey: `key-${calls}`, hmacSecret: `secret-${calls}` } : {}),
      } }),
    };
  };
  const result = await executeLaunchPlan(defaultPlan, {
    apiBaseUrl: "https://api-staging.example.test",
    bearerToken: "unit-test",
    fixtures: { customerId: 42 },
    operationCap: 10_000,
    runId: "unit-run",
  }, { fetchImpl });
  assert.deepEqual(result, { completed: 10_000, failed: 0, networkCalls: 10_000 });
  assert.equal(calls, 10_000);
});
