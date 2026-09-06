# G3A Current-Main Closeout

This branch closes the controllable HOS source-truth P1 on current main.

Software boundary after this change:
- no seeded/default remaining-time value is treated as legal authority;
- dispatch and available-driver legal-time data require a fresh tenant-scoped Authoritative `hos_clocks` source;
- operational HOS violation alerts require the same persisted authority/provenance boundary;
- unknown/unverified clocks are null/Unavailable and fail closed;
- Stage99 is enrolled in the canonical protected migration chain;
- daily HOS certification and ELD malfunction/recovery workflows already present in current main remain intact.

This is **ENGINEERING COMPLETE / EXTERNAL EVIDENCE HOLD**, not regulated ELD/HOS certification. Final HOS promotion still requires the selected certified ELD/provider/device/application boundary, authentic source events, jurisdiction-specific acceptance, visible Chrome evidence and independent regulatory/Security/SDET/Fleet Product acceptance under #116/#128.
