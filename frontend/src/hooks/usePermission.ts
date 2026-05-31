import { useAuth } from "./useAuth";

/**
 * Returns true if the current session has the given permission.
 * A session with ["*"] passes every check unconditionally.
 */
export function usePermission(permission: string): boolean {
  const { session } = useAuth();
  if (!session) return false;
  const perms = session.permissions;
  if (perms.includes("*")) return true;
  return perms.includes(permission);
}

/**
 * Returns true if the current session has ANY of the given permissions.
 */
export function useAnyPermission(permissions: string[]): boolean {
  const { session } = useAuth();
  if (!session) return false;
  const perms = session.permissions;
  if (perms.includes("*")) return true;
  return permissions.some((p) => perms.includes(p));
}

/**
 * Returns a function that checks whether the current session holds a permission.
 * Useful for imperative checks inside event handlers or render logic.
 */
export function useHasPermission(): (permission: string) => boolean {
  const { session } = useAuth();
  return (permission: string) => {
    if (!session) return false;
    const perms = session.permissions;
    if (perms.includes("*")) return true;
    return perms.includes(permission);
  };
}
