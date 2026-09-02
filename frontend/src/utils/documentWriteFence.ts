import type { UserSession } from "@/types";
import { SessionChangedBeforeRequestError } from "@/auth/requestSessionGuard";

type Fence = { pending: boolean; requiresReload: boolean; target: string; owner?: symbol };
const idle: Fence = Object.freeze({ pending: false, requiresReload: false, target: "" });
const fences = new WeakMap<UserSession, Fence>();
const listeners = new Set<() => void>();
const publish = (session: UserSession, value: Fence) => { fences.set(session, value); listeners.forEach(listener => listener()); };

// Same browser-document/session protection survives a route remount. It is not
// cross-tab idempotency, nor proof that an uncertain upload did not commit.
export const documentWriteFence = {
  subscribe(listener: () => void) { listeners.add(listener); return () => { listeners.delete(listener); }; },
  snapshot(session: UserSession | null) { return session ? fences.get(session) ?? idle : idle; },
  begin(session: UserSession, target: string) {
    const prior = fences.get(session) ?? idle;
    if (prior.pending || prior.requiresReload) throw new Error("A document change is pending or requires reconciliation. Reload the current document before another write.");
    const owner = Symbol("document-write");
    publish(session, { pending: true, requiresReload: false, target, owner });
    return owner;
  },
  finish(session: UserSession, owner: symbol, requiresReload: boolean) {
    const prior = fences.get(session);
    if (prior?.owner !== owner) return;
    publish(session, requiresReload ? { pending: false, requiresReload: true, target: prior.target } : idle);
  },
  reconcile(session: UserSession, target: string) {
    const prior = fences.get(session) ?? idle;
    if (prior.pending || (prior.requiresReload && prior.target !== target)) throw new Error("Reconcile the original document change before editing another record.");
    publish(session, idle);
  },
};

export function documentFailureNeedsReload(error: unknown): boolean {
  if (error instanceof SessionChangedBeforeRequestError) return false; // transport proved no request was sent
  const status = (error as { response?: { status?: number } })?.response?.status;
  // Known validation/authorization failures have no document write. A conflict,
  // missing response or server failure needs explicit readback, never retry.
  return ![400, 401, 403, 404, 413, 415, 422, 428].includes(status ?? 0);
}
