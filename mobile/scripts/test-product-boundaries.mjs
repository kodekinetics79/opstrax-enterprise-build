import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import { resolveProductAccess } from "../src/auth/productAccess.ts";

const access = (variant, normalizedRole, permissions) => resolveProductAccess({
  variant,
  hasSession: true,
  normalizedRole,
  permissions,
});

test("Driver accepts only a driver-scoped session", () => {
  assert.deepEqual(access("driver", "driverOperator", ["driver:self"]), {
    allowed: true,
    experience: "driver",
    isCustomer: false,
    isDriver: true,
    isFleetUser: false,
  });
  assert.equal(access("driver", "driverOperator", ["driver:self", "dashboard:view"]).allowed, false);
  assert.equal(access("driver", "tenantAdmin", ["*"]).allowed, false);
});

test("Customer requires both the customer role and portal permission", () => {
  assert.equal(access("customer", "customerClient", ["customer_portal:view"]).allowed, true);
  assert.equal(access("customer", "customerClient", []).allowed, false);
  assert.equal(access("customer", "tenantAdmin", ["customer_portal:view"]).allowed, false);
});

test("Fleet rejects customer, driver-only, platform-admin, and unknown roles", () => {
  for (const role of ["fieldWorker", "dispatcherSupervisor", "warehousePickup", "safetyMaintenance", "tenantAdmin"]) {
    assert.equal(access("fleet", role, ["dashboard:view"]).allowed, true, `${role} should be accepted`);
  }
  assert.equal(access("fleet", "driverOperator", ["driver:self"]).allowed, false);
  assert.equal(access("fleet", "customerClient", ["customer_portal:view"]).allowed, false);
  assert.equal(access("fleet", "platformAdmin", ["*"]).allowed, false);
  assert.equal(access("fleet", "general", ["dashboard:view"]).allowed, false);
});

test("Customer screens use only customer-portal API families", async () => {
  const paths = ["CustomerHomeScreen.tsx", "CustomerShipmentsScreen.tsx", "CustomerBillingScreen.tsx"];
  for (const path of paths) {
    const source = await readFile(new URL(`../src/screens/${path}`, import.meta.url), "utf8");
    assert.match(source, /\/api\/portal\//, `${path} should call the customer portal API`);
    assert.doesNotMatch(source, /["'`]\/api\/(?!portal\/)/, `${path} must not call an internal API family`);
  }
});

test("Customer build has a separate identity and omits device location", async () => {
  const [config, profiles] = await Promise.all([
    readFile(new URL("../app.config.ts", import.meta.url), "utf8"),
    readFile(new URL("../eas.json", import.meta.url), "utf8"),
  ]);
  assert.match(config, /bundle: "com\.opstrax\.customer"/);
  assert.match(config, /if \(APP_VARIANT !== "customer"\) plugins\.splice\(2, 0, "expo-location"\)/);
  assert.equal(JSON.parse(profiles).build["production-customer"].env.EXPO_PUBLIC_APP_VARIANT, "customer");
});
