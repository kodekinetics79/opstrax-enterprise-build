import { expect } from "../fixtures.mjs";

export async function expectAuthenticatedRoute(page, path, heading) {
  await page.goto(path, { waitUntil: "domcontentloaded" });
  await expect(page).not.toHaveURL(/\/(?:platform\/)?login(?:\?|$)/);
  await expect(page.locator("body")).not.toContainText(/Unhandled exception|Something went wrong|Access denied|Forbidden/i);
  await expect(page.locator("main").first()).toBeVisible();
  if (heading) await expect(page.getByRole("heading", { name: heading }).first()).toBeVisible();
}

export async function stubUnavailablePublicApisWhenLocal(page, target) {
  if (target.environment !== "local") return;
  await page.route("**/api/**", async (route) => {
    if (route.request().method() !== "GET") return route.abort("blockedbyclient");
    return route.fulfill({
      status: 404,
      contentType: "application/json",
      body: JSON.stringify({ success: false, message: "Not found" }),
    });
  });
}
