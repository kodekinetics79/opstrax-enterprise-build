import { createHash, createHmac } from "node:crypto";

export const DEFAULT_OPERATION_COUNT = 10_000;
export const MIN_OPERATION_COUNT = 10_000;
export const PLAN_VERSION = "2.0";

const ENTITY_SEQUENCE = [
  ...Array(15).fill("vehicle"),
  ...Array(15).fill("driver"),
  ...Array(10).fill("device"),
  ...Array(20).fill("job"),
  ...Array(10).fill("inspection"),
  ...Array(10).fill("work_order"),
  ...Array(10).fill("safety_event"),
  ...Array(10).fill("telemetry"),
];

// These are the JSON names accepted by the current EndpointMappings binders/DTOs.
// Keeping them here makes a stale generator fail before it can produce a load file.
export const ENDPOINT_CONTRACTS = Object.freeze({
  vehicle: Object.freeze({
    path: "/api/vehicles",
    source: "CreateVehicle/BindVehicle",
    required: ["vehicleCode"],
    allowed: ["vehicleCode", "type", "make", "model", "year", "vin", "plateNumber", "status", "odometerMiles"],
  }),
  driver: Object.freeze({
    path: "/api/drivers",
    source: "CreateDriver/BindDriver",
    required: ["driverCode", "fullName"],
    allowed: ["driverCode", "fullName", "phone", "email", "licenseNumber", "status"],
  }),
  device: Object.freeze({
    path: "/api/telemetry/devices/provision",
    source: "TelemetryDeviceProvision/DeviceProvisionBody",
    required: ["deviceSerial"],
    allowed: ["deviceSerial", "deviceModel", "provider", "vehicleId", "driverId", "firmwareVersion", "notes", "imei"],
  }),
  job: Object.freeze({
    path: "/api/jobs",
    source: "CreateJob/ValidateJob/BindJob",
    required: ["jobNumber", "customerId", "pickupAddress", "dropoffAddress"],
    allowed: [
      "jobNumber", "jobCode", "customerId", "jobType", "priority", "pickupAddress", "pickupLatitude",
      "pickupLongitude", "dropoffAddress", "dropoffLatitude", "dropoffLongitude", "scheduledStart", "scheduledEnd",
      "slaWindowStart", "slaWindowEnd", "requiredVehicleType", "requiredDriverCertification", "assignedDriverId",
      "assignedVehicleId", "routeId", "status", "eta", "slaStatus", "trackingCode", "riskScore", "revenueEstimate",
      "costEstimate", "marginEstimate", "notes",
    ],
  }),
  inspection: Object.freeze({
    path: "/api/maintenance/inspections",
    source: "MaintInspectionCreate/MaintInspectionBody",
    required: ["vehicleId", "driverId"],
    allowed: [
      "vehicleId", "driverId", "tripId", "inspectionType", "odometerMiles", "engineHours", "notes",
      "checklistItems", "attestationAccepted", "attestation",
    ],
  }),
  work_order: Object.freeze({
    path: "/api/maintenance/work-orders",
    source: "MaintWorkOrderCreate/MaintWorkOrderBody",
    required: ["vehicleId"],
    allowed: ["vehicleId", "title", "serviceType", "description", "priority", "defectId", "estimatedCost", "scheduledAt"],
  }),
  safety_event: Object.freeze({
    path: "/api/safety/events",
    source: "CreateSafetyEvent/BindSafety",
    required: ["eventType", "severity"],
    allowed: [
      "eventNumber", "eventType", "severity", "driverId", "vehicleId", "jobId", "routeId", "locationDescription",
      "speed", "postedSpeedLimit", "occurredAt", "riskScore", "aiSummary", "recommendedAction",
    ],
  }),
  telemetry: Object.freeze({
    path: "/api/telemetry/ingest",
    source: "TelemetryIngest/TelemetryPingBody",
    required: ["lat", "lng"],
    allowed: [
      "lat", "lng", "speedMph", "heading", "eventType", "engineStatus", "fuelLevel", "odometerMiles",
      "batteryVoltage", "accuracyMeters", "eventTime", "sourceChannel", "clientGeneratedId", "correlationId", "causationId",
    ],
  }),
});

