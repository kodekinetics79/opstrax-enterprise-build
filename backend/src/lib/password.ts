import crypto from "crypto";

const PBKDF2_ITERATIONS = 100_000;
const PBKDF2_KEYLEN = 32;
const PBKDF2_DIGEST = "sha256";

export function hashPassword(password: string) {
  const salt = crypto.randomBytes(16);
  const hash = crypto.pbkdf2Sync(password, salt, PBKDF2_ITERATIONS, PBKDF2_KEYLEN, PBKDF2_DIGEST);
  return [
    "PBKDF2",
    PBKDF2_ITERATIONS,
    salt.toString("base64"),
    hash.toString("base64"),
  ].join("$");
}

export function verifyPassword(password: string, storedHash: string | null | undefined) {
  if (!storedHash) return false;
  const parts = storedHash.split("$");
  if (parts[0] === "PBKDF2" && parts.length === 4) {
    const [, iterationRaw, saltBase64, hashBase64] = parts;
    const expected = Buffer.from(hashBase64, "base64");
    const derived = crypto.pbkdf2Sync(
      password,
      Buffer.from(saltBase64, "base64"),
      Number(iterationRaw),
      expected.length,
      PBKDF2_DIGEST
    );
    return expected.length === derived.length && crypto.timingSafeEqual(expected, derived);
  }

  if (parts[0] === "scrypt" && parts.length === 6) {
    const [, nRaw, rRaw, pRaw, saltHex, hashHex] = parts;
    const expected = Buffer.from(hashHex, "hex");
    const derived = crypto.scryptSync(password, Buffer.from(saltHex, "hex"), expected.length, {
      N: Number(nRaw),
      r: Number(rRaw),
      p: Number(pRaw),
      maxmem: 128 * Number(nRaw) * Number(rRaw),
    });
    return expected.length === derived.length && crypto.timingSafeEqual(expected, derived);
  }

  return false;
}

export function generateToken(bytes = 32) {
  return crypto.randomBytes(bytes).toString("hex");
}
