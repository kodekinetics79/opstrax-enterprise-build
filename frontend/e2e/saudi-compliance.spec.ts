import { test, expect } from '@playwright/test';
import {
  apiLogin, apiPlatformLogin, tenantLogin,
  INTELLIFLOW_ADMIN, INTELLIFLOW_EMP1, INTELLIFLOW_SLUG,
  EVOSTEL_ADMIN, EVOSTEL_SLUG,
} from './helpers';

// Track A — Saudi Regulatory Compliance E2E coverage.
// These tests assume the demo seed (IntelliFlow = Enterprise w/ all features,
// Evostel = Starter w/o qiwa_integration) and a running API + frontend.

test.describe('Saudi compliance — API authorization', () => {
  test('platform admin cannot access tenant saudi-compliance dashboard', async ({ request }) => {
    const token = await apiPlatformLogin(request);
    const resp = await request.get('/api/saudi-compliance/dashboard', {
      headers: { Authorization: `Bearer ${token}` },
    });
    // Platform token lacks tenant_id claim / tenant permissions.
    expect([401, 403]).toContain(resp.status());
  });

  test('IntelliFlow admin can access saudi-compliance dashboard', async ({ request }) => {
    const token = await apiLogin(request, INTELLIFLOW_ADMIN.email, INTELLIFLOW_ADMIN.password, INTELLIFLOW_SLUG);
    const resp = await request.get('/api/saudi-compliance/dashboard', {
      headers: { Authorization: `Bearer ${token}` },
    });
    expect(resp.status()).toBe(200);
    const body = await resp.json();
    expect(body).toHaveProperty('qiwa');
    expect(body).toHaveProperty('wps');
    expect(body).toHaveProperty('gosi');
    expect(body).toHaveProperty('actionItems');
  });

  test('Evostel admin (no qiwa_integration feature) is blocked on QIWA connection', async ({ request }) => {
    const token = await apiLogin(request, EVOSTEL_ADMIN.email, EVOSTEL_ADMIN.password, EVOSTEL_SLUG);
    const resp = await request.get('/api/qiwa/connection', {
      headers: { Authorization: `Bearer ${token}` },
    });
    // FeatureFlagGuardFilter blocks the qiwa_integration-gated route.
    expect(resp.status()).toBe(403);
  });

  test('IntelliFlow employee cannot access QIWA configuration endpoint', async ({ request }) => {
    const token = await apiLogin(request, INTELLIFLOW_EMP1.email, INTELLIFLOW_EMP1.password, INTELLIFLOW_SLUG);
    const resp = await request.put('/api/qiwa/connection', {
      headers: { Authorization: `Bearer ${token}` },
      data: { establishmentId: '7000123456', establishmentName: 'X', unifiedOrganisationNumber: '1', environment: 'sandbox' },
    });
    // Employee lacks qiwa.configure permission.
    expect(resp.status()).toBe(403);
  });

  test('GOSI readiness endpoint returns 200 with disclaimer', async ({ request }) => {
    const token = await apiLogin(request, INTELLIFLOW_ADMIN.email, INTELLIFLOW_ADMIN.password, INTELLIFLOW_SLUG);
    const resp = await request.get('/api/saudi-compliance/gosi-readiness', {
      headers: { Authorization: `Bearer ${token}` },
    });
    expect(resp.status()).toBe(200);
    const body = await resp.json();
    expect(body).toHaveProperty('disclaimer');
    expect(body.disclaimer).toContain('illustrative');
    expect(body).toHaveProperty('employees');
  });

  test('QIWA readiness summary surfaces blocked employees with missing fields', async ({ request }) => {
    const token = await apiLogin(request, INTELLIFLOW_ADMIN.email, INTELLIFLOW_ADMIN.password, INTELLIFLOW_SLUG);
    const resp = await request.get('/api/qiwa/readiness-summary', {
      headers: { Authorization: `Bearer ${token}` },
    });
    expect(resp.status()).toBe(200);
    const body = await resp.json();
    expect(body).toHaveProperty('totalEmployees');
    expect(body).toHaveProperty('blockedFromSync');
    expect(body).toHaveProperty('blockedEmployees');
    expect(Array.isArray(body.blockedEmployees)).toBe(true);
  });

  test('WPS pre-export validation on a non-existent run returns 404', async ({ request }) => {
    const token = await apiLogin(request, INTELLIFLOW_ADMIN.email, INTELLIFLOW_ADMIN.password, INTELLIFLOW_SLUG);
    const resp = await request.post('/api/payroll/runs/00000000-0000-0000-0000-000000000000/wps-validation', {
      headers: { Authorization: `Bearer ${token}` },
    });
    expect(resp.status()).toBe(404);
  });
});

test.describe('Saudi compliance — UI', () => {
  test('saudi-compliance page loads for IntelliFlow admin', async ({ page }) => {
    await tenantLogin(page, INTELLIFLOW_ADMIN.email, INTELLIFLOW_ADMIN.password, INTELLIFLOW_SLUG);
    await page.goto('/saudi-compliance');
    await expect(page.getByRole('heading', { name: 'Saudi Compliance' })).toBeVisible({ timeout: 15_000 });
    // No crash: at least one of the section cards is present.
    await expect(page.getByText('QIWA', { exact: true }).first()).toBeVisible();
  });
});
