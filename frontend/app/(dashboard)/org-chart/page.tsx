import { Suspense } from 'react';
import { PermissionGate } from '@/src/components/PermissionGate';
import { OrgChartPage } from '@/src/views/OrgChartPage';

export default function Page() {
  return (
    <PermissionGate permissions={['employees.read']}>
      <Suspense>
        <OrgChartPage />
      </Suspense>
    </PermissionGate>
  );
}
