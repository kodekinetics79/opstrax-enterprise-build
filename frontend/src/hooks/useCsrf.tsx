import { useContext, useCallback, useRef } from "react";
import { useAuth } from "@/hooks/useAuth";
import { setGlobalCsrfToken } from "@/auth/csrfTokenStore";

export type CsrfContextValue = {
  csrfToken: string | null;
  setCsrfToken: (token: string) => void;
};

export function useCsrfToken() {
  const { session } = useAuth();
  const tokenRef = useRef<string | null>(null);

  const getToken = useCallback(() => {
    if (session?.csrfToken) {
      return session.csrfToken;
    }
    if (tokenRef.current) {
      return tokenRef.current;
    }
    return null;
  }, [session?.csrfToken]);

  const setToken = useCallback((token: string) => {
    tokenRef.current = token;
    setGlobalCsrfToken(token);
  }, []);

  return { getToken: getToken(), setToken };
}
