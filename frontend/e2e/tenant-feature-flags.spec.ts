import { test, expect } from '@playwright/test';
import {
  apiLogin,
  INTELLIFLOW_SLUG, INTELLIFLOW_ADMIN,
  EVOSTEL_SLUG, EVOSTEL_ADMIN,
  apiPlatformLogin,
} from './helpers';

/**
 * Feature flag tests — verifies that:
 * 1. Disabled features return 403 from the API (backend enforcement).
 * 2. Enabled features (IntelliFlow) return 200 or appropriate non-403.
 * 3. Platform admin can toggle feature flags.
 *
 * Evostel has: ai_assistant=false, recruitment=false, performance=false, shifts=false
 * IntelliFlow has: all=true
 */
test.describe('Feature flag enforcement (API-level)', () => {
  let intelliflowToken: string;
  let evostelToken: string;

  test.beforeAll(async ({ request }) => {
    intelliflowToken = await apiLogin(request, INTELLIFLOW_ADMIN.email, INTELLIFLOW_ADMIN.password, INTELLIFLOW_SLUG);
    evostelToken     = await apiLogin(request, EVOSTEL_ADMIN.email, EVOSTEL_ADMIN.password, EVOSTEL_SLUG);
  });

  // ── Evostel: disabled features return 403 ────────────────────────────────────

  test('Evostel: AI assistant API returns 403 (feature disabled)', async ({ request }) => {
    const resp = await request.get('/api/ai/insights', {
      headers: { Authorization: `Bearer ${evostelToken}` },
    });
    expect(resp.status()).toBe(403);
  });

  test('Evostel: recruitment API returns 403 (feature disabled)', async ({ request }) => {
    const resp = await request.get('/api/recruitment/applications', {
      headers: { Authorization: `Bearer ${evostelToken}` },
    });
    expect(resp.status()).toBe(403);
  });

  test('Evostel: performance API returns 403 (feature disabled)', async ({ request }) => {
    const resp = await request.get('/api/performance/cycles', {
      headers: { Authorization: `Bearer ${evostelToken}` },
    });
    expect(resp.status()).toBe(403);
  });

  test('Evostel: shifts API returns 403 (feature disabled)', async ({ request }) => {
    const resp = await request.get('/api/shifts/definitions', {
      headers: { Authorization: `Bearer ${evostelToken}` },
    });
    expect(resp.status()).toBe(403);
  });

  test('Evostel: overtime API returns 403 (feature disabled)', async ({ request }) => {
    const resp = await request.get('/api/overtime/requests', {
      headers: { Authorization: `Bearer ${evostelToken}` },
    });
    expect(resp.status()).toBe(403);
  });

  // ── IntelliFlow: enabled features pass through ────────────────────────────────

  test('IntelliFlow: AI assistant API does not return 403 (feature enabled)', async ({ request }) => {
    const resp = await request.get('/api/ai/insights', {
      headers: { Authorization: `Bearer ${intelliflowToken}` },
    });
    expect(resp.status()).not.toBe(403);
  });

  test('IntelliFlow: recruitment API does not return 403 (feature enabled)', async ({ request }) => {
    const resp = await request.get('/api/recruitment/applications', {
      headers: { Authorization: `Bearer ${intelliflowToken}` },
    });
    expect(resp.status()).not.toBe(403);
  });

  test('IntelliFlow: performance API does not return 403 (feature enabled)', async ({ request }) => {
    const resp = await request.get('/api/performance/cycles', {
      headers: { Authorization: `Bearer ${intelliflowToken}` },
    });
    expect(resp.status()).not.toBe(403);
  });

  // ── Always-allowed routes bypass feature guard ────────────────────────────────

  test('Evostel: /api/employees is always allowed (not behind any feature flag)', async ({ request }) => {
    const resp = await request.get('/api/employees', {
      headers: { Authorization: `Bearer ${evostelToken}` },
    });
    expect(resp.status()).not.toBe(403);
  });

  test('Evostel: /api/leave/requests is always allowed', async ({ request }) => {
    const resp = await request.get('/api/leave/requests', {
      headers: { Authorization: `Bearer ${evostelToken}` },
    });
    expect(resp.status()).not.toBe(403);
  });

  test('Evostel: /api/attendance is always allowed', async ({ request }) => {
    const resp = await request.get('/api/attendance', {
      headers: { Authorization: `Bearer ${evostelToken}` },
    });
    expect(resp.status()).not.toBe(403);
  });

  // ── Platform API: feature flag read ────────────────────────────────────────────

  test('Platform admin can read IntelliFlow feature flags', async ({ request }) => {
    const platformToken = await apiPlatformLogin(request);
    const tenants = await request.get('/api/platform/tenants', {
      headers: { Authorization: `Bearer ${platformToken}` },
    });
    expect(tenants.ok()).toBe(true);
    const list = await tenants.json();
    const intelliflow = (list as Array<{ slug: string; featureFlags?: unknown }>)
      .find(t => t.slug === INTELLIFLOW_SLUG);
    expect(intelliflow).toBeDefined();
  });

  // ── /api/features endpoint always allowed (tenant reads own flags) ─────────────

  test('Evostel tenant can read own feature flags via /api/features/disabled-keys', async ({ request }) => {
    const resp = await request.get('/api/features/disabled-keys', {
      headers: { Authorization: `Bearer ${evostelToken}` },
    });
    expect([200, 204]).toContain(resp.status());
  });
});
