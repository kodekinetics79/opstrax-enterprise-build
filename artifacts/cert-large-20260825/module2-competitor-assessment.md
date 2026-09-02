# Module 2 competitor assessment

Checked 2026-08-28 against official vendor sources only.

## Market benchmark

- [Samsara telematics](https://www.samsara.com/products/telematics) combines its gateways and sensors with GPS, engine diagnostics, ELD, routing, maintenance, and connected operations. Its [Device Health reports](https://kb.samsara.com/hc/en-us/articles/33583268473997-Device-Health-Reports) provide central status, recommended actions, filters, sorting, export, scheduled reports, and custom-role filtering; its [gateway health guidance](https://kb.samsara.com/hc/en-us/articles/360036156791-Vehicle-Gateway-VG-Device-Health-Definitions-and-Troubleshooting) is operationally detailed.
- [Geotab GO](https://www.geotab.com/products/vehicle-tracking-device/) combines GPS and engine diagnostics with MyGeotab and a marketplace. Its [Faults view](https://support.geotab.com/help/mygeotab/maintenance-and-diagnostics/maintenance/faults-overview) distinguishes vehicle/device source, protocol, controller, state, and severity. Its [device reports](https://support.geotab.com/help/mygeotab/reports/productivity-reports/device-reports) expose serial, firmware, communications, and activation posture.
- [Motive Fleet View](https://helpcenter.gomotive.com/hc/en-us/articles/30921412359581-Fleet-View-Live-Tracking) combines live/historical GPS with entity context. Its [Device Status report](https://helpcenter.gomotive.com/hc/en-us/articles/28208608944925-Device-Status-Report) centralizes issues and recommended actions, while [Fault Codes](https://helpcenter.gomotive.com/hc/en-us/articles/30922611075229-Fault-Codes) includes current/history, severity, export, alerts, and driver sharing.

## Credible OpsTrax strengths

1. Evidence-first truth: device-fix time, gateway receipt, provenance/confidence, and explicit no-data, delayed, stale, and blocked states are kept distinct.
2. Fleet-master-native context: immutable serial, vehicle, driver, branch, and effective-dated assignment evidence can connect DeviceOps to dispatch and maintenance.
3. Exception-first fleet control: server paging, filter, sort, and complete authorized exports are directionally appropriate for a 1,100-device tenant.
4. Tenant and branch governance: role-scoped browser journeys, fail-closed runtime provenance, and a reproducible signed-ingest harness are useful pilot differentiators when final Chrome evidence passes.

## Material gaps

- Provider onboarding, authorization, mapping, backfill, reconciliation, disconnect, and sync-health are not yet a complete customer workflow.
- DeviceOps does not yet match incumbent guided remediation, scheduled health reporting/alerts, support-ticket/RMA lifecycle, firmware campaign, or replacement workflows.
- GPS still needs final proof for identity-preserving map drilldown, history/playback, proximity and grouping behavior, and sustained 1,000-position performance.
- Diagnostics still needs final browser proof for authenticated OBD/J1939 evidence, fault history, protocol/controller/source depth, alert distribution, acknowledgement/escalation, and fault-to-work-order closure.
- OpsTrax has not demonstrated proprietary hardware, certified ELD, camera/video telematics, EV/charging breadth, or a large integration marketplace.

## Pilot positioning

Position OpsTrax as a governed, evidence-first operations overlay for an existing telematics provider: it integrates device, GPS, and diagnostic truth with fleet master, branch security, dispatch, and maintenance. Do not position it as a hardware telematics replacement.

Do not claim feature parity with Samsara, Geotab, or Motive; real-time/to-the-second GPS; complete trip playback; universal OBD/J1939/CAN support; predictive breakdown prevention; plug-and-play hardware; certified ELD/HOS/IFTA; OEM/EV/video breadth; production Motive/Geotab pipelines; or complete DeviceOps/RMA/firmware automation until independently evidenced.
