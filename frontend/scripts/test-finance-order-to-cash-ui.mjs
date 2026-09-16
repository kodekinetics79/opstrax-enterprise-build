import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { fileURLToPath } from "node:url";

const root = resolve(fileURLToPath(new URL("..", import.meta.url)));
const page = readFileSync(resolve(root, "src/pages/FinancialAnalyticsPage.tsx"), "utf8");
const service = readFileSync(resolve(root, "src/services/financeOrderToCashApi.ts"), "utf8");
const workspace = readFileSync(resolve(root, "src/components/CommercialWorkspace.tsx"), "utf8");

assert.match(service, /get\("\/api\/invoice-drafts"\)/);
assert.match(service, /post\(`\/api\/invoice-drafts\/\$\{encodeURIComponent\(draftId\)\}\/issue`/);
assert.match(service, /post\(`\/api\/issued-invoices\/\$\{encodeURIComponent\(invoiceId\)\}\/payments`/);
assert.match(service, /idempotencyKey/);

assert.match(page, /hasPermission\("finance\.invoice_draft\.read"\)/);
assert.match(page, /hasPermission\("finance\.invoice\.issue"\)/);
assert.match(page, /hasPermission\("finance\.invoice\.payment\.record"\)/);
assert.match(page, /crypto\.randomUUID\(\)/);
assert.match(page, /Invoice drafts awaiting issue/);
assert.match(page, /Record invoice payment/);
assert.match(page, /Payment cannot exceed the outstanding balance/);
assert.match(page, /providerSettlementClaim/);
assert.match(page, /Provider Settlement/);
assert.match(page, /Margin stays unavailable where allocated cost evidence is missing/);
assert.doesNotMatch(page, /Collections are within expected range/);
assert.doesNotMatch(page, /Sourced from the live revenue spine/);
assert.doesNotMatch(page, /No fabricated finance rows are used here/);
assert.match(page, /role="dialog" aria-modal="true"/);
assert.match(page, /role=\{notice\.kind === "error" \? "alert" : "status"\}/);
assert.match(page, /apiErrorMessage\(error, "The invoice could not be issued/);
assert.match(page, /invalidateQueries\(\{ queryKey: \["invoice-drafts"\] \}\)/);
assert.match(page, /invalidateQueries\(\{ queryKey: \["issued-invoices"\] \}\)/);
assert.match(page, /invalidateQueries\(\{ queryKey: \["payments"\] \}\)/);
assert.match(page, /<FinanceWorkspaceTabs \/>/);
assert.match(workspace, /key: "\/invoices"/);
assert.match(workspace, /key: "\/payments"/);
assert.match(workspace, /onSelect=\{\(route\) => navigate\(route\)\}/, "Finance sections must navigate to their canonical routes through the shared compact tab rail");
assert.doesNotMatch(page, /const \[tab, setTab\] = useState<Tab>/);

console.log("Finance order-to-cash UI contract passed.");
