# Outbox Encryption Key Management (public TCP device edge)

**Key:** `Gateway:StoreForwardEncryptionKey` — base64-encoded, exactly 32 bytes (AES-256).
**Consumer:** `FileForwardOutbox` in `Opstrax.Telematics.Gateway` (the `Egress=Https` public edge),
and `PostgresStoreAndForwardBuffer` (the `Egress=Postgres` topology), which already required it.

## What it protects, and from whom

When OpsTrax is unreachable, the edge parks every accepted fix in an on-disk outbox
(`Gateway:Edge:Outbox:Path`, `/var/lib/opstrax-gateway/outbox` on a systemd install). A parked
fix is the identified, timestamped location of a real vehicle — PII — sitting on the disk of the
single most exposed machine in the fleet: an internet-facing VPS. The threat is offline: a
stolen or resold disk, a snapshot/backup of the VM, a second local account, or a file-read bug.
Every entry is therefore sealed with AES-256-GCM before it touches the disk, and entry files are
created `0600` in a `0700` directory.

The gateway **refuses to boot** in Production/Staging `Https` mode without this key. Outside
protected environments a missing key falls back to an ephemeral random key: the disk format
stays encrypted, but entries do not survive a restart readably — dev convenience only.

## On-disk entry format

```
fileName  := <utcTicks:D19>-<sequence:D9>.enc       (lexicographic order == enqueue order)
entry     := formatVersion(1) || keyVersion(1) || nonce(12) || tag(16) || ciphertext
formatVersion = 0x02      (0x01 was the plaintext-JSON format, retired before any deployment)
AAD       := formatVersion || keyVersion || UTF8(fileName without extension)
plaintext := UTF8(payload JSON exactly as normalized — the bytes the forwarding HMAC signs)
```

Design notes:

- **The enqueue time lives in the file name**, not inside the entry, so retention
  (`Outbox:MaxAge`) works without decrypting anything and no cleartext metadata exists in the
  file at all.
- **The file name is bound into the GCM associated data**, so renaming an entry (to forge its
  age or reorder the queue) fails authentication and lands on the corrupt-drop path.
- **The key-version byte** (`Gateway:Edge:Outbox:EncryptionKeyVersion`, 1–255, default 1)
  records which key sealed each entry, which is what makes rotation observable and future
  multi-key reads possible.
- **Anything undecryptable — wrong key, tampering, truncation — is discarded as corrupt**,
  counted in the outbox-discard metric and logged. Fail closed, bounded loss, never a stuck queue.

## Provisioning — environment file, never argv

The key travels only through the process environment, exactly like the forwarding secret:

- **systemd install (`telematics/deploy/install.sh`)**: generated automatically on first
  install (`head -c 32 /dev/urandom | base64`) and written to `/etc/opstrax/gateway.env`
  (mode `0640 root:opstrax`, written under `umask 077`), consumed via the unit's
  `EnvironmentFile=`. Re-runs of the installer **preserve** the existing key so previously
  parked fixes stay readable.
- **Docker (`telematics/deploy/docker-compose.yml`)**: supply `OPSTRAX_OUTBOX_KEY` in the
  environment or an `.env` file beside the compose file (`openssl rand -base64 32`). Compose
  refuses to start without it.

Never pass the key (or any secret) on a command line: argv is world-readable via
`/proc/<pid>/cmdline` for the life of the process. The gateway's composition root is built
**without** the command-line configuration source, so a key or secret supplied via argv is not
merely discouraged — it is not read at all.

The key never appears in `EdgeOptions`/`OutboxOptions` (options objects get logged and dumped),
never in a committed `appsettings*.json`, and must never be printed into logs, shell history, or
tickets. Refer to it by its configuration name.

## Rotation

The outbox is a short-lived queue (minutes when healthy, bounded by `MaxAge`, default 7 days),
which makes rotation cheap:

1. Wait for the queue to drain (log line `Outbox ... holds N fix(es)` absent, or the outbox
   directory empty), or accept that undrained entries sealed by the old key will be discarded as
   corrupt after the switch — bounded, counted loss.
2. Generate a new 32-byte key and replace `Gateway__StoreForwardEncryptionKey` in
   `/etc/opstrax/gateway.env` (or the container environment).
3. Bump `Gateway:Edge:Outbox:EncryptionKeyVersion` by one so newly sealed entries are
   attributable to the new key.
4. Restart the service (`systemctl restart opstrax-telematics-gateway`).

There is deliberately **no previous-key fallback** in the reader today: a second accepted key
doubles the offline-attack surface for a queue whose entries are worth minutes of retention. The
key-version byte in every entry is the seam that allows a multi-key reader later without a format
change.

## Loss semantics

Losing the key is losing the currently parked (undelivered) fixes and nothing else: each entry is
discarded as corrupt on the next read, counted in the discard metric, and the queue keeps
operating under the configured key. Delivered fixes live in OpsTrax; the outbox holds no
long-term data by design (`MaxEntries` 50k / `MaxAge` 7d ceilings already bounded it).

## Verification

Pinned by tests (run under `telematics/Opstrax.Telematics.sln`):

- `EdgeOutboxTests.PersistedBytes_RevealNoPlaintextCoordinateOrIdentifier` — raw entry bytes
  reveal no IMEI or coordinate in UTF-8/UTF-16LE and do not parse as JSON, while the API
  round-trips byte-identically.
- `EdgeOutboxTests.EntryFiles_AreOwnerReadWriteOnly` — `0600` files, `0700` directory (POSIX).
- `EdgeOutboxTests.WrongKey_CannotDecrypt_AndIsDiscardedAsCorrupt` — wrong key ⇒ corrupt-drop.
- `EdgeProtectedCompositionTests.ProtectedHttpsEdge_WithoutOutboxEncryptionKey_RefusesToBoot` —
  the real executable refuses to start in Production `Https` mode without the key.
