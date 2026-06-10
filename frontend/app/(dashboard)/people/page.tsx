import { Suspense } from 'react';
import { PermissionGate } from '@/src/components/PermissionGate';
import { EmployeesPage } from '@/src/views/EmployeesPage';
export default function Page() {
  return (
    <PermissionGate permissions={['employees.read']}>
      <Suspense>
        <EmployeesPage />
      </Suspense>
    </PermissionGate>
  );
}
