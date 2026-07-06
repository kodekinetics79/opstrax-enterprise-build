using System.Security.Cryptography;
using System.Text;

namespace Opstrax.Api.Security;

// ─────────────────────────────────────────────────────────────────────────────
// PiiProtectionService — application-layer encryption for sensitive personal data.
//
// COMPLIANCE: PDPL (KSA) Art.19, PIPEDA Principle 7 (safeguards), SOC 2 CC6.1 all
// expect sensitive personal data encrypted. Neon encrypts disk at rest, but that
// does not protect against an app-level data exposure (leaked query result, over-
// broad export, insider read). This service encrypts the *values* so a plaintext
// PII field never leaves the process in the clear.
//
// Crypto:
//   • AES-256-GCM (authenticated encryption) with a fresh 96-bit nonce per value.
//   • Envelope pattern: the data-encryption key (DEK) comes from IDataKeyProvider,
//     which is swappable for AWS KMS / HashiCorp Vault WITHOUT touching call sites.
//     Default provider reads DATA_ENCRYPTION_KEY (base64, 32 bytes) from env.
//   • Ciphertext format (base64):  v1 | keyId(1) | nonce(12) | tag(16) | ct(n)
//     The keyId byte enables key rotation (decrypt-old / encrypt-new).
//
// Searchability:
//   • BlindIndex(value) = HMAC-SHA256(indexKey, normalized(value)) → hex. Store it
//     in a *_bidx column so encrypted email/phone can still be looked up by exact
//     match (login-by-email, dedup) WITHOUT decrypting. HMAC (not plain hash)
//     stops offline dictionary attacks on low-entropy values like phone numbers.
//
// Erasure (right to be forgotten):
//   • Crypto-shredding: overwrite the ciphertext (and its blind index) so the value
//     is mathematically unrecoverable — a stronger guarantee than anonymization,
//     and it satisfies erasure even where a row must be retained for referential
//     integrity.
// ─────────────────────────────────────────────────────────────────────────────

public interface IDataKeyProvider
{
    /// <summary>Returns the active (keyId, 32-byte key) used for NEW encryptions.</summary>
    (byte KeyId, byte[] Key) ActiveKey { get; }

    /// <summary>Resolves a key by id so ciphertext written under an older key still decrypts.</summary>
    byte[]? ResolveKey(byte keyId);

    /// <summary>HMAC key for blind indexes (separate from the encryption key).</summary>
    byte[] IndexKey { get; }

    /// <summary>True when a real key is configured (else the service runs in pass-through).</summary>
    bool IsConfigured { get; }
}

public sealed class PiiProtectionService(IDataKeyProvider keys, ILogger<PiiProtectionService> logger)
{
    private const byte Version = 0x01;

    public bool Enabled => keys.IsConfigured;

    // ── Encrypt / Decrypt ────────────────────────────────────────────────────────

    /// <summary>Encrypts a PII value → base64 envelope. Null/empty passes through
    /// unchanged. When no key is configured, returns the plaintext (dev/local) so
    /// the app still runs — Enabled is false so callers/health can flag it.</summary>
    public string? Encrypt(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return plaintext;
        if (!keys.IsConfigured) return plaintext;

        var (keyId, key) = keys.ActiveKey;
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[plainBytes.Length];
        var tag = new byte[16];

        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, plainBytes, cipher, tag);

