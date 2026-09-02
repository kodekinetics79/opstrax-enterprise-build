# OpsTrax Telematics Security Report

Run: `RETEST-20260821-1035-R1` · Status: implementation complete, **pending Wave C adversarial review** (all suites now executed: 39+47+169+8 green).

## Applicability finding (changes severity posture)

The public TCP edge is **not deployed** (opstrax-telematics-gateway.fly.dev = NXDOMAIN) and the entire `telematics/deploy/` tree plus `FileForwardOutbox.cs` do **not exist** at deployed SHA `979c142`. DEF-003/004a/005 are therefore **pre-ship blockers**, not live exposure. The single live-relevant finding was DEF-004b: `render.yaml` provisioned `Telemetry__GatewaySecret`, whose mere presence is a startup-fatal validation failure in protected environments.

## Repairs (all verified in-tree by the orchestrator)

| Defect | Repair | Threat closed |
|---|---|---|
| DEF-003 | Installer fails closed (exit 2) without `--allowlist`/existing file; example file is a template of inadmissible `<IMEI-15-DIGITS>` placeholders validated against the real parser; deploy-compose phantom bind-mount guarded | Internet-reachable edge can never boot admitting a publicly-known identifier |
| DEF-004a | `--secret` argv removed (env `OPSTRAX_GATEWAY_SECRET` or `--secret-file`, umask 077); gateway `CreateApplicationBuilder()` without args — no CLI config source | Secret material never appears in a world-readable process table |
| DEF-004b | `Telemetry__GatewaySecret` + dead `Telemetry__DeviceSecret` removed from `render.yaml` and root compose; **ConfigValidationService untouched** (the validator was correct) | Operators can no longer brick the API by following the blueprint; no cross-tenant skeleton key path |
| DEF-005 | Outbox format v2: AES-256-GCM `version‖keyVersion‖nonce‖tag‖ciphertext`, AAD binds header+filename (rename-forgery fails auth); entry files 0600, dir 0700; age from filename ticks (no cleartext timestamp); wrong-key → corrupt-drop; protected Https path **refuses to boot without a key** (proven by real-executable composition test); key zeroed on dispose; `OUTBOX_KEY_MANAGEMENT.md` documents provisioning/rotation/loss semantics | Disk theft, backup exfiltration, or non-root local read yields no fleet positions or device identities |

Breaking-format decision: **explicitly safe** — no deployed edge, no on-disk outbox data exists anywhere. Format v1 retired; readers accept only v2.

Spec correction credited to implementation review: an all-zeros IMEI placeholder would have been admissible (parser accepts any 8–20 digit string; faulted trackers self-report all-zero IMEIs) — placeholders are non-digit template tokens instead.

## Test evidence

| Suite | Result | Artifact |
|---|---|---|
| Protocols | 39/39 | `artifacts/telematics-protocols-RETEST-R1.trx` |
| Security (incl. 8 new DeploymentHardeningContractTests) | 47/47 | `artifacts/telematics-security-RETEST-R1.trx` |
| Integration (non-DB) | 169/169 | `artifacts/telematics-integration-RETEST-R1.trx` |
| Postgres durability (8 facts incl. StoreForward encryption) | **8/8 PASS** | `artifacts/telematics-db-RETEST-R1.trx` |

Key new assertions: persisted outbox bytes reveal no IMEI/coordinate in UTF-8, UTF-16LE, or 4-dp decimal forms and refuse JSON parse; entry files owner-read-write only; wrong key discards as corrupt; protected boot refuses without the key; example allowlist admits **zero** entries through the real parser; manifests contain neither retired key.

## Lifecycle gaps carried by other packets

DEF-022/023 frontend: **done, verified** (Packet 4). DeviceRevoke state-transition ledger row + tenant-transaction alignment: Packet 5. Implicit activation-by-first-fix audit event: register follow-up. Device `UAT-1035-GPS01` remains Revoked; nothing in this run touches it.

## Open follow-ups

Stale docs (`telematics/deploy/README.md:81`, `cloud-init.yaml:104`) still show `--secret` — fail-safe today (exit 2), assigned to Gate 4 docs packet. GT06 protocol remains presumed until a real captured frame is fingerprinted (`tools/telematics/fingerprint.py`) — unchanged pre-existing risk.
