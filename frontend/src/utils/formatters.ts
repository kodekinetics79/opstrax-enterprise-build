export function formatMinutesAsClock(minutes: number): string {
  const h = Math.floor(Math.abs(minutes) / 60);
  const m = Math.abs(minutes) % 60;
  return `${h}h ${m}m`;
}

export function formatDuration(minutes: number): string {
  if (minutes < 60) return `${minutes}m`;
  const h = Math.floor(minutes / 60);
  const m = minutes % 60;
  return m > 0 ? `${h}h ${m}m` : `${h}h`;
}

export function formatPercent(value: number, decimals = 1): string {
  return `${value.toFixed(decimals)}%`;
}

export function formatCurrency(value: number, currency = "USD"): string {
  return new Intl.NumberFormat("en-US", { style: "currency", currency, maximumFractionDigits: 2 }).format(value);
}

export function calculateCustomerHealth(customer: { healthScore?: number; monthlyShipments?: number; revenueMtd?: number }): number {
  const health = Number(customer.healthScore ?? 70);
  const volumeLift = Math.min(Number(customer.monthlyShipments ?? 0) / 25, 12);
  const revenueLift = Math.min(Number(customer.revenueMtd ?? 0) / 200000, 10);
  return Math.max(0, Math.min(100, Math.round(health * 0.75 + volumeLift + revenueLift)));
}

export function calculateShipmentDelay(shipment: { currentStatus?: string; delayRisk?: string; eta?: string }): { risk: string; minutes: number } {
  const status = String(shipment.currentStatus ?? "");
  const delayRisk = String(shipment.delayRisk ?? "");
  if (/delayed/i.test(status) || /high/i.test(delayRisk)) return { risk: "High", minutes: 70 };
  if (/medium|watch/i.test(delayRisk)) return { risk: "Medium", minutes: 25 };
  if (/delivered/i.test(status)) return { risk: "None", minutes: 0 };
  return { risk: "Low", minutes: 8 };
}

export function calculateProfitability(revenue: number, cost: number): { margin: number; marginText: string; status: string } {
  const margin = revenue > 0 ? ((revenue - cost) / revenue) * 100 : 0;
  const status = margin < 12 ? "Margin Risk" : margin < 20 ? "Watch" : "Healthy";
  return { margin, marginText: `${margin.toFixed(1)}%`, status };
}

export function formatDistance(value: number, unit: "Miles" | "Kilometers" = "Miles"): string {
  return `${value.toLocaleString()} ${unit === "Miles" ? "mi" : "km"}`;
}

export function formatFuel(value: number, unit: "Gallons" | "Liters" = "Gallons"): string {
  return `${value.toFixed(2)} ${unit === "Gallons" ? "gal" : "L"}`;
}

export function formatDateTime(iso: string): string {
  if (!iso) return "—";
  try {
    return new Date(iso).toLocaleString("en-US", { month: "short", day: "numeric", year: "numeric", hour: "2-digit", minute: "2-digit" });
  } catch {
    return iso;
  }
}

export function formatDate(iso: string): string {
  if (!iso) return "—";
  try {
    return new Date(iso).toLocaleDateString("en-US", { month: "short", day: "numeric", year: "numeric" });
  } catch {
    return iso;
  }
}

export function daysUntil(iso: string): number {
  if (!iso) return 9999;
  return Math.ceil((new Date(iso).getTime() - Date.now()) / 86400000);
}

export function expiryBadgeClass(days: number): string {
  if (days < 0) return "badge-red";
  if (days < 15) return "badge-red";
  if (days < 30) return "badge-amber";
  return "badge-green";
}
