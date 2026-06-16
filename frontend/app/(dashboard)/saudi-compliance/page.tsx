import { PermissionGate } from '@/src/components/PermissionGate';
import { SaudiComplianceDashboard } from '@/src/views/SaudiComplianceDashboard';

export default function Page() {
  return (
    <PermissionGate permissions={['compliance.read', 'qiwa.read']}>
      <SaudiComplianceDashboard />
    </PermissionGate>
  );
}
