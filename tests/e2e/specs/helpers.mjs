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
    if (new URL(route.request().url()).pathname === "/api/localization/user-preferences") {
      return route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({ success: true, data: [] }),
      });
    }
    return route.fulfill({
      // A transport-successful negative envelope exercises the page's
      // fail-closed state without manufacturing a browser console error.
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ success: false, message: "Not found" }),
    });
  });
}
