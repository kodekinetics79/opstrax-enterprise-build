import { PermissionGate } from '@/src/components/PermissionGate';
import { EmployeeSelfServicePage } from '@/src/views/EmployeeSelfServicePage';
export default function Page() {
  return (
    <PermissionGate permissions={['ess.read']}>
      <EmployeeSelfServicePage />
    </PermissionGate>
  );
}
