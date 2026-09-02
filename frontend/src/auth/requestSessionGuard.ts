import { AxiosHeaders, type AxiosRequestConfig, type InternalAxiosRequestConfig, type RawAxiosHeaders } from "axios";
import type { UserSession } from "@/types";

type ExpectedSession = { token: string; companyId: string; userId: string };
const pending = new Map<string, ExpectedSession>();
const INTERNAL_SESSION_GUARD_HEADER = "X-OpsTrax-Expected-Session-Guard";

const sessionParts = (session: UserSession): ExpectedSession => ({
  token: String(session.token ?? ""),
  companyId: String(session.company?.id ?? session.company?.companyId ?? session.user?.companyId ?? session.user?.company_id ?? ""),
  userId: String(session.user?.id ?? ""),
});

export class SessionChangedBeforeRequestError extends Error {
  constructor() { super("Your session changed before the document request was sent. Reopen the document in the current session."); }
}

/** Register an exact in-memory auth session without copying its bearer token into Axios config. */
export function createRequestSessionGuard(session: UserSession): string {
  const expected = sessionParts(session);
  if (!expected.token || !expected.companyId || !expected.userId) throw new SessionChangedBeforeRequestError();
  const id = globalThis.crypto.randomUUID();
  pending.set(id, expected);
  return id;
}

/** Consume once in the request interceptor, before any Authorization header is attached. */
export function consumeRequestSessionGuard(id: string, rawSession: string | null): void {
  const expected = pending.get(id);
  pending.delete(id);
  if (!expected || !rawSession) throw new SessionChangedBeforeRequestError();
  try {
    const parsed = JSON.parse(rawSession) as { session?: UserSession } & UserSession;
    const actual = sessionParts(parsed.session ?? parsed);
    if (actual.token !== expected.token || actual.companyId !== expected.companyId || actual.userId !== expected.userId) {
      throw new SessionChangedBeforeRequestError();
    }
  } catch (failure) {
    if (failure instanceof SessionChangedBeforeRequestError) throw failure;
    throw new SessionChangedBeforeRequestError();
  }
}

export function sessionBoundRequest(session: UserSession, config: AxiosRequestConfig = {}): AxiosRequestConfig {
  const headers = AxiosHeaders.from(config.headers as AxiosHeaders | RawAxiosHeaders | undefined);
  headers.set(INTERNAL_SESSION_GUARD_HEADER, createRequestSessionGuard(session));
  return { ...config, headers };
}

/** Remove the local-only marker and fail before dispatch if browser authority changed. */
export function enforceRequestSessionGuard(config: InternalAxiosRequestConfig, rawSession: string | null): void {
  const id = config.headers.get(INTERNAL_SESSION_GUARD_HEADER);
  config.headers.delete(INTERNAL_SESSION_GUARD_HEADER);
  if (id) consumeRequestSessionGuard(String(id), rawSession);
}
