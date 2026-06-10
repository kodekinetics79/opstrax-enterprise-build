import { Suspense } from 'react';
import { PermissionGate } from '@/src/components/PermissionGate';
import { ReportsPage } from '@/src/views/ReportsPage';
export default function Page() {
  return (
    <PermissionGate permissions={['reports.read','reports.schedule']}>
      <Suspense>
        <ReportsPage />
      </Suspense>
    </PermissionGate>
  );
}
