import * as SecureStore from "expo-secure-store";
import { APP_PRODUCT, STAGE_LABEL } from "@/config";

function safeSegment(value: number | string | null | undefined) {
  return String(value ?? "unknown")
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9._-]+/g, "-")
    .slice(0, 80) || "unknown";
}

export function secureDraftKey(
  name: string,
  companyId: number | string | null | undefined,
  userId: number | string | null | undefined,
  workId?: number | string | null,
) {
  return [
    "opstrax",
    safeSegment(APP_PRODUCT),
    safeSegment(STAGE_LABEL),
    "draft",
    safeSegment(companyId),
    safeSegment(userId),
    safeSegment(name),
    workId == null ? null : safeSegment(workId),
    "v1",
  ].filter(Boolean).join(".");
}

export async function readSecureDraft<T>(key: string): Promise<T | null> {
  try {
    const raw = await SecureStore.getItemAsync(key);
    if (!raw) return null;
    return JSON.parse(raw) as T;
  } catch {
    return null;
  }
}

export async function writeSecureDraft<T>(key: string, value: T): Promise<void> {
  const serialized = JSON.stringify(value);
  // Draft storage is intentionally limited to small form metadata/text. Photos and binary
  // evidence must live in durable file storage rather than Keychain/Keystore values.
  if (serialized.length > 12_000) {
    throw new Error("This draft is too large for secure text storage.");
  }
  await SecureStore.setItemAsync(key, serialized, {
    keychainAccessible: SecureStore.WHEN_UNLOCKED_THIS_DEVICE_ONLY,
  });
}

export async function clearSecureDraft(key: string): Promise<void> {
  await SecureStore.deleteItemAsync(key);
}
