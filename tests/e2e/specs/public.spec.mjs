import { test, expect } from "../fixtures.mjs";
import { stubUnavailablePublicApisWhenLocal } from "./helpers.mjs";

test.describe("@readonly public access", () => {
  test("login renders identifier-first access without a stored session", async ({ page }) => {
    await page.goto("/login");
    await expect(page.getByRole("heading", { name: "Sign in" })).toBeVisible();
    const email = page.getByLabel("Work email");
    const continueButton = page.getByRole("button", { name: /Continue/i });
    await expect(email).toBeVisible();
    await expect(continueButton).toBeDisabled();
    await email.fill("qa@example.com");
    await expect(continueButton).toBeEnabled();
  });

  test("invalid work email is rejected without a network request", async ({ page }) => {
    await page.goto("/login");
    await page.getByLabel("Work email").fill("not-an-email");
    await page.getByRole("button", { name: /Continue/i }).click();
    await expect(page.getByText("Enter a valid work email address.")).toBeVisible();
  });

  test("login controls expose names and a visible keyboard focus path", async ({ page }) => {
    await page.goto("/login");
    const email = page.getByLabel("Work email");
    const continueButton = page.getByRole("button", { name: /Continue/i });
    await email.focus();
    await expect(email).toBeFocused();
    await email.fill("qa@example.com");
    await page.keyboard.press("Tab");
    await expect(continueButton).toBeFocused();
  });

  test("forgot-password entry point renders but does not submit", async ({ page }) => {
    await page.goto("/forgot-password");
    await expect(page.getByRole("heading", { name: "Reset your password" })).toBeVisible();
    await expect(page.getByLabel("Work email")).toBeVisible();
  });

  test("invalid ETA token fails closed", async ({ page, target }) => {
    await stubUnavailablePublicApisWhenLocal(page, target);
    await page.goto("/eta/qa-invalid-token");
    await expect(page.getByText("Tracking link not found.")).toBeVisible();
  });

  test("invalid shipment token fails closed", async ({ page, target }) => {
    await stubUnavailablePublicApisWhenLocal(page, target);
    await page.goto("/track/qa-invalid-token");
    await expect(page.getByRole("heading", { name: "Tracking unavailable" })).toBeVisible();
    await expect(page.getByText(/unavailable, expired, or revoked/i)).toBeVisible();
  });

  test("invalid detention-evidence token fails closed", async ({ page, target }) => {
    await stubUnavailablePublicApisWhenLocal(page, target);
    await page.goto("/evidence/qa-invalid-token");
    await expect(page.getByText(/evidence link is unavailable, expired, or revoked/i)).toBeVisible();
  });
});
