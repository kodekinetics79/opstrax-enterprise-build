import type { JsonRecord } from "@/types";

export function asRecords(value: unknown): JsonRecord[] {
  if (Array.isArray(value)) return value as JsonRecord[];
  if (value && typeof value === "object") {
    const record = value as Record<string, unknown>;
    for (const key of ["items", "rows", "data", "results", "tasks", "latest"]) {
      if (Array.isArray(record[key])) return record[key] as JsonRecord[];
    }
  }
  return [];
}

export function textOf(value: unknown, fallback = "Not available") {
  return value === null || value === undefined || value === "" ? fallback : String(value);
}

export function numberOf(value: unknown) {
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : null;
}

export function titleCase(value: unknown) {
  return textOf(value)
    .replace(/[_-]+/g, " ")
    .replace(/\b\w/g, (letter) => letter.toUpperCase());
}
