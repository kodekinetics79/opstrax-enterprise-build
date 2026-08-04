import { useCallback, useRef } from "react";

/**
 * Synchronous client-side admission guard for critical mutations. React Query's
 * isPending state is rendered asynchronously, so two clicks in the same event
 * turn can otherwise start two requests before the button becomes disabled.
 * API errors remain owned by the mutation and are intentionally not rethrown.
 */
export function useSingleFlight() {
  const active = useRef(false);

  return useCallback(async (operation: () => Promise<unknown>): Promise<boolean> => {
    if (active.current) return false;
    active.current = true;
    try {
      await operation();
      return true;
    } catch {
      return false;
    } finally {
      active.current = false;
    }
  }, []);
}
