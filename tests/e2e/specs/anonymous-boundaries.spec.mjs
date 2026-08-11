import { test, expect } from "../fixtures.mjs";

const tenantRoutes = [
  "/live-dashboard",
  "/map-view",
  "/vehicles/overview",
  "/drivers/overview",
  "/dispatch",
  "/work-orders",
  "/proof-of-delivery",
  "/safety",
  "/customer-portal",
  "/user-management",
];

for (const path of tenantRoutes) {
  test(`@readonly anonymous access to ${path} redirects to tenant login`, async ({ page }) => {
    await page.goto(path);
    await expect(page).toHaveURL(/\/login(?:\?|$)/);
    await expect(page.getByRole("heading", { name: "Sign in" })).toBeVisible();
  });
}

for (const path of ["/driver", "/driver/assignments", "/driver/dvir"]) {
  test(`@readonly anonymous access to ${path} redirects to tenant login`, async ({ page }) => {
    await page.goto(path);
    await expect(page).toHaveURL(/\/login(?:\?|$)/);
  });
}

test("@readonly anonymous Platform access stays in the separate Platform login boundary", async ({ page }) => {
  await page.goto("/platform");
  await expect(page).toHaveURL(/\/platform\/login(?:\?|$)/);
  await expect(page.getByRole("heading", { name: /Platform/i })).toBeVisible();
});
