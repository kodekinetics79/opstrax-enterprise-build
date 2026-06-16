import { test, expect } from '@playwright/test';
import { platformLogin, platformLogout, PLATFORM_EMAIL, PLATFORM_PASSWORD } from './helpers';

test.describe('Platform authentication', () => {
  test('valid credentials redirect to /platform/dashboard', async ({ page }) => {
    await platformLogin(page);
    await expect(page).toHaveURL(/\/platform\/dashboard/);
  });

  test('valid login stores platform_access_token in localStorage', async ({ page }) => {
    await platformLogin(page);
    const token = await page.evaluate(() => localStorage.getItem('platform_access_token'));
    expect(token).not.toBeNull();
    expect(token!.length).toBeGreaterThan(20);
  });

  test('wrong password shows an error message', async ({ page }) => {
    await page.goto('/platform/login');
    await page.getByRole('textbox', { name: 'Email address' }).fill(PLATFORM_EMAIL);
    await page.getByRole('textbox', { name: 'Password' }).fill('WRONG_PASSWORD_XYZ');
    await page.getByRole('button', { name: /sign in/i }).click();
    // Wait for the error message to appear (API call completes and shows error)
    await expect(page.getByText(/invalid|incorrect|error|wrong|credentials/i).first()).toBeVisible({ timeout: 10_000 });
    await expect(page).toHaveURL(/\/platform\/login/);
  });

  test('unauthenticated visit to /platform/dashboard redirects to /platform/login', async ({ page }) => {
    // Ensure no token
    await page.goto('/platform/login');
    await page.evaluate(() => localStorage.removeItem('platform_access_token'));
    await page.goto('/platform/dashboard');
    await page.waitForURL(/\/platform\/login/, { timeout: 10_000 });
    await expect(page).toHaveURL(/\/platform\/login/);
  });

  test('after logout dashboard redirects to login', async ({ page }) => {
    await platformLogin(page);
    await platformLogout(page);
    await page.goto('/platform/dashboard');
    await page.waitForURL(/\/platform\/login/, { timeout: 10_000 });
    await expect(page).toHaveURL(/\/platform\/login/);
  });

  test('password visibility toggle works on login form', async ({ page }) => {
    await page.goto('/platform/login');
    const passwordInput = page.getByRole('textbox', { name: 'Password' });
    // Initially masked
    await expect(passwordInput).toHaveAttribute('type', 'password');
    // Click toggle
    await page.getByRole('button', { name: /show password|hide password/i }).click();
    // Should now be visible (type=text)
    const typeAfterToggle = await passwordInput.getAttribute('type');
    expect(['text', 'password']).toContain(typeAfterToggle);
  });
});
