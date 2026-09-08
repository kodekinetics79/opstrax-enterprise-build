import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const source = async (path) => readFile(new URL(`../${path}`, import.meta.url), "utf8");

test("customer mobile navigation is role and portal-permission gated", async () => {
  const [navigation, productAccess] = await Promise.all([
    source("src/navigation/RootNavigator.tsx"),
    source("src/auth/productAccess.ts"),
  ]);
  assert.match(productAccess, /normalizedRole === "customerClient"/);
  assert.match(productAccess, /directPermissions\.has\("customer_portal:view"\)/);
  assert.match(navigation, /component=\{CustomerSupportScreen\}/);
});

test("customer shipment list, detail, timeline, and proof use portal-scoped endpoints", async () => {
  const shipments = await source("src/screens/CustomerShipmentsScreen.tsx");
  assert.match(shipments, /"\/api\/portal\/jobs"/);
  assert.match(shipments, /`\/api\/portal\/jobs\/\$\{selectedJobId\}`/);
  assert.match(shipments, /statusTimeline/);
  assert.match(shipments, /proofs/);
  assert.doesNotMatch(shipments, /api\.jobs\(\)/);
  assert.doesNotMatch(shipments, /"\/api\/jobs"/);
});

test("customer billing summary and detail use customer portal invoice endpoints", async () => {
  const billing = await source("src/screens/CustomerBillingScreen.tsx");
  assert.match(billing, /"\/api\/portal\/invoices"/);
  assert.match(billing, /`\/api\/portal\/invoices\/\$\{encodeURIComponent\(selectedInvoiceId\)\}`/);
  assert.match(billing, /taxBreakdown/);
  assert.match(billing, /payments/);
  assert.doesNotMatch(billing, /issued_invoices|\/api\/finance\//);
});

test("customer support submits only shipment-linked portal feedback", async () => {
  const support = await source("src/screens/CustomerSupportScreen.tsx");
  assert.match(support, /"\/api\/portal\/feedback"/);
  assert.match(support, /jobId: selectedJobId/);
  assert.match(support, /feedbackType/);
  assert.match(support, /subject: subject\.trim\(\)/);
  assert.match(support, /comment: comment\.trim\(\)/);
  assert.doesNotMatch(support, /\/api\/support\/admin|\/api\/jobs\/\$\{/);
});

test("customer screens do not expose internal fleet economics or driver risk fields", async () => {
  const [home, shipments, billing, support] = await Promise.all([
    source("src/screens/CustomerHomeScreen.tsx"),
    source("src/screens/CustomerShipmentsScreen.tsx"),
    source("src/screens/CustomerBillingScreen.tsx"),
    source("src/screens/CustomerSupportScreen.tsx"),
  ]);
  const combined = [home, shipments, billing, support].join("\n");
  for (const forbidden of ["riskScore", "costEstimate", "marginEstimate", "dispatcherNotes", "driverRiskScore"]) {
    assert.doesNotMatch(combined, new RegExp(forbidden, "i"));
  }
});

test("store packaging produces distinct Driver Fleet and Customer products", async () => {
  const [appConfig, eas, runtimeConfig, navigation, productAccess] = await Promise.all([
    source("app.config.ts"),
    source("eas.json"),
    source("src/config.ts"),
    source("src/navigation/RootNavigator.tsx"),
    source("src/auth/productAccess.ts"),
  ]);
  for (const product of ["driver", "fleet", "customer"]) {
    assert.match(appConfig, new RegExp(`${product}:\\s*\\{[\\s\\S]{0,100}name:\\s*"OpsTrax`, "i"));
    assert.match(eas, new RegExp(`"production-${product}"`));
  }
  assert.match(appConfig, /Production OpsTrax builds must set EXPO_PUBLIC_APP_VARIANT/);
  assert.match(runtimeConfig, /SECURE_SESSION_KEY = `opstrax\.\$\{APP_VARIANT\}/);
  assert.match(runtimeConfig, /SECURE_WORKSPACE_JOB_KEY = `opstrax\.\$\{APP_VARIANT\}/);
  assert.match(navigation, /resolveProductAccess/);
  assert.match(productAccess, /variant === "driver"/);
  assert.match(productAccess, /variant === "customer"/);
  assert.match(productAccess, /variant === "fleet"/);
  assert.match(productAccess, /FLEET_ROLES\.has\(normalizedRole\)/);
  assert.doesNotMatch(productAccess, /"platformAdmin"/);
});
