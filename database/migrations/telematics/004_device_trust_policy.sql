-- ============================================================================
-- 004_device_trust_policy.sql
--
-- Persistence for the per-device TRUST POLICY that backs
--   Opstrax.Telematics.Contracts.Identity.DeviceTrustPolicy
--   Opstrax.Telematics.Gateway.Security.Auth.DefaultDeviceAuthenticator
--
-- This migration is ADDITIVE and idempotent (IF NOT EXISTS throughout). It does
-- NOT alter any existing table or ingest behaviour; it only gives the registry a
-- home for the explicit provisioning facts the authenticator enforces.
--
-- HONESTY / RESIDUAL RISK (read this):
--   auth_mode = 'ImeiAllowlistOnly' is the honest baseline for raw GT06/Concox
--   trackers. There is NO cryptographic device authentication in that mode — the
--   IMEI in the login frame is a spoofable bearer identifier. The columns below
--   (source-IP pin, SIM pin, replay-defense flag) are defence-in-depth that
--   NARROW the spoofing window; they do not close it. Treat such devices as the
--   low/spoofable trust tier and keep quarantine armed. Cryptographic assurance
--   applies only to 'PerDeviceHmac', 'MutualTls' and 'ClientCertificate'.
-- ============================================================================

-- Per-device trust policy. One row per provisioned device (the device row itself
-- lives in the existing device registry / eld_devices; this references it by the
-- fabric-internal device id string used across the telematics fabric).
CREATE TABLE IF NOT EXISTS telematics_device_trust_policy (
    device_id           VARCHAR(120) PRIMARY KEY,     -- fabric-internal device id (never the IMEI)

    -- ImeiAllowlistOnly | PerDeviceHmac | MutualTls | ClientCertificate
    auth_mode           VARCHAR(40)  NOT NULL DEFAULT 'ImeiAllowlistOnly',

    -- Opaque credential handle (vault:// URI, KMS key id, or cert fingerprint).
    -- NEVER the raw secret. NULL for ImeiAllowlistOnly (there is no credential).
    credential_kind     VARCHAR(40)  NOT NULL DEFAULT 'None',   -- None|PreSharedHmacKey|ClientCertFingerprint|PublicKey
    credential_handle   VARCHAR(400) NULL,

    -- Optional source-IP allowlist, CIDR notation (e.g. '203.0.113.0/24','2001:db8::/32').
    -- A network-position signal only; carrier NAT and proxies weaken it.
    pinned_source_cidrs TEXT[]       NULL,

    -- Optional SIM pinning. A changed value is treated as a SIM-swap anomaly (quarantine).
    pinned_sim_iccid    VARCHAR(32)  NULL,
    pinned_imsi         VARCHAR(32)  NULL,

    -- Whether durable, shared replay/sequence defense MUST be enforced at ingest.
    -- Defaults ON: for a raw tracker with no per-message crypto this is one of the
    -- few real defences it has.
    require_replay_defense BOOLEAN   NOT NULL DEFAULT TRUE,

    created_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_at          TIMESTAMPTZ  NULL,

    CONSTRAINT ck_tdtp_auth_mode CHECK (
        auth_mode IN ('ImeiAllowlistOnly','PerDeviceHmac','MutualTls','ClientCertificate')
    ),
    CONSTRAINT ck_tdtp_credential_kind CHECK (
        credential_kind IN ('None','PreSharedHmacKey','ClientCertFingerprint','PublicKey')
    ),
    -- Fail-closed invariant: any cryptographic mode MUST carry a credential handle.
    -- ImeiAllowlistOnly MUST NOT pretend to have credential material.
    CONSTRAINT ck_tdtp_credential_presence CHECK (
        (auth_mode = 'ImeiAllowlistOnly' AND credential_kind = 'None')
        OR (auth_mode <> 'ImeiAllowlistOnly'
            AND credential_kind <> 'None'
            AND credential_handle IS NOT NULL
            AND length(btrim(credential_handle)) > 0)
    )
);

COMMENT ON TABLE  telematics_device_trust_policy IS
    'Per-device trust policy backing DefaultDeviceAuthenticator. ImeiAllowlistOnly is spoofable (no crypto); other modes are cryptographically authenticated. See docs/telematics/security/per-device-authenticator.md.';
COMMENT ON COLUMN telematics_device_trust_policy.credential_handle IS
    'OPAQUE handle only (vault URI / KMS id / cert fingerprint). NEVER the raw secret.';
COMMENT ON COLUMN telematics_device_trust_policy.pinned_source_cidrs IS
    'Optional source-IP CIDR allowlist. Network-position signal only, not identity proof.';

-- Fast lookup of all devices provisioned for a given auth mode (fleet migration reporting).
CREATE INDEX IF NOT EXISTS idx_tdtp_auth_mode
    ON telematics_device_trust_policy (auth_mode);
