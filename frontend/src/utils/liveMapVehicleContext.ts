type MapEntity = Record<string, unknown>;

/** Vehicle endpoints use positive signed 64-bit IDs; labels are never identities. */
export function mapVehicleId(value: string | null): string | null {
  if (!value || !/^[1-9]\d{0,18}$/.test(value) || BigInt(value) > 9223372036854775807n) return null;
  return value;
}

export function contextualMapEntity<T extends MapEntity>(entities: T[], vehicleId: string): T | null {
  return entities.find(entity => String(entity.vehicleId ?? entity.vehicle_id ?? entity.id ?? "") === vehicleId) ?? null;
}

export function mapContextHasPosition(entity: MapEntity): boolean {
  const rawLat = entity.lat ?? entity.latitude;
  const rawLng = entity.lng ?? entity.longitude;
  if (rawLat == null || rawLng == null || rawLat === "" || rawLng === "") return false;
  const lat = Number(rawLat);
  const lng = Number(rawLng);
  return Number.isFinite(lat) && Number.isFinite(lng) && Math.abs(lat) <= 90 && Math.abs(lng) <= 180 && !(lat === 0 && lng === 0);
}

/** A positionless summary cannot settle a link before the initial GPS snapshot finishes. */
export function mapContextReady(entity: MapEntity | null, snapshotSettled: boolean): boolean {
  return Boolean(entity && mapContextHasPosition(entity)) || snapshotSettled;
}

/** Preserve a requested unit beyond the roster rendering cap without fetching outside scope. */
export function includeContextualMapEntity<T extends MapEntity>(visible: T[], target: T | null): T[] {
  return target && !visible.includes(target) ? [...visible, target] : visible;
}
