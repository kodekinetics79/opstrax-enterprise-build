import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const source = (path) => readFileSync(resolve(root, path), "utf8");
const lines = (path) => source(path).split(/\r?\n/).length;

const dispatchPage = source("src/pages/DispatchWorkspacePage.tsx");
const adminPage = source("src/pages/AdminPage.tsx");
const tenantPage = source("src/pages/platform/PlatformTenantsPage.tsx");
const adminPanels = source("src/pages/admin/AdminTaskPanels.tsx");
const tenantCreate = source("src/pages/platform/tenant-management/TenantCreateDrawer.tsx");
const tenantDetail = source("src/pages/platform/tenant-management/TenantDetailDrawer.tsx");
const tenantBilling = source("src/pages/platform/tenant-management/TenantBillingPlanSection.tsx");
const app = source("src/App.tsx");
const platformApp = source("src/pages/platform/PlatformApp.tsx");

assert.ok(lines("src/pages/DispatchWorkspacePage.tsx") < 1_250, "dispatch orchestration page must stay below its former 1,550-line mixed-workflow size");
assert.match(dispatchPage, /DispatchOverviewPanel/);
assert.match(dispatchPage, /DispatchWorkspaceComponents/);
assert.match(dispatchPage, /dispatchWorkspaceModel/);

assert.ok(lines("src/pages/AdminPage.tsx") < 1_250, "admin orchestration page must stay below its former 1,540-line mixed-task size");
for (const panel of ["AdminRolesPanel", "AdminPermissionsPanel", "AdminAccessReviewsPanel", "AdminSettingsPanel", "AdminAuditPanel"]) {
  assert.match(adminPage, new RegExp(panel));
  assert.match(adminPanels, new RegExp(`export function ${panel}`));
}

assert.ok(lines("src/pages/platform/PlatformTenantsPage.tsx") < 350, "platform tenant list must not absorb provisioning, account, and billing drawers again");
assert.match(tenantCreate, /export function CreateTenantDrawer/);
assert.match(tenantDetail, /export function TenantDetailDrawer/);
assert.match(tenantDetail, /TenantBillingPlanSection/);
assert.match(tenantBilling, /export function TenantBillingPlanSection/);

assert.match(app, /path="\/logistics-workspace"[\s\S]*<DispatchWorkspacePage mode="dispatch"/);
assert.match(app, /path="\/admin"[\s\S]*<AdminPage/);
assert.match(app, /path="\/user-management"[\s\S]*<AdminPage/);
assert.match(platformApp, /path="tenants"[\s\S]*<PlatformTenantsPage/);

console.log("Large-page decomposition contract passed; routes and task boundaries remain stable.");
