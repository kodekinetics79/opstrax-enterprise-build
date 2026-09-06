# G4 Current-Main Port — Author Correction Addendum

Date: 2026-09-05

Role: implementation author only; excluded from certification and final gate authority

Rejected evidence parent: `c447cb6332d89e489967403aedf8a62cc3d217f3`

Rejected evidence tree: `20a2113879e9fea42090646cc4a91779d5eaaacf`

This bounded successor responds to the two P0 perspective-B blockers in `INDEPENDENT_G4_EXACT_CANDIDATE_PERSPECTIVE_B_REVIEW.md` (SHA-256 `f4017750`) and the one fixture-isolation blocker in the perspective-A report (SHA-256 `2690d208325fae0052865fbb91b4bb652cbd87f5b7c00bf0457e473bee87d215`). The exact successor commit/tree are recorded in the post-commit handoff because a commit cannot contain its own identity.

## Corrections

### Bounded coaching detail and export

Coaching detail now consumes a type-checked projection from the exact successful, idle `['coaching','detail',id]` query. Unknown, inherited, prototype, non-scalar, malformed collection, and identity/version/status-invalid input cannot flow into the projected view.

The approved displayed task fields are:

- task context: `taskNumber`, `driverId`, `driverName`, `coachingType`, `priority`, `status`, `assignedToUserId`, `assignedToName`, `assignedByName`, `dueAt`;
- coaching content: `title`, `description`, `aiScript`; and
- acknowledgement/outcome: `driverAcknowledged`, `acknowledgedAt`, `beforeSafetyScore`, `afterSafetyScore`, `effectivenessScore`, `completedAt`.

The projected record also retains `id` and `rowVersion` solely for exact action ownership. Nested allowlists are:

- notes: `id`, `noteType`, `noteText`, `createdAt`, `createdByName`;
- related safety: `id`, `eventNumber`, `eventType`, `severity`, `reviewStatus`, `occurredAt`;
- related dashcam metadata: `id`, `eventNumber`, `eventType`, `severity`, `reviewStatus`, `occurredAt`; and
- audit: `actionName`, `actorName`, `createdAt`.

No recommendation object is displayed from this coaching response. CSV is a separate scalar projection limited to `id`, `rowVersion`, `taskNumber`, `driverId`, `driverName`, `coachingType`, `priority`, `status`, `assignedToName`, `dueAt`, `driverAcknowledged`, `acknowledgedAt`, `beforeSafetyScore`, `afterSafetyScore`, `effectivenessScore`, and `completedAt`. Coaching descriptions, scripts, notes, related records, audit records, and unknown fields are not exported by this action.

### Modal write admission

Add-note and completion modals now own an explicit modal session, attempt, pending state, selected record ID, row version, detail generation, and exact QueryClient data identity. Their submit callbacks synchronously re-read the exact current query and direct permission at invocation. Terminal error, fetching, paused, replacement (including same ID/version), removal, selection/detail generation change, permission loss, pending flight, or superseded modal session produces zero mutation. The modal/draft remains mounted and receives the fixed current-detail-unavailable guidance. A stale close/reset callback is inert against a replacement modal.

Unknown mutation settlement behavior is unchanged: input is retained, uncertainty remains neutral, and no mutation retry/resubmit is offered. Known strict coaching-creation receipts remain the only basis for origin-bound GET-only related-read recovery. The original author report's mistaken phrase “completion receipts” was corrected to “coaching creation receipts.”

### HTTP fixture isolation

The fixture-owned `HttpClient` now owns a `SocketsHttpHandler` with `UseProxy = false` and `AllowAutoRedirect = false`. Disposal of the client disposes that handler. No fixture route, case, database behavior, package, or project-file change accompanied this correction.

## Verification

- all six direct focused frontend suites: pass;
- coaching detail suite now covers bounded display/export plus actual QueryObserver terminal error, fetching, paused, same-identity replacement, changed-version replacement, removal, selection/generation change, superseded modal sessions, stale submit/close before rerender, and one exact-current completion control;
- coaching note suite now proves a locally owned draft/modal survives same-ID current-query replacement with zero POST and fixed guidance;
- retained camera manual-metadata contract: 29/29 pass;
- independent camera current-query fault probe: pass with zero controlled writes/downloads after actual terminal cache error;
- unchanged registered frontend aggregate: pass;
- TypeScript/Vite production build and bundle budget: pass, 2,691 modules, 210 chunks, largest raw chunk 314.86 KiB and gzip 95.23 KiB;
- configured ESLint: pass;
- Release `.NET` test-project compile after handler correction: 28 warnings, 0 errors;
- relevant no-database `.NET` suites: 31/31 pass (15 conditional UI-contract cases plus 16 scorecard cases);
- exact PostgreSQL fixture discovery: 27 cases; none executed;
- `git diff --check`: pass.

## Unchanged HOLDs

No PostgreSQL, browser, provider, device, footage, publication, merge, deployment, customer journey, or qualified-human evidence was produced. The six focused frontend suites remain deliberately unregistered because package ownership is excluded. The author does not certify the successor or assert GO/LIMITED GO. PostgreSQL remains on HOLD until both independent perspectives accept the exact immutable successor and the root owner allocates a serialized window.