const VIN_ALPHABET = "0123456789ABCDEFGHJKLMNPRSTUVWXYZ";
const VIN_TRANSLITERATION = Object.freeze({
  A: 1, B: 2, C: 3, D: 4, E: 5, F: 6, G: 7, H: 8,
  J: 1, K: 2, L: 3, M: 4, N: 5, P: 7, R: 9,
  S: 2, T: 3, U: 4, V: 5, W: 6, X: 7, Y: 8, Z: 9,
});
const VIN_WEIGHTS = [8, 7, 6, 5, 4, 3, 2, 10, 0, 9, 8, 7, 6, 5, 4, 3, 2];

function assertInteger(name, value, min) {
  if (!Number.isSafeInteger(value) || value < min) throw new Error(`${name} must be an integer >= ${min}`);
}

function xorshift32(initialSeed) {
  let state = initialSeed >>> 0;
  if (state === 0) state = 0x6d2b79f5;
  return () => {
    state ^= state << 13;
    state ^= state >>> 17;
    state ^= state << 5;
    return (state >>> 0) / 0x1_0000_0000;
  };
}

function pad(value, size = 6) {
  return String(value).padStart(size, "0");
}

function pick(random, values) {
  return values[Math.floor(random() * values.length)];
}

function ref(kind, index) {
  return `QA-${kind.toUpperCase()}-${pad(index)}`;
}

function refValue(clientReference) {
  return Object.freeze({ $ref: clientReference });
}

function fixtureValue(name) {
  return Object.freeze({ $fixture: name });
}

function encodeVinSequence(value, width) {
  let remaining = value;
  let result = "";
  while (result.length < width) {
    result = VIN_ALPHABET[remaining % VIN_ALPHABET.length] + result;
    remaining = Math.floor(remaining / VIN_ALPHABET.length);
  }
  return result;
}

function vinCharacterValue(character) {
  if (/\d/.test(character)) return Number(character);
  return VIN_TRANSLITERATION[character];
}

function vinCheckDigit(vin) {
  const total = [...vin].reduce((sum, character, index) => sum + vinCharacterValue(character) * VIN_WEIGHTS[index], 0);
  const remainder = total % 11;
  return remainder === 10 ? "X" : String(remainder);
}

export function syntheticVin(index) {
  assertInteger("VIN index", index, 1);
  // 1KK is a deliberately synthetic WMI. Position 9 is calculated per ISO 3779/NHTSA.
  const withoutCheck = `1KK${encodeVinSequence(index, 5)}0TA${pad(index % 1_000_000, 6)}`;
  return `${withoutCheck.slice(0, 8)}${vinCheckDigit(withoutCheck)}${withoutCheck.slice(9)}`;
}

export function isValidVin(value) {
  return typeof value === "string" && /^[A-HJ-NPR-Z0-9]{17}$/.test(value) && value[8] === vinCheckDigit(value);
}

function luhnCheckDigit(baseDigits) {
  const digits = `${baseDigits}0`.split("").map(Number);
  const sum = digits.reduce((total, digit, index) => {
    if ((digits.length - index) % 2 === 0) {
      const doubled = digit * 2;
      return total + (doubled > 9 ? doubled - 9 : doubled);
    }
    return total + digit;
  }, 0);
  return String((10 - (sum % 10)) % 10);
}

export function syntheticImei(index) {
  assertInteger("IMEI index", index, 1);
  const base = `99999999${pad(index % 1_000_000, 6)}`;
  return `${base}${luhnCheckDigit(base)}`;
}

