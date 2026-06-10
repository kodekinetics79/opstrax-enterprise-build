import { PermissionGate } from '@/src/components/PermissionGate';
import CompliancePage from '@/src/views/CompliancePage';
export default function Page() {
  return (
    <PermissionGate permissions={['compliance.read','compliance.write']}>
      <CompliancePage />
    </PermissionGate>
  );
}