        // v(1) | keyId(1) | nonce(12) | tag(16) | ct(n)
        var envelope = new byte[2 + 12 + 16 + cipher.Length];
        envelope[0] = Version;
        envelope[1] = keyId;
        Buffer.BlockCopy(nonce, 0, envelope, 2, 12);
        Buffer.BlockCopy(tag, 0, envelope, 14, 16);
        Buffer.BlockCopy(cipher, 0, envelope, 30, cipher.Length);
        return "enc:" + Convert.ToBase64String(envelope);
    }

    /// <summary>Decrypts a base64 envelope → plaintext. Values that are not
    /// enc:-prefixed are returned as-is (legacy plaintext, forward-compatible).</summary>
    public string? Decrypt(string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return stored;
        if (!stored.StartsWith("enc:", StringComparison.Ordinal)) return stored; // legacy plaintext

        try
        {
            var envelope = Convert.FromBase64String(stored[4..]);
            if (envelope.Length < 30 || envelope[0] != Version) return stored;

            var keyId = envelope[1];
            var key = keys.ResolveKey(keyId);
            if (key is null)
            {
                logger.LogError(new EventId(0, "pii_key_missing"),
                    "Cannot decrypt PII: key id {KeyId} is not available", keyId);
                return null;
            }

            var nonce = envelope.AsSpan(2, 12);
            var tag = envelope.AsSpan(14, 16);
            var cipher = envelope.AsSpan(30);
            var plain = new byte[cipher.Length];

            using var aes = new AesGcm(key, 16);
            aes.Decrypt(nonce, cipher, tag, plain);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex)
        {
            // Never surface the ciphertext or key material in the log (redactor also covers this).
            logger.LogError(new EventId(0, "pii_decrypt_failed"), ex, "PII decrypt failed");
            return null;
        }
    }

    // ── Blind index (searchable equality without decryption) ─────────────────────

    /// <summary>Deterministic HMAC index for exact-match lookups on an encrypted
    /// column. Normalizes (trim + lowercase) so "A@B.com" and "a@b.com " match.</summary>
    public string? BlindIndex(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        if (!keys.IsConfigured) return null; // no index when not encrypting
        var normalized = value.Trim().ToLowerInvariant();
        using var hmac = new HMACSHA256(keys.IndexKey);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    // ── Masking (for display / logs / non-privileged reads) ──────────────────────

    /// <summary>Masks a decrypted value for display where the full value is not
    /// needed (e.g. "j***@acme.com", "***-**-1234"). Never a substitute for access
    /// control — just least-exposure defence in depth.</summary>
    public static string Mask(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains('@'))
        {
            var at = value.IndexOf('@');
            return (at > 0 ? value[0] + "***" : "***") + value[at..];
        }
        return value.Length <= 4 ? "***" : new string('*', value.Length - 4) + value[^4..];
    }
}

// ── Default key provider (env-based; KMS-swappable) ──────────────────────────────
//
// Reads:
//   DATA_ENCRYPTION_KEY  — base64 of 32 bytes; the active DEK (keyId 1).
//   DATA_ENCRYPTION_KEY_PREVIOUS — optional base64 of the prior DEK (keyId 0) so
//                                  ciphertext survives a rotation window.
//   PII_INDEX_KEY        — base64 HMAC key for blind indexes (defaults to a KDF of
//                          the DEK if unset, but a dedicated key is recommended).
//
// To move to AWS KMS / Vault: implement IDataKeyProvider to fetch/unwrap the DEK
// from the KMS and register it instead — no call site changes.
public sealed class EnvDataKeyProvider : IDataKeyProvider
{
    private readonly byte[]? _active;
    private readonly byte[]? _previous;
    private readonly byte[] _indexKey;

    public EnvDataKeyProvider(IConfiguration config)
    {
        _active   = Decode(config["Pii:DataKey"]   ?? Environment.GetEnvironmentVariable("DATA_ENCRYPTION_KEY"));
        _previous = Decode(config["Pii:DataKeyPrevious"] ?? Environment.GetEnvironmentVariable("DATA_ENCRYPTION_KEY_PREVIOUS"));
        var idx   = Decode(config["Pii:IndexKey"] ?? Environment.GetEnvironmentVariable("PII_INDEX_KEY"));

        // Derive a stable index key from the DEK if a dedicated one isn't supplied.
        _indexKey = idx ?? (_active is not null
            ? SHA256.HashData(Encoding.UTF8.GetBytes("opstrax-pii-index:").Concat(_active).ToArray())
            : new byte[32]);
    }

    public bool IsConfigured => _active is { Length: 32 };

    public (byte KeyId, byte[] Key) ActiveKey =>
        _active is { Length: 32 }
            ? ((byte)1, _active)
            : throw new InvalidOperationException("DATA_ENCRYPTION_KEY is not configured");

    public byte[]? ResolveKey(byte keyId) => keyId switch
    {
        1 => _active,
        0 => _previous,
        _ => null,
    };

    public byte[] IndexKey => _indexKey;

    private static byte[]? Decode(string? b64)
    {
        if (string.IsNullOrWhiteSpace(b64)) return null;
        try { var b = Convert.FromBase64String(b64.Trim()); return b.Length == 32 ? b : null; }
        catch { return null; }
    }
}
