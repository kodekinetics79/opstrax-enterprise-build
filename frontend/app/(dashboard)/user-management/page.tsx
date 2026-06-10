import { Suspense } from 'react';
import { PermissionGate } from '@/src/components/PermissionGate';
import { UserManagementPage } from '@/src/views/UserManagementPage';
export default function Page() {
  return (
    <PermissionGate permissions={['users.manage','roles.manage','security.manage']}>
      <Suspense>
        <UserManagementPage />
      </Suspense>
    </PermissionGate>
  );
}
