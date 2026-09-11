import assert from "node:assert/strict";
import fs from "node:fs";

const read = (path) => fs.readFileSync(new URL(path, import.meta.url), "utf8");
const app = read("../src/App.tsx");
const shell = read("../src/layouts/AppShell.tsx");
const trips = read("../src/pages/TripsPage.tsx");
const dispatch = read("../src/pages/DispatchCommandPage.tsx");
const proof = read("../src/pages/OperationsProofCenterPage.tsx");
const jobs = read("../src/pages/JobsPage.tsx");
const dispatchApi = read("../src/services/dispatchApi.ts");
const leads = read("../src/pages/LeadsPage.tsx");
const opportunities = read("../src/pages/OpportunitiesPage.tsx");
const quotations = read("../src/pages/QuotationsPage.tsx");
const rateCards = read("../src/pages/RateCardsPage.tsx");
const campaigns = read("../src/pages/CampaignsPage.tsx");
const endpoints = read("../../backend-dotnet/Controllers/EndpointMappings.cs");

assert.match(app, /path="\/price-simulation"[\s\S]{0,180}moduleKey="price-simulation"/, "Price Simulation must open its own module surface");
assert.doesNotMatch(app, /path="\/price-simulation"[\s\S]{0,180}<QuotationsPage/, "Price Simulation must not silently open Quotations");

assert.match(shell, /notificationsApi\.list/, "The shell bell must use the persisted notification service");
assert.match(shell, /navigate\("\/notifications"\)/, "The shell notification action must open Notification Center");
assert.doesNotMatch(shell, /const NOTIFS/, "The shell must not use a hardcoded notification list");

for (const target of ["/jobs?jobId=", "/dispatch?jobId=", "/operations/proof-center?jobId="]) {
  assert.ok(trips.includes(target), `Trip context must carry the linked job into ${target}`);
}
assert.doesNotMatch(trips, /(?:jobs|dispatch|proof-center)\?tripId=/, "Trip actions must not pass an unsupported tripId query");
assert.match(dispatch, /searchParams\.get\("jobId"\)/, "Dispatch must consume linked job context");
assert.match(proof, /searchParams\.get\("jobId"\)/, "Proof Center must consume linked job context");
assert.match(dispatchApi, /jobId\?: number/, "The dispatch service must support job filtering");
assert.match(endpoints, /da\.job_id=@jid/, "The dispatch endpoint must apply the linked job filter");
assert.match(endpoints, /j\.status job_status/, "Dispatch assignments must expose job state separately");
assert.match(dispatch, /Current.*History/s, "Dispatch must distinguish the current assignment from history");
assert.match(dispatch, /Job State/, "Dispatch detail must distinguish job state from assignment state");
assert.match(jobs, /operations\/proof-center\?jobId=/, "Jobs must link directly to the same Proof Center job context");
assert.match(endpoints, /Batch 2 proof placeholder\./, "Operational proof reads must explicitly exclude the retired demo placeholder");

assert.match(endpoints, /NpgsqlDbType\.Jsonb, JsonSerializer\.Serialize\(metadata\)/, "Generic commercial modules must persist their allowed detail fields");
for (const field of ["contactPerson", "estimatedMonthlyLoads", "probability", "expectedCloseDate", "currency", "origin", "destination", "cargo", "quoteAmount", "segment", "channel", "startDate"]) {
  assert.ok(endpoints.includes(`\"${field}\"`), `The commercial record contract must preserve ${field}`);
}
assert.match(leads, /conversion is not automated/, "Lead UI must disclose its workflow boundary");
assert.match(opportunities, /conversion is not automated/, "Opportunity UI must disclose its workflow boundary");
assert.match(quotations, /Automated quote-to-contract or booking conversion is not available/, "Quotation UI must disclose its workflow boundary");
assert.doesNotMatch(opportunities, /currency:\s*r\.currency\s*\?\?\s*"SAR"/, "Opportunity currency must not be invented");
assert.doesNotMatch(quotations, /currency:\s*r\.currency\s*\?\?\s*"SAR"/, "Quotation currency must not be invented");
assert.match(campaigns, /Campaign-to-lead creation and revenue attribution are not automated/, "Campaign UI must disclose its attribution boundary");
assert.doesNotMatch(campaigns, /r\.currency\s*\?\?\s*"SAR"/, "Campaign currency must not be invented");

for (const field of ["rateCardName", "billingBasis", "fuelSurchargePercent", "effectiveDate"]) {
  assert.ok(rateCards.includes(`${field}:`), `Rate Card creation must send ${field}`);
}
assert.doesNotMatch(rateCards, /currency\s*\?\?\s*"SAR"/, "Rate Card currency must not be invented");

console.log("Module relationship integrity contract passed");
