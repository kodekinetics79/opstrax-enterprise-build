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
  assert.doesNotMatch(settings, /["']X-Tenant["']/i);
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

test("native push uses Expo SDK56 project-bound token acquisition and authenticated device lifecycle", async () => {
  const config = await source("app.config.ts");
  const push = await source("src/notifications/push.ts");
  assert.match(config, /"expo-notifications"/);
  assert.match(push, /getExpoPushTokenAsync\(\{ projectId: EAS_PROJECT_ID \}\)/);
  assert.match(push, /setNotificationChannelAsync\(CHANNEL_ID/);
  assert.match(push, /"\/api\/mobile\/devices\/register"/);
  assert.match(push, /"\/api\/mobile\/devices\/revoke"/);
  assert.match(push, /product: APP_VARIANT/);
  assert.match(push, /platform: Platform\.OS/);
  assert.doesNotMatch(push, /companyId|company_id|tenantId|tenant_id|userId|user_id/);
});

test("native push never exposes or logs the raw token and notification taps do not trust payload navigation", async () => {
  const push = await source("src/notifications/push.ts");
  const settings = await source("src/screens/SettingsScreen.tsx");
  assert.doesNotMatch(push, /console\.(?:log|info|warn|error)\([^\n]*token/i);
  assert.doesNotMatch(settings, /pushStatus[^\n]*(?:token|pushToken)/i);
  assert.doesNotMatch(push, /Linking\.openURL|response\.notification\.request\.content\.data|deep.?link|navigation\.navigate/i);
  assert.match(push, /Notification payloads never choose a tenant, URL, or privileged destination/);
});

test("push permission is explicit and remote revocation starts before local session destruction", async () => {
  const settings = await source("src/screens/SettingsScreen.tsx");
  const session = await source("src/auth/SessionProvider.tsx");
  assert.match(settings, /registerNativePush\(api, \{ requestPermission: true \}\)/);
  assert.match(session, /registerNativePush\(api, \{ requestPermission: false \}\)/);
  assert.match(session, /revokeStoredNativePush\(api\)/);
  const revokeIndex = session.indexOf("revokeStoredNativePush(api)");
  const clearIndex = session.indexOf("SecureStore.deleteItemAsync(SECURE_SESSION_KEY)", revokeIndex);
  assert.ok(revokeIndex >= 0 && clearIndex > revokeIndex, "push revocation must begin before local session clear in logout");
});
