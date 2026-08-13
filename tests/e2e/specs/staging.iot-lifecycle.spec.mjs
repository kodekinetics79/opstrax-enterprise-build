import crypto from "node:crypto";
import fs from "node:fs";
import path from "node:path";
import { request as playwrightRequest } from "@playwright/test";
import { test, expect } from "../fixtures.mjs";
import { expectAuthenticatedRoute } from "./helpers.mjs";

const SESSION_KEYS = ["opstrax.session.v3", "opstrax.session.v2", "opstrax.session"];

function apiUrl(target, relativePath) {
  const base = target.apiBaseUrl.endsWith("/") ? target.apiBaseUrl : `${target.apiBaseUrl}/`;
  return new URL(relativePath.replace(/^\//, ""), base).toString();
}

function sessionFromStorageState(state) {
  for (const origin of state.origins || []) {
    for (const entry of origin.localStorage || []) {
      if (!SESSION_KEYS.includes(entry.name)) continue;
      const parsed = JSON.parse(entry.value);
      const session = parsed.session || parsed;
      if (session?.token) return session;
    }
  }
  throw new Error("Authenticated storage state does not contain an OpsTrax bearer session");
}

function authHeaders(session) {
  const companyId = session.company?.id
    ?? session.company?.companyId
    ?? session.user?.companyId
    ?? session.user?.company_id;
  if (!companyId) throw new Error("Authenticated storage state does not identify a tenant");
  return {
    Authorization: `Bearer ${session.token}`,
    "X-Opstrax-Tenant-Id": String(companyId),
    ...(session.csrfToken ? { "X-CSRF-Token": String(session.csrfToken) } : {}),
  };
}

async function jsonResponse(response) {
  let body;
  try {
    body = await response.json();
  } catch {
    throw new Error(`${response.url()} returned non-JSON HTTP ${response.status()}`);
  }
  return { response, body };
}

async function api(context, target, relativePath, session, options = {}) {
  const method = options.method || "GET";
  const response = await context.fetch(apiUrl(target, relativePath), {
    method,
    headers: { ...authHeaders(session), ...(options.headers || {}) },
    ...(options.data === undefined ? {} : { data: options.data }),
  });
  return jsonResponse(response);
}

function expectOk(result, expectedStatus, operation) {
  expect(result.response.status(), operation).toBe(expectedStatus);
  expect(result.body?.success, `${operation} response envelope`).toBe(true);
  expect(result.body?.data, `${operation} response data`).toBeTruthy();
  return result.body.data;
}

function signedTelemetryHeaders(apiKey, hmacSecret, rawBody, ingestPath) {
  const timestamp = String(Math.floor(Date.now() / 1000));
  const nonce = crypto.randomUUID();
  const bodyHash = crypto.createHash("sha256").update(rawBody, "utf8").digest("hex");
  const canonical = `POST\n${ingestPath}\n${timestamp}\n${nonce}\n${bodyHash}`;
  const signature = crypto.createHmac("sha256", Buffer.from(hmacSecret, "utf8"))
    .update(canonical, "utf8")
    .digest("hex");
  return {
    "Content-Type": "application/json",
    "X-Device-Key": apiKey,
    "X-Timestamp": timestamp,
    "X-Nonce": nonce,
    "X-Signature": signature,
  };
}

async function ingest(context, target, credentials, payload) {
  const relativePath = "/api/telemetry/ingest";
  const url = apiUrl(target, relativePath);
  const rawBody = JSON.stringify(payload);
  // The deployed API is expected to expose /api directly. Refuse to guess how a
  // path-prefixing proxy canonicalizes HMAC input because a false signature result
  // would not certify the device protocol.
  const pathname = new URL(url).pathname;
  if (pathname !== relativePath) {
    throw new Error("IoT lifecycle certification requires E2E_API_BASE_URL at the API origin, without a path prefix");
  }
  return jsonResponse(await context.fetch(url, {
    method: "POST",
    headers: signedTelemetryHeaders(credentials.apiKey, credentials.hmacSecret, rawBody, pathname),
    data: rawBody,
  }));
}

function installationFromDetail(detail, installationId) {
  const rows = detail.installationHistory || detail.installations || [];
  return rows.find((row) => String(row.id) === String(installationId));
}

function positionFor(rows, vehicleId) {
  return rows.find((row) => String(row.vehicleId) === String(vehicleId));
}

test("authenticated device lifecycle preserves temporal vehicle identity and tenant isolation", async ({
  page,
  target,
  authConfigured,
  iotLifecycleGate,
  runId,
}, testInfo) => {
  test.skip(!authConfigured, "Provide E2E_TENANT_AUTH_STATE for the staging fleet administrator");
  test.skip(!iotLifecycleGate.enabled, iotLifecycleGate.reasons.join("; "));

  await expectAuthenticatedRoute(page, "/iot-devices", /IoT|Device/i);

  const tenantState = await page.context().storageState();
  const tenantSession = sessionFromStorageState(tenantState);
  const crossTenantStatePath = path.resolve(process.env.E2E_CROSS_TENANT_AUTH_STATE);
  const crossTenantState = JSON.parse(fs.readFileSync(crossTenantStatePath, "utf8"));
  const crossTenantSession = sessionFromStorageState(crossTenantState);
  expect(String(authHeaders(crossTenantSession)["X-Opstrax-Tenant-Id"]))
    .not.toBe(String(authHeaders(tenantSession)["X-Opstrax-Tenant-Id"]));

  const sourceVehicleId = Number(process.env.E2E_IOT_SOURCE_VEHICLE_ID);
  const targetVehicleId = Number(process.env.E2E_IOT_TARGET_VEHICLE_ID);
  const deviceCategory = process.env.E2E_IOT_DEVICE_CATEGORY.trim();
  const deviceRole = process.env.E2E_IOT_DEVICE_ROLE.trim();
  const serial = `${(process.env.E2E_TEST_PREFIX || "QA-E2E").replace(/[^A-Za-z0-9-]/g, "-")}-${runId}-IOT`
    .toUpperCase().slice(0, 120);
  let installAt;
  let firstFixAt;
  let transferAt;
  let targetFixAt;
  let delayedOldFixAt;
  const sourceCoordinates = { lat: 38.75011, lng: -77.47511 };
  const targetCoordinates = { lat: 38.75122, lng: -77.47622 };
  const delayedCoordinates = { lat: 38.75233, lng: -77.47733 };

  const crossTenantContext = await playwrightRequest.newContext({
    baseURL: target.apiBaseUrl,
    storageState: crossTenantState,
    extraHTTPHeaders: { Origin: target.uiBaseUrl },
  });

  let deviceId;
  let credentials;
  let revoked = false;
  const evidence = { runId, serial, deviceCategory, deviceRole, sourceVehicleId, targetVehicleId };

  try {
    const provision = await api(page.request, target, "/api/telemetry/devices/provision", tenantSession, {
      method: "POST",
      data: {
        deviceSerial: serial,
        deviceCategory,
        deviceModel: "OpsTrax certification tracker",
        provider: "staging-certification",
        firmwareVersion: "certification-only",
        notes: `Disposable IoT lifecycle ${runId}`,
      },
    });
    const provisioned = expectOk(provision, 200, "provision device");
    deviceId = Number(provisioned.id);
    credentials = {
      apiKey: String(provisioned.apiKey || ""),
      hmacSecret: String(provisioned.hmacSecret || ""),
    };
    expect(deviceId).toBeGreaterThan(0);
    expect(credentials.apiKey.length, "one-time API key").toBeGreaterThanOrEqual(32);
    expect(credentials.hmacSecret.length, "one-time HMAC secret").toBeGreaterThanOrEqual(32);
    expect(String(provisioned.deviceCategory).toLowerCase(), "provision response must preserve explicit hardware category")
      .toBe(deviceCategory.toLowerCase());
    expect(provision.response.headers()["cache-control"], "credential response must never be cacheable").toMatch(/no-store/i);

    const missingCategory = await api(page.request, target, "/api/telemetry/devices/provision", tenantSession, {
      method: "POST",
      data: { deviceSerial: serial },
    });
    expect(missingCategory.response.status(), "deviceCategory is mandatory rather than inferred").toBe(400);
    expect(String(missingCategory.body?.message || "")).toMatch(/deviceCategory/i);

    const reread = expectOk(
      await api(page.request, target, `/api/telemetry/devices/${deviceId}`, tenantSession),
      200,
      "re-read provisioned device",
    );
    expect(JSON.stringify(reread)).not.toMatch(/apiKey|hmacSecret|api_key_hash|hmac_secret/i);
    expect(String(reread.device?.deviceCategory).toLowerCase()).toBe(deviceCategory.toLowerCase());

    const foreignRead = await api(
      crossTenantContext,
      target,
      `/api/telemetry/devices/${deviceId}`,
      crossTenantSession,
    );
    expect(foreignRead.response.status(), "cross-tenant device read must be indistinguishable from absence").toBe(404);
    expect(JSON.stringify(foreignRead.body)).not.toContain(serial);

    const missingRole = await api(
      page.request,
      target,
      `/api/telemetry/devices/${deviceId}/installations`,
      tenantSession,
      { method: "POST", data: { vehicleId: 0, isPrimary: true } },
    );
    expect(missingRole.response.status(), "deviceRole is mandatory rather than defaulted to GPS").toBe(400);
    expect(String(missingRole.body?.message || "")).toMatch(/role/i);

    installAt = new Date();
    const install = expectOk(
      await api(page.request, target, `/api/telemetry/devices/${deviceId}/installations`, tenantSession, {
        method: "POST",
        data: {
          vehicleId: sourceVehicleId,
          deviceRole,
          isPrimary: true,
          effectiveFrom: installAt.toISOString(),
          installationLocation: "staging certification bench",
          commissioningMethod: "authenticated-heartbeat",
          assignmentReason: `IoT lifecycle ${runId}`,
          idempotencyKey: `${runId}-source-install`,
        },
      }),
      201,
      "create effective-dated installation",
    );
    const sourceInstallationId = Number(install.id);

    const foreignInstall = await api(
      crossTenantContext,
      target,
      `/api/telemetry/devices/${deviceId}/installations`,
      crossTenantSession,
      {
        method: "POST",
        data: { vehicleId: sourceVehicleId, deviceRole, isPrimary: true },
      },
    );
    expect([400, 404], "cross-tenant installation mutation must be denied without identity disclosure")
      .toContain(foreignInstall.response.status());
    expect(JSON.stringify(foreignInstall.body)).not.toContain(serial);

    firstFixAt = new Date();
    const firstTelemetry = expectOk(
      await ingest(page.request, target, credentials, {
        lat: sourceCoordinates.lat,
        lng: sourceCoordinates.lng,
        speedMph: 12,
        heading: 90,
        eventType: "certification-heartbeat",
        engineStatus: "On",
        eventTime: firstFixAt.toISOString(),
        clientGeneratedId: `${runId}-source-fix`,
        correlationId: `${runId}-source`,
      }),
      200,
      "authenticated source telemetry",
    );

    let detail = expectOk(
      await api(page.request, target, `/api/telemetry/devices/${deviceId}`, tenantSession),
      200,
      "read heartbeat evidence",
    );
    let sourceInstallation = installationFromDetail(detail, sourceInstallationId);
    expect(sourceInstallation?.activationVerifiedAt, "heartbeat must activate the installation").toBeTruthy();
    expect(String(sourceInstallation?.deviceRole).toLowerCase()).toBe(deviceRole.toLowerCase());

    const sourceCommission = expectOk(
      await api(
        page.request,
        target,
        `/api/telemetry/devices/${deviceId}/installations/${sourceInstallationId}/commission`,
        tenantSession,
        {
          method: "POST",
          data: {
            result: "Passed",
            expectedRowVersion: Number(sourceInstallation.rowVersion),
            verificationReference: `telemetry-event:${firstTelemetry.id};run:${runId}`,
          },
        },
      ),
      200,
      "commission source installation with telemetry evidence",
    );
    expect(sourceCommission.status).toBe("Verified");
    expect(sourceCommission.verificationReference).toBe(`location-event:${firstTelemetry.id}`);

    detail = expectOk(
      await api(page.request, target, `/api/telemetry/devices/${deviceId}`, tenantSession),
      200,
      "read commissioned installation",
    );
    sourceInstallation = installationFromDetail(detail, sourceInstallationId);
    transferAt = new Date();
    const transfer = expectOk(
      await api(page.request, target, `/api/telemetry/devices/${deviceId}/installations/transfer`, tenantSession, {
        method: "POST",
        data: {
          vehicleId: targetVehicleId,
          currentInstallationId: sourceInstallationId,
          expectedRowVersion: Number(sourceInstallation.rowVersion),
          removalReason: `Certified transfer from vehicle ${sourceVehicleId}`,
          assignmentReason: `Certified transfer to vehicle ${targetVehicleId}`,
          deviceRole,
          isPrimary: true,
          effectiveAt: transferAt.toISOString(),
          commissioningMethod: "authenticated-heartbeat",
          idempotencyKey: `${runId}-target-install`,
        },
      }),
      200,
      "transfer installation",
    );
    const targetInstallationId = Number(transfer.id);

    targetFixAt = new Date();
    const targetTelemetry = expectOk(
      await ingest(page.request, target, credentials, {
        lat: targetCoordinates.lat,
        lng: targetCoordinates.lng,
        speedMph: 8,
        heading: 180,
        eventType: "certification-target-heartbeat",
        engineStatus: "On",
        eventTime: targetFixAt.toISOString(),
        clientGeneratedId: `${runId}-target-fix`,
        correlationId: `${runId}-target`,
      }),
      200,
      "authenticated target telemetry",
    );

    detail = expectOk(
      await api(page.request, target, `/api/telemetry/devices/${deviceId}`, tenantSession),
      200,
      "read target activation evidence",
    );
    let targetInstallation = installationFromDetail(detail, targetInstallationId);
    expect(targetInstallation?.activationVerifiedAt).toBeTruthy();
    expect(String(targetInstallation?.deviceRole).toLowerCase()).toBe(deviceRole.toLowerCase());
    const targetCommission = expectOk(
      await api(
        page.request,
        target,
        `/api/telemetry/devices/${deviceId}/installations/${targetInstallationId}/commission`,
        tenantSession,
        {
          method: "POST",
          data: {
            result: "Passed",
            expectedRowVersion: Number(targetInstallation.rowVersion),
            verificationReference: `telemetry-event:${targetTelemetry.id};run:${runId}`,
          },
        },
      ),
      200,
      "commission target installation with telemetry evidence",
    );
    expect(targetCommission.verificationReference).toBe(`location-event:${targetTelemetry.id}`);

    delayedOldFixAt = new Date(firstFixAt.getTime() + 1);
    expect(delayedOldFixAt.getTime()).toBeLessThan(transferAt.getTime());
    expectOk(
      await ingest(page.request, target, credentials, {
        lat: delayedCoordinates.lat,
        lng: delayedCoordinates.lng,
        speedMph: 5,
        heading: 270,
        eventType: "certification-delayed-fix",
        engineStatus: "Off",
        eventTime: delayedOldFixAt.toISOString(),
        clientGeneratedId: `${runId}-delayed-old-fix`,
        correlationId: `${runId}-delayed`,
      }),
      200,
      "delayed telemetry attributed to historical installation",
    );

    const positions = expectOk(
      await api(page.request, target, "/api/telemetry/positions", tenantSession),
      200,
      "read current positions",
    );
    const sourcePosition = positionFor(positions, sourceVehicleId);
    const targetPosition = positionFor(positions, targetVehicleId);
    expect(Number(sourcePosition?.lat)).toBeCloseTo(sourceCoordinates.lat, 5);
    expect(Number(sourcePosition?.lng)).toBeCloseTo(sourceCoordinates.lng, 5);
    expect(Number(targetPosition?.lat)).toBeCloseTo(targetCoordinates.lat, 5);
    expect(Number(targetPosition?.lng)).toBeCloseTo(targetCoordinates.lng, 5);
    expect(Number(targetPosition?.deviceId)).toBe(deviceId);

    const breadcrumbs = expectOk(
      await api(
        page.request,
        target,
        `/api/telemetry/breadcrumbs?vehicleId=${sourceVehicleId}&from=${encodeURIComponent(installAt.toISOString())}&to=${encodeURIComponent(new Date().toISOString())}&limit=2000`,
        tenantSession,
      ),
      200,
      "read source vehicle history",
    );
    expect(breadcrumbs.points.some((point) =>
      Math.abs(Number(point.lat) - delayedCoordinates.lat) < 0.00001
      && Math.abs(Number(point.lng) - delayedCoordinates.lng) < 0.00001),
    "delayed point must remain in the former vehicle's history").toBe(true);

    detail = expectOk(
      await api(page.request, target, `/api/telemetry/devices/${deviceId}`, tenantSession),
      200,
      "read target installation for removal",
    );
    targetInstallation = installationFromDetail(detail, targetInstallationId);
    expectOk(
      await api(
        page.request,
        target,
        `/api/telemetry/devices/${deviceId}/installations/${targetInstallationId}/remove`,
        tenantSession,
        {
          method: "POST",
          data: {
            removalReason: `Certification cleanup ${runId}`,
            expectedRowVersion: Number(targetInstallation.rowVersion),
          },
        },
      ),
      200,
      "remove target installation",
    );

    const afterRemoval = await ingest(page.request, target, credentials, {
      lat: targetCoordinates.lat,
      lng: targetCoordinates.lng,
      speedMph: 0,
      eventType: "post-removal-denial",
      eventTime: new Date().toISOString(),
      clientGeneratedId: `${runId}-post-removal`,
    });
    expect(afterRemoval.response.status(), "removed device must have no current vehicle identity").toBe(422);

    expectOk(
      await api(page.request, target, `/api/telemetry/devices/${deviceId}/revoke`, tenantSession, {
        method: "POST",
        data: {},
      }),
      200,
      "revoke device credentials",
    );
    revoked = true;

    const afterRevocation = await ingest(page.request, target, credentials, {
      lat: targetCoordinates.lat,
      lng: targetCoordinates.lng,
      speedMph: 0,
      eventType: "post-revocation-denial",
      eventTime: new Date().toISOString(),
      clientGeneratedId: `${runId}-post-revocation`,
    });
    expect([401, 403], "revoked credentials must not ingest telemetry").toContain(afterRevocation.response.status());

    Object.assign(evidence, {
      deviceId,
      sourceInstallationId,
      targetInstallationId,
      sourceTelemetryEventId: firstTelemetry.id,
      targetTelemetryEventId: targetTelemetry.id,
      delayedHistoricalAttribution: true,
      currentProjectionNotOverwritten: true,
      crossTenantReadDenied: true,
      crossTenantMutationDenied: true,
      removed: true,
      revoked: true,
    });
    await testInfo.attach("iot-lifecycle-redacted-evidence.json", {
      body: Buffer.from(JSON.stringify(evidence, null, 2)),
      contentType: "application/json",
    });
  } finally {
    // Best-effort cleanup on any mid-journey assertion failure. Credentials are
    // held only in memory, never attached; this project also disables tracing.
    if (deviceId && !revoked) {
      try {
        await api(page.request, target, `/api/telemetry/devices/${deviceId}/revoke`, tenantSession, {
          method: "POST",
          data: {},
        });
      } catch {
        // The failed assertion remains authoritative; cleanup failure is visible
        // from the labeled disposable device in the staging tenant.
      }
    }
    credentials = undefined;
    await crossTenantContext.dispose();
  }
});
