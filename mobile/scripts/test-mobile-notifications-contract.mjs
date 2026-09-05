import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const source = async (path) => readFile(new URL(`../${path}`, import.meta.url), "utf8");

test("mobile operational inbox uses recipient-scoped notification endpoints", async () => {
  const settings = await source("src/screens/SettingsScreen.tsx");
  assert.match(settings, /"\/api\/notifications"/);
  assert.match(settings, /"\/api\/notifications\/unread-count"/);
  assert.match(settings, /`\/api\/notifications\/\$\{encodeURIComponent\(id\)\}\/read`/);
  assert.doesNotMatch(settings, /companyId|company_id|tenantId|tenant_id/);
  assert.doesNotMatch(settings, /X-Tenant|tenant override/i);
});

test("notification rendering does not fabricate delivery or acknowledgement state", async () => {
  const settings = await source("src/screens/SettingsScreen.tsx");
  assert.match(settings, /recipientStatus/);
  assert.match(settings, /recipient_status/);
  assert.match(settings, /item\.status/);
  assert.match(settings, /notifications\.loading/);
  assert.match(settings, /notifications\.error/);
  assert.doesNotMatch(settings, /acknowledge-all/);
});

test("all public mobile role products retain the authenticated inbox surface", async () => {
  const navigation = await source("src/navigation/RootNavigator.tsx");
  assert.match(navigation, /DriverMore[^\n]+SettingsScreen/);
  assert.match(navigation, /CustomerMore[^\n]+SettingsScreen/);
  assert.match(navigation, /name="More" component=\{SettingsScreen\}/);
});
