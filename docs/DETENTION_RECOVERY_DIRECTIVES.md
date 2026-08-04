# Detention Recovery — Market-Grounded Product Directives

_From a leadership panel: Sales / Product Development / Commercial / New-Client-Acquisition directors + three practitioner lenses (a 5-year Samsara fleet-ops user, a carrier billing manager, and an ADVERSARIAL shipper AP clerk whose job is denying detention claims), synthesized by CPO/CRO. This document governs the build of the monitoring module's detention wedge._

## Consensus market truths
- Rip-and-replace telematics is dead in the 5-200 truck segment. All seven seats independently said the same thing: the buyer is 2-5 years into a Samsara/Motive hardware contract, and any pitch that implies a device swap ends the meeting. The only viable motion is an overlay that ingests the incumbent's cloud API — which simultaneously makes the single-protocol and no-ELD gaps strategically irrelevant for 12-18 months.
- Detention is a collections and process problem, not a detection problem. Fleets already know the truck sat 4 hours — the driver texted dispatch. The leak is: notice-before-expiry clauses missed, claim windows expired, clock-start disputed, back office gives up after the second email. Motive/Samsara dwell reports are commoditized; 'detention that gets PAID' is not.
- The clock-start rule is the entire dispute, not the dwell math. Free time starts at the later of appointment time or check-in per most contracts — never at geofence entry. Every practitioner seat (ops, billing, the shipper AP clerk) said a geofence-anchored clock produces confidently wrong invoices that get disputed 100% of the time and poison the legitimate claims.
- The pre-expiry notification is where the money is legally won or lost. Most rate cons waive detention unless the broker/shipper is notified while the truck is still on site. Billing manager, ops manager, and the AP clerk all ranked an automated, timestamped, logged notice as worth more than the entire invoicing pipeline. 30s-5min batch latency is completely adequate for this — no seat asked for real-time push.
- Auto-send billing is a relationship grenade; approval queues are non-negotiable. A fleet's top shipper is often 30% of revenue. Every seat demanded draft-review-approve with per-customer opt-in automation; the AP clerk confirmed the shipper-side response to machine-generated claim volume is freight-audit routing and blacklisting, not payment.
- The product is the artifact, not the dashboard: a boring, complete, no-login PDF/link an AP clerk can verify in under 2 minutes. 'Tamper-evident cryptographic bundle' is a CTO sentence that sells to nobody in this segment — recognizability, completeness, and the shipper's own references (PO/BOL/appointment ID) determine win rate.
- QuickBooks is the ledger of record for the 5-50 truck buyer and the controller will veto anything that threatens it. The double-entry GL is OpsTrax's architectural moat and diligence proof point, but it must ship as invisible plumbing behind an idempotent QBO sync — GL-first onboarding converts a 30-day found-money sale into a 12-month accounting migration.
- The freight recession is a headwind for 'platform' and a tailwind for 'found money.' Budgets are frozen for subscriptions and open for recovered revenue with sub-90-day payback. The recovered-dollars scoreboard (identified → billed → collected) is simultaneously the sales demo, the owner's screenshot, the renewal defense, and the honest feedback loop on whether the wedge collects and not just invoices.
- The 90-day historical lookback audit is the sales motion: 'hand us a read-only API key, we show you the detention you never billed last quarter, in dollars, from your own data.' Three director seats independently designed this exact play.
- Geofence setup friction is the silent trial-killer. Nobody hand-draws 300-400 consignee polygons; fences must auto-build from stop clustering with human confirmation, and a shared curated library of major DCs is a compounding cross-tenant moat.
- Do not chase Samsara onto their turf. ELD/HOS, AI dashcam, sub-30s streaming, and broad native-protocol expansion are unanimous 'later' items — a capital bonfire against 100x R&D budgets, and the overlay strategy makes them unnecessary. The money layer is where incumbents structurally cannot follow because they have no billing/GL weld.
- Brokers are a second market with the same engine and faster willingness to pay: validating inbound carrier claims and facility scorecards is a cost-avoidance sale, and every broker seat seeds two-sided acceptance of the evidence format.

