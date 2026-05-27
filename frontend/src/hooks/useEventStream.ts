import { useEffect, useState } from "react";
import { NODE_EVENTS_URL } from "@/services/apiClient";
import type { AnyRecord } from "@/types";

export function useEventStream() {
  const [events, setEvents] = useState<AnyRecord[]>([]);

  useEffect(() => {
    const source = new EventSource(`${NODE_EVENTS_URL}/events/stream`);
    source.onmessage = (message) => {
      try {
        const event = JSON.parse(message.data);
        setEvents((current) => [event, ...current].slice(0, 20));
      } catch {
        // Ignore malformed demo stream messages.
      }
    };
    return () => source.close();
  }, []);

  return events;
}
