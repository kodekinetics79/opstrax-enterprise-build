import { useCallback, useEffect, useRef, useState } from "react";
import { API_BASE_URL, apiClient, unwrap } from "@/services/apiClient";
import type { AnyRecord } from "@/types";

export type VehiclePosition = {
  vehicleCode: string;
  vehicleId: number | null;
  driverId: number | null;
  lat: number;
  lng: number;
  speedMph: number;
  heading: number;
  eventType: string;
  engineStatus: string | null;
  fuelLevel: number | null;
  odometerMiles: number | null;
  eventTime: string;
  make: string | null;
  model: string | null;
  vehicleStatus: string | null;
  driverName: string | null;
  secondsSincePing: number | null;
  isStale: boolean;
  address: string | null;
};



function toPosition(r: AnyRecord): VehiclePosition {
  const ssp = r["secondsSincePing"] != null ? Number(r["secondsSincePing"]) : null;
  return {
    vehicleCode:      String(r["vehicleCode"] ?? r["vehicle_code"] ?? ""),
    vehicleId:        r["vehicleId"]   != null ? Number(r["vehicleId"])   : null,
    driverId:         r["driverId"]    != null ? Number(r["driverId"])    : null,
    lat:              Number(r["lat"] ?? 0),
    lng:              Number(r["lng"] ?? 0),
    speedMph:         Number(r["speedMph"]  ?? r["speed_mph"] ?? 0),
    heading:          Number(r["heading"]   ?? 0),
    eventType:        String(r["eventType"] ?? r["event_type"] ?? "ping"),
    engineStatus:     r["engineStatus"] != null ? String(r["engineStatus"]) : null,
    fuelLevel:        r["fuelLevel"]    != null ? Number(r["fuelLevel"])    : null,
    odometerMiles:    r["odometerMiles"] != null ? Number(r["odometerMiles"]) : null,
    eventTime:        String(r["eventTime"] ?? r["event_time"] ?? ""),
    make:             r["make"]          != null ? String(r["make"])          : null,
    model:            r["model"]         != null ? String(r["model"])         : null,
    vehicleStatus:    r["vehicleStatus"] != null ? String(r["vehicleStatus"]) : null,
    driverName:       r["driverName"]    != null ? String(r["driverName"])    : null,
    secondsSincePing: ssp,
    isStale:          ssp !== null ? ssp > 900 : false,
    address:          r["address"] != null ? String(r["address"]) : null,
  };
}

export type UseLiveTelemetryReturn = {
  positions: VehiclePosition[];
  connected: boolean;
  lastUpdated: Date | null;
  error: string | null;
  refresh: () => void;
};

async function fetchStreamTicket(): Promise<string | null> {
  try {
    const result = await unwrap<{ ticket: string; ttlSeconds: number }>(
      apiClient.post("/api/telemetry/stream-ticket")
    );
    return result.ticket;
  } catch {
    return null;
  }
}

export function useLiveTelemetry(): UseLiveTelemetryReturn {
  const [positions,   setPositions]   = useState<VehiclePosition[]>([]);
  const [connected,   setConnected]   = useState(false);
  const [lastUpdated, setLastUpdated] = useState<Date | null>(null);
  const [error,       setError]       = useState<string | null>(null);
  const sourceRef = useRef<EventSource | null>(null);

  const fetchSnapshot = useCallback(async () => {
    try {
      const rows = await unwrap<AnyRecord[]>(apiClient.get("/api/telemetry/positions"));
      if (rows.length > 0) {
        setPositions(rows.map(toPosition));
        setLastUpdated(new Date());
      }
    } catch {
      // ignore — SSE will keep data fresh
    }
  }, []);

  useEffect(() => {
    void fetchSnapshot();

    let cancelled = false;
    let renewTimer: ReturnType<typeof setTimeout> | null = null;

    function clearRenewTimer() {
      if (renewTimer !== null) {
        clearTimeout(renewTimer);
        renewTimer = null;
      }
    }

    // A dropped connection or a failed ticket fetch must always lead back here —
    // otherwise one transient hiccup (cold backend, brief 401) permanently kills
    // the live map until the component remounts.
    function scheduleRetry(delayMs: number) {
      if (cancelled) return;
      clearRenewTimer();
      renewTimer = setTimeout(() => { void openStream(); }, delayMs);
    }

    async function openStream() {
      if (cancelled) return;

      clearRenewTimer();
      if (sourceRef.current) {
        sourceRef.current.close();
        sourceRef.current = null;
      }

      // Fetch short-lived stream ticket (90s TTL, HMAC-signed, scoped to companyId).
      // Only the SST is sent — long-lived session tokens are never placed in query strings.
      const sst = await fetchStreamTicket();

      if (cancelled) return;

      if (!sst) {
        setError("Failed to obtain stream ticket — retrying");
        scheduleRetry(5_000);
        return;
      }

      const params = new URLSearchParams();
      params.set("sst", sst);

      const url = `${API_BASE_URL}/api/telemetry/stream?${params.toString()}`;
      const source = new EventSource(url);
      sourceRef.current = source;

      source.addEventListener("connected", () => {
        if (!cancelled) setConnected(true);
      });

      source.addEventListener("positions", (e: MessageEvent) => {
        if (cancelled) return;
        try {
          const rows: AnyRecord[] = JSON.parse(e.data);
          setPositions(rows.map(toPosition));
          setLastUpdated(new Date());
          setError(null);
          setConnected(true);
        } catch {
          // malformed frame — ignore
        }
      });

      source.onerror = () => {
        if (cancelled) return;
        setConnected(false);
        setError("Live stream disconnected — retrying");
        // Close rather than let EventSource auto-reconnect: it would keep hitting
        // the same (soon-expiring) ticket URL. Re-ticket and reopen instead.
        if (sourceRef.current) {
          sourceRef.current.close();
          sourceRef.current = null;
        }
        scheduleRetry(5_000);
      };

      // Renew 15s before the 90s ticket expires to avoid mid-stream expiry
      renewTimer = setTimeout(() => { void openStream(); }, 75_000);
    }

    void openStream();

    return () => {
      cancelled = true;
      if (sourceRef.current) {
        sourceRef.current.close();
        sourceRef.current = null;
      }
      clearRenewTimer();
      setConnected(false);
    };
  }, [fetchSnapshot]);

  return { positions, connected, lastUpdated, error, refresh: fetchSnapshot };
}
