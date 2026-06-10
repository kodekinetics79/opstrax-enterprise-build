import { PermissionGate } from '@/src/components/PermissionGate';
import { PayrollPage } from '@/src/views/PayrollPage';
export default function Page() {
  return (
    <PermissionGate permissions={['payroll.read']}>
      <PayrollPage />
    </PermissionGate>
  );
}
