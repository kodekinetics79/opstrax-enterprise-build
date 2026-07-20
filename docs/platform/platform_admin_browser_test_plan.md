# Platform Admin — Browser Walkthrough Test Plan

Manual verification script for the `/platform/*` control plane. Run against the
local stack (`http://localhost:10000`) or the deployed frontend. Keep DevTools
open (Console + Network) for every step — **any console error or failed request
(other than an intentional 401/403 test) is a defect.**

Prereqs: platform admin credentials (from `PLATFORM_SUPERADMIN_EMAIL` /
`PLATFORM_SUPERADMIN_PASSWORD` env). Never type credentials into any log/ticket.

## 1. Platform login
1. Navigate to `/platform`. Expect redirect to `/platform/login` (no flash of
   protected content).
2. Submit a wrong password → inline error, stays on login, **no console error**.
   Repeat 5×: the 6th attempt must return HTTP 429 (lockout) — check Network tab.
3. Log in with valid credentials → lands on `/platform` Command Center.
4. Confirm `localStorage` has `opstrax.platform.session.v1` and the tenant app
   session key is untouched (separate stores).

## 2. Command Center dashboard
1. KPIs (MRR, ARR, tenant lifecycle counts) render with real numbers.
2. Top risks and recommended actions populate or show a clean empty state.
3. No request in Network tab fails.

## 3. Tenant list + search/filter
1. Open Tenants. Table lists tenants with status badges, package, seats, MRR.
2. Type a partial tenant name in the search box — list narrows live.
3. Type garbage (`zzzz###`) — "No tenants match" empty state appears.
4. Set the status filter to `suspended` — only suspended tenants (or the empty
   state) show. Clear filters restores the list.

## 4. Tenant detail drawer
1. Click the Acme row. Drawer shows status, package, MRR, seat limit, users,
   owners, trial/contract dates, and the entitlement toggle list.
2. All fields render as text (create a tenant named `<b>bold?</b>` first if
   validating XSS — it must render literally, not as markup).

## 5. Create tenant
1. New Tenant → leave name blank → Create disabled.
2. Fill name + seat limit + trial days → Create → drawer closes, list refreshes,
   new tenant visible with `trial` badge.
3. Repeat with the SAME company code (create via API or reuse) → inline error
   showing the 409 conflict message; no crash.

## 6. Edit tenant / limits
1. Open the new tenant → Limits → enter seat limit `25` → Save → success note,
   drawer data refreshes showing 25.

## 7. Suspend / reactivate / cancel (destructive-action confirmations)
1. Click **Suspend** → a confirmation dialog MUST appear (no immediate action).
   Cancel it → nothing changes.
2. Confirm suspend → success note "sessions revoked", status badge → `suspended`.
3. In another browser profile, attempt tenant login as a user of that tenant →
   HTTP 403 "organization's account is not active".
4. Click **Activate** → status returns to `active`.
5. Click **Cancel** → dialog requires TYPING THE TENANT CODE; the confirm button
   stays disabled until it matches exactly. Confirm → status `cancelled`.
6. Reactivate for cleanup (or offboard the temp tenant via the API workflow).

## 8. Feature/module controls
1. In tenant detail, toggle **dispatch** OFF → toggle flips, source shows
   `override`.
2. As a logged-in user of that tenant, call any dispatch page/API → HTTP 403
   "Module disabled" (server-enforced, not just hidden UI).
3. Toggle back ON → tenant access restored without re-login.

## 9. Tenant admin invite / session revoke
1. In tenant detail, enter an email → **Invite / Reset admin** → success note.
2. **Revoke all tenant sessions** → confirmation dialog → confirm → success note
   with revoked count; any active tenant session gets 401 on next API call.

## 10. Packages & billing
1. Packages page lists packages; create/edit reflects immediately.
2. Billing page lists invoices; Mark paid updates the row + Command Center KPIs.

## 11. Health & audit
1. Health page shows per-tenant score with green/yellow/red badges.
2. Audit page lists recent actions. Verify every action performed above appears
   (tenant.created, tenant.updated, tenant.suspend, tenant.sessions_revoked,
   entitlement.disabled, invoice.paid, platform.login, platform.login_failed).

## 12. Permission-gated UI (non-super roles)
1. Log in as a `finance_admin` → Tenants page hides New Tenant + subscription
   action buttons; Billing works. Navigating directly to a forbidden route
   redirects to the command center.

## 13. Unauthorized handling
1. In DevTools, delete `opstrax.platform.session.v1`, click any nav item →
   clean redirect to `/platform/login` (401 interceptor), no error flash.
2. A TENANT app token must never grant access to `/platform/*` screens or APIs.

Record results (pass/fail + screenshots for failures) alongside the release
notes for the pilot.