export function isValidImei(value) {
  if (typeof value !== "string" || !/^\d{15}$/.test(value)) return false;
  const sum = value.split("").map(Number).reduce((total, digit, index, digits) => {
    if ((digits.length - index) % 2 === 0) {
      const doubled = digit * 2;
      return total + (doubled > 9 ? doubled - 9 : doubled);
    }
    return total + digit;
  }, 0);
  return sum % 10 === 0;
}

export function validateOperationContract(operation) {
  const contract = ENDPOINT_CONTRACTS[operation.kind];
  if (!contract) throw new Error(`No API contract for operation kind ${operation.kind}`);
  if (operation.request.method !== "POST" || operation.request.path !== contract.path) {
    throw new Error(`${operation.kind} must POST ${contract.path}`);
  }
  const body = operation.request.body;
  if (!body || typeof body !== "object" || Array.isArray(body)) throw new Error(`${operation.kind} body must be an object`);
  for (const key of contract.required) {
    if (!(key in body) || body[key] === null || body[key] === "") throw new Error(`${operation.kind} is missing required key ${key}`);
  }
  for (const key of Object.keys(body)) {
    if (!contract.allowed.includes(key)) throw new Error(`${operation.kind} body contains unsupported key ${key}`);
  }
  if (operation.kind === "telemetry" && operation.request.auth?.type !== "device-hmac") {
    throw new Error("telemetry operations require device-hmac auth metadata");
  }
  return true;
}

