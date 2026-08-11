import { test, expect } from "../fixtures.mjs";

test("creates one labeled work order for the dedicated staging canary vehicle", async ({
  page,
  authConfigured,
  mutationGate,
  runId,
}) => {
  test.skip(!authConfigured, "Provide E2E_TENANT_AUTH_STATE for the staging persona");
  test.skip(!mutationGate.enabled, mutationGate.reasons.join("; "));

  const prefix = (process.env.E2E_TEST_PREFIX || "QA-E2E").replace(/[^a-zA-Z0-9-]/g, "-");
  const title = `${prefix}-${runId}-work-order`;
  const due = new Date(Date.now() + 7 * 24 * 60 * 60 * 1000).toISOString().slice(0, 10);

  await page.goto("/work-orders");
  await page.getByRole("button", { name: "Create work order" }).click();
  const dialog = page.getByRole("dialog", { name: "Create work order" });
  await dialog.getByLabel(/Vehicle/).selectOption(process.env.E2E_CANARY_VEHICLE_ID);
  await dialog.getByLabel(/Title/).fill(title);
  await dialog.getByLabel(/Service/).fill("QA staging inspection");
  await dialog.getByLabel(/Priority/).selectOption("Low");
  await dialog.getByLabel(/Estimated cost/).fill("0");
  await dialog.getByLabel(/Due date/).fill(due);
  await dialog.getByLabel(/Description/).fill("Authorized disposable staging canary created by the guarded launch suite.");
  await dialog.getByRole("button", { name: "Create work order", exact: true }).click();
  await expect(page.getByRole("status")).toContainText("Work order created.");
  await expect(page.getByText(title)).toBeVisible();
});
