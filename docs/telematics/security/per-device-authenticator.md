# Per-Device Trust Policy & Authenticator

Companion to `identity-trust-architecture.md` and `threat-model.md`. This documents
the **isolated, standalone** trust-policy + authenticator added under `telematics/`.
It is wired into the framing loop by a separate owner; nothing here edits
`GatewayConnection.cs`, `TcpGatewayService.cs` or `Program.cs`.

## What was added

| Concern | Type | Project |
|---|---|---|
| Auth mechanism + honest classification | `DeviceAuthMode`, `DeviceTrustTier`, `DeviceAuthModeExtensions` | Contracts/Identity |
| Per-device policy (auth mode, IP/SIM pins, replay flag) | `DeviceTrustPolicy` | Contracts/Identity |
| Opaque credential handle (never the secret) | `CredentialMaterial`, `CredentialKind` | Contracts/Identity |
| Owner + policy + credential composed | `ResolvedDeviceTrust` | Contracts/Identity |
| Observed login inputs | `DeviceLoginContext`, `DeviceHmacProof` | Gateway/Security/Auth |
| Decision surface | `IDeviceAuthenticator`, `AuthResult` (`Authenticated`/`Rejected`/`Quarantine`) | Gateway/Security/Auth |
| Enforcement | `DefaultDeviceAuthenticator` | Gateway/Security/Auth |
| Secret dereference (fail-closed) | `ICredentialKeyResolver` | Gateway/Security/Auth |
| Persistence | `004_device_trust_policy.sql` | database/migrations/telematics |

The Contracts project keeps **zero external dependencies**.

## Enforcement order (`DefaultDeviceAuthenticator`)

1. **Lifecycle gate.** `Quarantined` → `Quarantine`; `Suspended`/`Retired` → `Rejected`;
   any non-connectable state (Draft/Provisioned/AwaitingAssignment/AwaitingConfiguration)
   → `Rejected`. Only the connectable states may authenticate.
2. **Identifier presented** — else reject.
3. **Allowlist membership** — a real registry resolution (non-empty fabric device id);
   the registry *is* the IMEI allowlist, an unknown IMEI resolves to `null` upstream.
4. **Source-IP CIDR pin** (if configured) — no match, or unknown address, → reject.
5. **SIM pin** (if configured) — a changed ICCID/IMSI → **quarantine** (SIM-swap signal).
6. **Per-device HMAC** (only when `AuthMode = PerDeviceHmac`) — resolve the key from its
   opaque handle, constant-time compare `HMACSHA256(key, signedMessage)`; missing proof,
   unresolvable key, or bad signature → reject (fail-closed).

Every path fails closed: anything not explicitly cleared is reject/quarantine.

## Honesty: what is and is not authenticated

**Raw GT06/Concox (and most cheap trackers) have NO cryptographic device
authentication at the protocol level.** The IMEI in the login frame is the only
identifier and it is spoofable. For `AuthMode = ImeiAllowlistOnly`:

- A passing login returns `Authenticated` **but** with
  `IsCryptographicallyAuthenticated = false` and `TrustTier = LowSpoofable`. It never
  masquerades as proof of identity.
- The real trust model for these devices is: **explicit provisioning + IMEI allowlist
  + optional source-IP / SIM pinning + durable replay/sequence defense + behavioural
  trust-scoring + quarantine on anomaly.**
- **Residual risk:** an attacker who learns a provisioned IMEI *and* can source traffic
  from the pinned network/SIM position can still impersonate the device. Pinning and
  replay defense narrow the window; they do not close it. Keep quarantine armed and rely
  on behavioural scoring for these devices.

Cryptographic device authentication applies **only** to `PerDeviceHmac` (verified here),
`MutualTls` and `ClientCertificate` (TLS/cert verified at the edge and asserted upstream;
this component trusts that out-of-band assertion and does not re-terminate TLS). Those
modes yield `IsCryptographicallyAuthenticated = true` and the `Cryptographic` /
`StrongCryptographic` tiers.

## Scope boundaries

- The authenticator does **not** resolve ownership (that is `IDeviceRegistry`) and does
  **not** perform durable replay defense (that is the shared nonce/sequence store from
  `identity-trust-architecture.md` §5). It *surfaces* the replay requirement via
  `DeviceTrustPolicy.RequireReplayDefense`; the ingest pipeline enforces it.
- Secrets never touch the contract surface: `CredentialMaterial` is an opaque handle, and
  `ICredentialKeyResolver` dereferences it to key bytes only at the instant of verification,
  zeroing the buffer afterwards.