function operationFor(kind, ordinal, perKindIndex, random, pools) {
  const operationId = `op-${pad(ordinal, 7)}`;
  const clientReference = ref(kind, perKindIndex);
  const base = { operationId, kind, clientReference, authorizedScope: "disposable-staging-tenant" };

  if (kind === "vehicle") {
    pools.vehicle.push(clientReference);
    return {
      ...base,
      request: {
        method: "POST",
        path: ENDPOINT_CONTRACTS.vehicle.path,
        body: {
          vehicleCode: clientReference,
          type: "Truck",
          vin: syntheticVin(perKindIndex),
          make: pick(random, ["Freightliner", "Volvo", "Kenworth", "International"]),
          model: pick(random, ["Cascadia", "VNL", "T680", "LT"]),
          year: 2021 + (perKindIndex % 6),
          plateNumber: `QA${pad(perKindIndex, 6)}`,
          status: "Available",
          odometerMiles: 10_000 + perKindIndex * 17,
        },
      },
    };
  }

  if (kind === "driver") {
    pools.driver.push(clientReference);
    return {
      ...base,
      request: {
        method: "POST",
        path: ENDPOINT_CONTRACTS.driver.path,
        body: {
          driverCode: clientReference,
          fullName: `QA Driver ${pad(perKindIndex)}`,
          email: `qa.driver.${pad(perKindIndex)}@example.test`,
          phone: `+1202555${pad(perKindIndex % 10_000, 4)}`,
          licenseNumber: `QA-LIC-${pad(perKindIndex, 8)}`,
          status: "Available",
        },
      },
    };
  }

  const vehicleRef = pools.vehicle[perKindIndex % pools.vehicle.length];
  const driverRef = pools.driver[perKindIndex % pools.driver.length];

  if (kind === "device") {
    pools.device.push(clientReference);
    return {
      ...base,
      dependsOn: [vehicleRef, driverRef],
      request: {
        method: "POST",
        path: ENDPOINT_CONTRACTS.device.path,
        body: {
          deviceSerial: clientReference,
          imei: syntheticImei(perKindIndex),
          deviceModel: pick(random, ["QA-GT06", "QA-HMAC", "QA-J1939"]),
          provider: "QA Synthetic",
          vehicleId: refValue(vehicleRef),
          driverId: refValue(driverRef),
          firmwareVersion: `qa-${1 + (perKindIndex % 4)}.${perKindIndex % 10}`,
          notes: "Synthetic launch-certification device; no physical asset.",
        },
      },
    };
  }

  if (kind === "job") {
    pools.job.push(clientReference);
    return {
      ...base,
      request: {
        method: "POST",
        path: ENDPOINT_CONTRACTS.job.path,
        body: {
          jobNumber: clientReference,
          customerId: fixtureValue("customerId"),
          jobType: "Delivery",
          pickupAddress: `${100 + (perKindIndex % 900)} Test Origin Ave, Richmond, VA`,
          dropoffAddress: `${200 + (perKindIndex % 800)} Test Destination Rd, Baltimore, MD`,
          priority: pick(random, ["Low", "Normal", "High"]),
          status: "Unassigned",
          trackingCode: `QA-TRACK-${pad(perKindIndex, 8)}`,
          notes: "Authorized synthetic launch-plan job.",
        },
      },
    };
  }

  if (kind === "inspection") {
    return {
      ...base,
      dependsOn: [vehicleRef, driverRef],
      request: {
        method: "POST",
        path: ENDPOINT_CONTRACTS.inspection.path,
        headers: { "Idempotency-Key": `qa-dvir-${pad(perKindIndex, 8)}` },
        body: {
          vehicleId: refValue(vehicleRef),
          driverId: refValue(driverRef),
          inspectionType: perKindIndex % 2 ? "post_trip" : "pre_trip",
          odometerMiles: 10_000 + perKindIndex * 19,
          notes: "Authorized synthetic inspection",
          checklistItems: [
            { category: "Brakes", itemName: "Service brake", result: "pass", severity: "minor", notes: "Synthetic pass" },
            { category: "Tires", itemName: "Tire condition", result: "pass", severity: "minor", notes: "Synthetic pass" },
          ],
        },
      },
    };
  }

  if (kind === "work_order") {
    return {
      ...base,
      dependsOn: [vehicleRef],
      request: {
        method: "POST",
        path: ENDPOINT_CONTRACTS.work_order.path,
        body: {
          vehicleId: refValue(vehicleRef),
          title: `${clientReference} synthetic maintenance`,
          serviceType: pick(random, ["Inspection", "Oil service", "Brake service", "Tire rotation"]),
          description: "Authorized disposable staging work order.",
          priority: pick(random, ["Low", "Medium", "High"]),
          estimatedCost: Number((50 + random() * 1_200).toFixed(2)),
        },
      },
    };
  }

  if (kind === "safety_event") {
    return {
      ...base,
      dependsOn: [vehicleRef, driverRef],
      request: {
        method: "POST",
        path: ENDPOINT_CONTRACTS.safety_event.path,
        body: {
          eventNumber: clientReference,
          eventType: pick(random, ["Harsh Braking", "Speeding", "Following Distance"]),
          severity: pick(random, ["Low", "Medium", "High"]),
          vehicleId: refValue(vehicleRef),
          driverId: refValue(driverRef),
          locationDescription: "Synthetic launch route",
          speed: 35 + (perKindIndex % 30),
          postedSpeedLimit: 55,
          riskScore: 10 + (perKindIndex % 70),
          aiSummary: "Synthetic safety event for launch certification.",
          recommendedAction: "No operational action; disposable QA data.",
        },
      },
    };
  }

  const deviceRef = pools.device[perKindIndex % pools.device.length];
  return {
    ...base,
    dependsOn: [deviceRef],
    request: {
      method: "POST",
      path: ENDPOINT_CONTRACTS.telemetry.path,
      auth: { type: "device-hmac", deviceRef },
      body: {
        lat: Number((38.7 + random() * 0.35).toFixed(6)),
        lng: Number((-77.6 + random() * 0.35).toFixed(6)),
        speedMph: Number((random() * 65).toFixed(1)),
        heading: Math.floor(random() * 360),
        odometerMiles: Number((10_000 + perKindIndex * 0.25).toFixed(2)),
        eventType: "ping",
        sourceChannel: "qa-launch-plan",
        clientGeneratedId: `qa-telemetry-${pad(perKindIndex, 8)}`,
      },
    },
  };
}