## Conflicts and resolutions
- Pricing — performance/gain-share (Sales, Acquisition, AP clerk: 'contingency forces you to build for win-rate') vs flat tiers (Commercial: 'contingency pricing death-spirals into revenue-share audit disputes at renewal'). RESOLUTION: flat per-truck ($15-25) is the default; gain-share (10-15% of COLLECTED detention, capped) is offered only as a first-cohort/pilot option that contractually converts to flat tiers at first renewal. We get the risk-reversal sales weapon and the win-rate discipline without the long-run margin pathology.
- Rate-con terms capture — AI/OCR extraction as v1-must (Sales, Product) vs 'manual entry is fine in v1, just make it under 60 seconds' (Acquisition, Billing Manager — the actual user). RESOLUTION: ship fast manual rule cards in v1 (the practitioner who does this job says a sub-minute form beats a mediocre model), OCR-suggested terms with one-click confirm as the first fast-follow. Wrong extracted terms are worse than typed terms — a mis-parsed cap poisons the whole invoice per the AP clerk.
- Auto-generated geofences (Sales/Acquisition: mandatory for onboarding survival) vs 'a wrong auto-fence is worse than screenshots' (Billing Manager) and 'I will argue the polygon is self-serving' (AP clerk). RESOLUTION: auto-generate but never auto-trust — every fence passes a human confirm queue, polygons are versioned and locked into each evidence bundle, and parcel-boundary checks flag fences that spill onto public roads. Onboarding speed and evidence credibility are both existential; the confirm queue buys both.
- Dual-ring gate/dock geofencing — Billing Manager says v1-must, Product/Ops say should. RESOLUTION: fast-follow, because the v1 later-of-appointment clock-start already neutralizes most 'your clock started in our parking lot' disputes (the AP clerk's #1 legitimate denial is early arrival, which appointment-anchoring kills). Dual-ring then upgrades evidence from good to nearly undisputable.
- Tamper-evidence positioning — the founding 'Provable Money Layer' framing vs every seat saying crypto is a footnote, and the AP clerk's sharper point: a self-attested hash proves nothing about a self-serving polygon. RESOLUTION: strip cryptography from all buyer-facing language ('audit-ready evidence'), but INVEST in the provenance features the adversary actually respects — versioned polygons, disclosed ping cadence, lower-bound billing, independent RFC 3161 timestamps. Build the proof for the skeptic, market the payment rate.
- Packaging — two SKUs with a cheap tracking tier (Commercial) vs single standalone product (Sales, Acquisition, Ops). RESOLUTION: one standalone 'Detention Recovery' SKU. A tracking tier invites the live-map latency comparison we lose and muddies 'keep your Samsara.' Monitoring/tracking surfaces exist inside the product but are never the pitch.
- Module framing — internally this is the Vehicle Monitoring module evolving into a money layer; externally, per every seat, it must never be sold as monitoring. RESOLUTION: engineering keeps building on the geofence/telematics engine; GTM sells only 'Detention Recovery.' 'Provable Money Layer' is banned from sales materials — as the ops manager put it, no fleet manager will ever say that phrase out loud.
- Connector scope — Samsara+Motive+Geotab (Acquisition) vs top-2 only (Commercial). RESOLUTION: Samsara and Motive in v1 (they blanket the ICP), Geotab fast-follow. Broad multi-protocol native expansion stays 'later' per the unanimous overlay strategy.
- Broker mode — same engine, but a broker wants to REJECT the very claims a carrier wants to prove, and 'auto-billing shippers loses me the freight.' RESOLUTION: fast-follow with fully split messaging — carriers get recovery, brokers get claim validation and facility scorecards for negotiation leverage, never auto-send. Two pitches, one engine; broker adoption doubles as the path to two-sided evidence acceptance.

## Build directives — V1 MUST

### Ship Samsara and Motive cloud-API ingest connectors as the PRIMARY onboarding path — positions and existing geofence events feeding the same dwell engine as native ingest, connected via API token in under 15 minutes, zero OpsTrax hardware.
_Demanded by: All 7 seats — the only unanimous item on the panel_
The entire ICP is mid-contract on incumbent hardware; without this there is no wedge, only a hardware sale we lose. It converts Samsara's install base from moat into distribution and makes the single-protocol and no-ELD gaps irrelevant. Nothing else on this list matters if this doesn't ship.

### Per-customer/per-lane free-time rule cards computed from contract terms, never a global default: clock-start basis (later-of appointment / check-in / geofence arrival), free hours, hourly rate, billing increment + rounding direction, caps, excluded hours/weekends, early-arrival handling, notify-before-expiry flag, claim-submission window — enterable from a rate con in under 60 seconds, with per-load override.
_Demanded by: Product Dev, Billing Manager, Ops Manager, AP clerk (adversary), Sales_
Every detention calculation is downstream of terms, and the AP clerk was explicit: a claim that already matches the rate con removes 80% of the denial toolkit, while one wrong term discredits the clean lines too. Terms-entry friction is also the historical trial-killer for the category — hence the 60-second constraint.

### Capture appointment time on every load (manual entry minimum) and default the billable clock to the later of appointment vs arrival, with the applied rule stored in the evidence bundle.
_Demanded by: AP clerk (adversary), Ops Manager, Billing Manager_
Early arrival is the #1 legitimate denial reason and GPS alone cannot see it — drivers arrive early on purpose. This single field flips the majority of real-world claims from deniable to payable, and it neutralizes most gate-vs-dock disputes before dual-ring fencing even exists.

### Pre-expiry detention alerting and automated notice: alert dispatch at ~75% of free time (ETA-aware), and fire a timestamped, logged 'meter running' notification (email/SMS, pre-drafted with in-geofence proof) to the broker/shipper's designated contact before free time expires — welded into the evidence bundle.
_Demanded by: Billing Manager, AP clerk (adversary), Ops Manager, Product Dev, Acquisition, Sales_
Most rate cons void detention not requested while the truck is on site — more money is lost to missed notice than to missing GPS. The adversary called this the single feature that converts a deniable claim into one she MUST pay, and it makes the product daily-use (prevention) instead of month-end, which is the retention profile that survives renewal review.

### Approval queue as the only path to billing: every charge auto-DRAFTS with evidence attached into a review/edit/waive/approve queue; approval is what posts it. Auto-send exists only as per-customer opt-in earned later; 'prior approval required' customers can never auto-send.
_Demanded by: All 7 seats; AP clerk confirms the shipper-side retaliation to auto-billing at scale_
One false detention invoice to a fleet's anchor customer ends the trial, the account, and the reference. Preview-first converts the scariest objection into the demo's best moment, and the adversary confirmed that machine-generated claim volume triggers freight-audit routing and carrier blacklisting, not payment.

### The dispute-ready evidence packet: a branded, no-login PDF/shareable page per charge — geofence polygon over satellite imagery, breadcrumb trail with disclosed ping cadence, entry/exit as a bounded interval billed from the LOWER bound ('exit between 14:02-14:06; billed from 14:02'), appointment vs actual, free-time math showing the contract rule applied, notice log, and the shipper's own references (PO, BOL, appointment ID, rate-con number, facility name as the shipper knows it) validated as present before the invoice can generate.
_Demanded by: All 7 seats on the packet; AP clerk specifically on references, disclosed cadence, and lower-bound billing_
The artifact that survives the AP department IS the product — a dashboard nobody can forward collects nothing. Conceding the ping-gap minutes voluntarily costs pennies and pre-empts the adversary's precision attack; missing PO/BOL match is an automatic process denial regardless of GPS quality.

### Claim-window enforcement: countdown timers on every unbilled detention event, and the drafted charge ready for one-click submission within days of the event — never batch-billed from a spreadsheet 60-90 days later.
_Demanded by: AP clerk (adversary): 'expired = auto-deny, zero guilt, contractually bulletproof'_
Window expiry is the cheapest denial in the adversary's toolkit and carriers hand it over constantly. Speed alone beats every spreadsheet workflow in the market, and no incumbent automates this.

### QuickBooks Online invoice-line push that is idempotent (provably never double-posts) plus a McLeod-friendly CSV export — working WITHOUT adopting OpsTrax billing, GL, period close, or settlements. The GL runs silently underneath as the audit backbone.
_Demanded by: Product Dev, Commercial, Acquisition, Ops Manager, Billing Manager, Sales_
The controller is the economic buyer's gatekeeper and will veto anything threatening QuickBooks; one duplicated invoice ends trust permanently. Forcing ledger migration before first recovered dollar guarantees setup churn — the GL is the upsell, never the toll gate.

### Auto-generate customer-site geofences from historical stop clustering + geocoded addresses, gated by a human confirm/adjust review queue; version every polygon and lock the version used into each evidence bundle.
_Demanded by: Sales, Acquisition (onboarding survival); Billing Manager and AP clerk (credibility guardrails)_
Manual fence-drawing for 300-400 sites is the week-two trial dropout point, but an unreviewed wrong fence is worse than screenshots and the adversary's go-to attack is 'you drew the fence to include the public road.' Auto-generate plus confirm plus versioning buys speed and credibility simultaneously.

### The 90-day lookback 'found money audit': on connecting an API key, retro-compute historical GPS against the customer list and free-time rules to produce a dollar figure of unbilled detention — presentable pre-contract.
_Demanded by: Sales, Commercial, Acquisition (independently designed the same play)_
This is the demo, the POC, and the proposal in one artifact — 'you left $23k on the dock last quarter' converts skeptics with their own data at zero prospect cost, and it is the sharpest answer to recession budget freezes. It ships v1 because it IS the sales motion, not a feature.

### Recovered-revenue funnel dashboard: detected → notified → billed → collected → written off, in dollars, per customer and per facility, with a running 'recovered vs subscription cost' counter front and center for the owner.
_Demanded by: Sales, Product Dev, Commercial, Acquisition, Ops Manager, Billing Manager_
It is the screenshot the ops manager takes to the owner, the renewal defense ('the counter is bigger than the contract'), the case-study engine, and the honest internal feedback loop on whether the wedge collects rather than merely invoices. Tracking 'collected' — not just billed — is what makes the ROI story survivable.

## Build directives — FAST FOLLOW

### Rate-con upload with AI/OCR term extraction suggesting rule-card values for one-click confirmation.
_Demanded by: Sales and Product Dev (as must); deferred per Billing Manager and Acquisition who validated fast manual entry for v1_
Removes the hidden per-load admin cost that stalls adoption at scale — but a mis-extracted cap poisons an invoice, so it ships as suggestion-with-confirm after the manual path proves the data model.

### Dual-ring geofencing (property/queue zone vs dock zone) with clock rules keyed to the correct zone, plus tractor-dwell vs dropped-trailer-dwell distinction.
_Demanded by: Billing Manager (as must), Product Dev, Ops Manager, Commercial_
Answers 'on property 08:02, first dock proximity 10:47' to the check-in counter-argument, and prevents the two credibility-destroying errors: undercounted street-queue detention and 3-day dropped trailers flagged as detention. Fast-follow (not v1) only because appointment-anchored clocks absorb most of these disputes at launch.

### Curated shared geofence library of major DCs (Walmart, Kroger, Target, food-service networks) with one-click adoption and fence-confidence flagging.
_Demanded by: Ops Manager, Billing Manager_
Collapses time-to-first-recovered-dollar below two weeks and compounds into a cross-tenant data moat no telematics vendor or TMS assembles.

### Detention/accessorial AR sub-ledger and collections workspace: dispute status, short-pay tracking with automatic credit-note reconciliation to adjusted AR, detention-specific aging, per-broker/per-shipper acceptance and win-rate analytics, factoring-compatible export, and AP-contact dunning sequences.
_Demanded by: Billing Manager, Ops Manager, Commercial, Sales; AP clerk flagged clean partial-payment reconciliation as OpsTrax's genuine GL differentiator_
Winning the argument and getting the check are two different jobs; billed-but-not-collected is where the ROI story dies today, inside 'miscellaneous open balance.' Per-shipper win-rate data becomes proprietary moat data — and it's the first place owning a real GL visibly pays off for the buyer.

### Driver settlement linkage: billed/collected detention flows automatically into driver pay with per-fleet policy (pay-when-billed vs pay-when-collected).
_Demanded by: Ops Manager ('the feature Samsara structurally cannot build')_
Detention is a driver-retention problem wearing a billing costume — drivers quit over unpaid dock time. This is the first visible proof that owning both physical and financial truth matters, and it is unreachable by any tracking-only competitor.

### Broker mode with split messaging: validate inbound carrier detention claims against tracking/site data with approve/reject workflow, plus facility dwell scorecards (avg dwell, detention frequency by shipper/receiver) as negotiation ammunition for both brokers and fleets.
_Demanded by: Sales, Product Dev, Commercial, Acquisition_
Same engine, second market, cost-avoidance framing that brokers buy faster than carriers buy revenue tools; higher willingness-to-pay, and every broker seat drags carrier fleets into the ecosystem while seeding two-sided acceptance of the evidence format. Never pitched as auto-billing shippers.

### Evidence provenance hardening: independent trusted timestamp (RFC 3161) on bundle hashes, parcel-boundary checks flagging fences that extend onto public roads, timestamped geofence edit history — plus Geotab as the third connector.
_Demanded by: AP clerk (adversary); Acquisition on Geotab_
A self-attested hash is still the claimant's data — independent timestamps and parcel-checked, versioned polygons close the 'self-serving evidence' rebuttal that survives everything else. Marketed as 'audit-ready,' never as cryptography.

### Driver-app corroboration: photo capture of gate tickets/BOL timestamps attached to the dwell event as a second evidence signal, plus EDI 210/214 delivery of invoice+packet for fleets billing enterprise shippers.
_Demanded by: Billing Manager; Product Dev on EDI_
Gate documents are the receiver-recognized truth — GPS plus gate ticket is nearly undisputable and fixes driver detention-pay trust as a side effect. EDI isn't needed for the first 20 customers but is mandatory for the next 200 billing large shippers.

### Gain-share pricing pilot: 10-15% of collected detention, capped, first cohort only, contractually converting to flat tiers at first renewal.
_Demanded by: Sales, Acquisition, AP clerk (for); Commercial's renewal guardrail (against long-run contingency)_
Obliterates the budget-risk objection in a recession and is a model hardware vendors structurally cannot match — but only after early-cohort collected-vs-billed data proves unit economics, and never as a perpetual model.

## Build directives — LATER / DO NOT BUILD NOW

### Broker/shipper acceptance program: put the packet format in front of CHR/TQL/Coyote carrier-payments teams, publish per-broker acceptance stats, then pursue direct evidence-feed integrations with freight-audit firms (Cass, nVision) and a shipper-side acknowledgment portal.
_Demanded by: Billing Manager, Sales, AP clerk_
Two-sided acceptance is the endgame moat — the freight-audit firms are the actual adjudicators at large shippers — but the network play needs carrier-side density and win-rate data first. The free unauthenticated evidence page ships v1 and is the seed of this network.

### Expand the engine to layover, TONU, and lumper accessorials.
_Demanded by: Acquisition, Sales (expansion path)_
Same geofence + rules + evidence machinery and it multiplies recovered dollars per account — but it's only credible once detention collection rates are proven; premature breadth dilutes the wedge.

### ELD/HOS via certified partnership (not ground-up build), and HOS correlation in evidence bundles; native multi-protocol gateway expansion for direct-hardware fleets and trailer trackers.
_Demanded by: All directors as 'later'; AP clerk notes HOS context rebuts 'driver was on break'_
Required eventually to be the primary in-cab system and to close the last adversarial rebuttal, but it's a multi-year certification minefield that would starve the wedge, and the overlay strategy makes it unnecessary for 12-18 months.

### Do NOT build sub-30s real-time push or AI dashcam; document the crossing-time interpolation method instead and ban 'real-time' from sales claims.
_Demanded by: All 7 seats, explicitly_
Detention is measured in hours — batching is immaterial to the money math. Real-time visibility is Samsara's and project44's home turf with 100x the R&D budget; chasing it is how challengers die, and overclaiming 'real-time' lets a technical evaluator torch credibility on the claim that actually matters (evidence integrity).

## Evidence requirements — what forces the AP clerk to pay
- Computed by MY contract, not your defaults: the charge already respects the rate con's exact terms — free time, later-of appointment/check-in clock start, billing increments rounded DOWN, hourly/daily caps, excluded hours and weekends. A claim priced off a global rule gets the whole invoice denied, including the clean lines.
- Appointment context in the bundle: scheduled appointment time shown against actuals, with early-arrival time explicitly excluded from the billable clock. Without this, every early-arrival claim (most of them — drivers arrive early on purpose) is contestable and denied.
- Proof of contemporaneous notice: a timestamped 'meter running' notification delivered to my designated contact WHILE detention was accruing, logged in the bundle. This is contractually required in most rate cons and converts the claim from a surprise invoice into a pre-acknowledged charge I must pay.
- Filed inside the claim window: the charge arrives within days of the event, with the window compliance visible. Expired-window claims are my easiest, guilt-free, contractually bulletproof denial — close it and I lose my cheapest out.
- Matchable to my TMS in under 2 minutes: PO number, BOL, appointment ID, rate-con number, and the facility name as MY system knows it, welded onto every line. Anything I can't match fast goes to the deny pile on process grounds regardless of GPS quality.
- Verifiable without a login: the geofence polygon overlaid on satellite imagery, the breadcrumb trail with ping timestamps and stated cadence, and the computed clock showing which contract rule applied. If auditing takes longer than denying, I deny.
- Conservative, disclosed dwell math: exit shown as a bounded interval with the charge billed from the LOWER bound ('exit between 14:02-14:06; billed from 14:02'). Voluntarily conceding the ambiguity minutes removes my ping-gap increment knock-down entirely; over-reaching by 4 minutes costs you the increment and the claim's credibility.
- Provenance I can't call self-serving: versioned polygons locked into the bundle, parcel-boundary checks proving the fence doesn't include the public road where trucks queue, and an independent trusted timestamp (RFC 3161-style) on the bundle — a self-attested hash from the claimant's own SaaS proves nothing to me.
- A clean partial-payment path: when I short-pay $240 of a $410 claim, it reconciles on the carrier side into a credit note and adjusted AR instead of a six-email fight — this is the one place the GL genuinely differentiates, and it's what makes paying you easier than fighting you.
- The meta-requirement: the bundle must close ALL of my cheap denial paths (no appointment ref, no load match, expired window, unverifiable fence, missed notice) BEFORE it hits my inbox — because a 60-second denial costs me nothing and fighting costs the carrier more than the $180 claim.

## Go-to-market
**One-liner:** "Keep your Samsara. You're eating roughly $2,000 per truck per year in detention you can't prove or can't be bothered to fight — we turn the GPS you already pay for into detention that actually gets PAID: an alert before free time expires so you keep the legal right to bill, an evidence packet the broker's AP clerk approves instead of denies, and the collected dollars landing in QuickBooks — counted on one screen next to what we cost."

**Demo moment:** The first meeting is won in two beats, and neither is a live map. Beat one — the found-money number: the prospect connects a read-only Samsara/Motive API key (live, in-meeting, under 15 minutes), and the 90-day lookback renders a single figure from THEIR OWN data: 'You had $23,400 of unbilled detention last quarter, here are the 41 events, by customer.' That number, from their trucks at their customers, ends the 'why do I need this' conversation. Beat two — the side-by-side dispute: on the left, how they claim today (a Samsara screenshot, a spreadsheet, a 70% denial rate); on the right, the full OpsTrax flow running on one real dwell event — the 75%-of-free-time alert fires, the pre-drafted timestamped notice goes to the broker contact (preserving the legal right to bill), the auto-drafted charge lands in the approval queue with the no-login evidence page attached (appointment-aware clock, PO/BOL welded on, polygon on satellite, dwell billed from the conservative lower bound), the owner clicks Approve, the line posts to QuickBooks, and the Recovered Revenue counter ticks up. Close on the counter: 'that number is your renewal conversation — if it isn't bigger than our invoice, fire us.' The GL, settlements, and platform story is deliberately absent; it is the month-three expansion conversation after the counter has numbers on it.