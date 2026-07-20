import { createContext, useContext, type ReactNode } from "react";
import { useQuery } from "@tanstack/react-query";
import { apiClient, unwrap } from "@/services/apiClient";

// ─────────────────────────────────────────────────────────────────────────────
// Feature flags — client side.
//
// GET /api/feature-flags/evaluate returns flags ALREADY RESOLVED for the current
// user (kill switch + deterministic rollout bucketing applied server-side). The
// client never re-implements the rollout maths, so the UI and the API can never
// disagree about whether a user is in a rollout.
//
// The server gate is the source of truth: hiding UI is a courtesy, not security.
// ─────────────────────────────────────────────────────────────────────────────

type FlagMap = Record<string, boolean>;

const FeatureFlagsContext = createContext<{ flags: FlagMap; loaded: boolean }>({ flags: {}, loaded: false });

async function fetchFlags(): Promise<FlagMap> {
  return unwrap<FlagMap>(apiClient.get("/api/feature-flags/evaluate"));
}

export function FeatureFlagsProvider({ children }: { children: ReactNode }) {
  const q = useQuery({
    queryKey: ["feature-flags", "evaluate"],
    queryFn: fetchFlags,
    staleTime: 60_000,
    refetchInterval: 60_000, // a kill switch must take effect without a reload
  });
  return (
    <FeatureFlagsContext.Provider value={{ flags: q.data ?? {}, loaded: q.isSuccess }}>
      {children}
    </FeatureFlagsContext.Provider>
  );
}

/**
 * Resolve one flag.
 *
 * `fallback` is what to use until the flags have loaded (or if the flag is absent).
 * For a kill switch over EXISTING behaviour pass `true`, so the feature doesn't
 * flicker out of existence on every page load. For a genuinely NEW feature pass
 * `false`, so it stays hidden until explicitly turned on.
 */
export function useFlag(key: string, fallback = false): boolean {
  const { flags, loaded } = useContext(FeatureFlagsContext);
  if (!loaded) return fallback;
  return key in flags ? flags[key] : fallback;
}

export function useFlags(): FlagMap {
  return useContext(FeatureFlagsContext).flags;
}