function negativeCases() {
  return [
    { caseId: "NEG-001", name: "duplicate vehicle VIN", method: "POST", path: "/api/vehicles", expectedStatus: 409, body: { vehicleCode: "QA-VEHICLE-DUP", vin: syntheticVin(1) } },
    { caseId: "NEG-002", name: "driver missing full name", method: "POST", path: "/api/drivers", expectedStatus: 400, body: { driverCode: "QA-DRIVER-MISSING-NAME" } },
    { caseId: "NEG-003", name: "job missing customer", method: "POST", path: "/api/jobs", expectedStatus: 400, body: { jobNumber: "QA-JOB-MISSING-CUSTOMER", pickupAddress: "A", dropoffAddress: "B" } },
    { caseId: "NEG-004", name: "cross-tenant DVIR references", method: "POST", path: "/api/maintenance/inspections", expectedStatus: 404, body: { vehicleId: fixtureValue("crossTenantVehicleId"), driverId: fixtureValue("crossTenantDriverId") } },
    { caseId: "NEG-005", name: "cross-tenant work-order vehicle", method: "POST", path: "/api/maintenance/work-orders", expectedStatus: 400, body: { vehicleId: fixtureValue("crossTenantVehicleId") } },
    { caseId: "NEG-006", name: "safety event missing severity", method: "POST", path: "/api/safety/events", expectedStatus: 400, body: { eventType: "Speeding" } },
    { caseId: "NEG-007", name: "duplicate device serial", method: "POST", path: "/api/telemetry/devices/provision", expectedStatus: 409, body: { deviceSerial: ref("device", 1) } },
    { caseId: "NEG-008", name: "latitude outside range", method: "POST", path: "/api/telemetry/ingest", expectedStatus: 422, auth: "valid-device-hmac", mutation: "lat=91" },
    { caseId: "NEG-009", name: "replayed telemetry nonce", method: "POST", path: "/api/telemetry/ingest", expectedStatus: 409, auth: "repeat-valid-signed-request" },
    { caseId: "NEG-010", name: "stale telemetry HMAC timestamp", method: "POST", path: "/api/telemetry/ingest", expectedStatus: 422, auth: "valid-signature-over-timestamp-older-than-60-seconds" },
  ];
}

function resolveTemplate(value, resources, fixtures) {
  if (Array.isArray(value)) return value.map((item) => resolveTemplate(item, resources, fixtures));
  if (!value || typeof value !== "object") return value;
  if (Object.keys(value).length === 1 && typeof value.$ref === "string") {
    const resource = resources[value.$ref];
    if (!resource || !Number.isSafeInteger(resource.id) || resource.id <= 0) throw new Error(`Unresolved resource reference ${value.$ref}`);
    return resource.id;
  }
  if (Object.keys(value).length === 1 && typeof value.$fixture === "string") {
    const fixture = fixtures[value.$fixture];
    if (!Number.isSafeInteger(fixture) || fixture <= 0) throw new Error(`Missing positive fixture ${value.$fixture}`);
    return fixture;
  }
  return Object.fromEntries(Object.entries(value).map(([key, item]) => [key, resolveTemplate(item, resources, fixtures)]));
}

