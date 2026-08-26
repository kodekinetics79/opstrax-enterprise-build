export type DirectPermissionCheck = (permission: string) => boolean;

export type JobActionAccess = {
  create: boolean;
  import: boolean;
  export: boolean;
  queueProof: boolean;
};

// Keep these action gates aligned with EndpointMappings' direct-permission
// checks. Semantic permission aliases are suitable for navigation/read views,
// but must never advertise a write/export action that the API will reject.
export function resolveJobActionAccess(hasDirectPermission: DirectPermissionCheck): JobActionAccess {
  const canCreate = hasDirectPermission("dispatch:manage")
    || hasDirectPermission("job:create")
    || hasDirectPermission("shipments:create")
    || hasDirectPermission("dispatch:create");

  return {
    create: canCreate,
    import: canCreate,
    export: hasDirectPermission("shipments:export"),
    queueProof: hasDirectPermission("dispatch:manage")
      || hasDirectPermission("dispatch:update")
      || hasDirectPermission("shipments:update"),
  };
}