export function materializeOperation(operation, context = {}) {
  validateOperationContract(operation);
  const resources = context.resources || {};
  const body = resolveTemplate(operation.request.body, resources, context.fixtures || {});
  const rawBody = JSON.stringify(body);
  const headers = { "Content-Type": "application/json", ...(operation.request.headers || {}) };
  if (operation.request.auth?.type === "device-hmac") {
    const device = resources[operation.request.auth.deviceRef];
    if (!device?.apiKey || !device?.hmacSecret) throw new Error(`Missing device credentials for ${operation.request.auth.deviceRef}`);
    const timestamp = String(context.timestamp ?? Math.floor(Date.now() / 1000));
    const runId = String(context.runId || "dry-run").replace(/[^a-zA-Z0-9-]/g, "-").slice(0, 48);
    const nonce = `${runId}-${operation.operationId}`;
    const bodyHash = createHash("sha256").update(rawBody).digest("hex");
    const canonical = `${operation.request.method}\n${operation.request.path}\n${timestamp}\n${nonce}\n${bodyHash}`;
    headers["X-Device-Key"] = device.apiKey;
    headers["X-Timestamp"] = timestamp;
    headers["X-Nonce"] = nonce;
    headers["X-Signature"] = createHmac("sha256", Buffer.from(device.hmacSecret, "utf8")).update(canonical).digest("hex");
  } else if (context.bearerToken) {
    headers.Authorization = `Bearer ${context.bearerToken}`;
  }
  return Object.freeze({ method: operation.request.method, path: operation.request.path, headers, body, rawBody });
}

export function captureOperationResult(operation, responsePayload, resources = {}) {
  const data = responsePayload?.data ?? responsePayload?.Data ?? responsePayload;
  if (!Number.isSafeInteger(data?.id) || data.id <= 0) throw new Error(`${operation.operationId} response did not contain a positive data.id`);
  const captured = { id: data.id };
  if (operation.kind === "device") {
    if (typeof data.apiKey !== "string" || typeof data.hmacSecret !== "string") {
      throw new Error(`${operation.operationId} provision response omitted one-time device credentials`);
    }
    captured.apiKey = data.apiKey;
    captured.hmacSecret = data.hmacSecret;
  }
  resources[operation.clientReference] = captured;
  return captured;
}

export function stableJson(value) {
  if (Array.isArray(value)) return `[${value.map(stableJson).join(",")}]`;
  if (value && typeof value === "object") {
    return `{${Object.keys(value).sort().map((key) => `${JSON.stringify(key)}:${stableJson(value[key])}`).join(",")}}`;
  }
  return JSON.stringify(value);
}

export function planSha256(plan) {
  return createHash("sha256").update(stableJson(plan)).digest("hex");
}

export function generateLaunchPlan({ count = DEFAULT_OPERATION_COUNT, seed = 20260811 } = {}) {
  assertInteger("count", count, MIN_OPERATION_COUNT);
  assertInteger("seed", seed, 1);
  const random = xorshift32(seed);
  const pools = { vehicle: [], driver: [], device: [], job: [] };
  const counters = Object.fromEntries([...new Set(ENTITY_SEQUENCE)].map((kind) => [kind, 0]));
  const operations = [];

  for (let ordinal = 1; ordinal <= count; ordinal += 1) {
    const kind = ENTITY_SEQUENCE[(ordinal - 1) % ENTITY_SEQUENCE.length];
    counters[kind] += 1;
    const operation = operationFor(kind, ordinal, counters[kind], random, pools);
    validateOperationContract(operation);
    operations.push(operation);
  }

  const negatives = negativeCases();
  return Object.freeze({
    planVersion: PLAN_VERSION,
    seed,
    operationCount: operations.length,
    negativeCaseCount: negatives.length,
    authorization: {
      environment: "isolated-staging-only",
      dataClass: "synthetic-no-real-PII",
      productionAllowed: false,
      networkCallsDuringGeneration: 0,
    },
    requiredFixtures: ["customerId"],
    optionalNegativeFixtures: ["crossTenantVehicleId", "crossTenantDriverId"],
    entityCounts: counters,
    operations,
    negativeCases: negatives,
  });
}

export function summarizePlan(plan) {
  return {
    planVersion: plan.planVersion,
    seed: plan.seed,
    operationCount: plan.operationCount,
    negativeCaseCount: plan.negativeCases.length,
    entityCounts: plan.entityCounts,
    sha256: planSha256(plan),
    networkCalls: 0,
  };
}
