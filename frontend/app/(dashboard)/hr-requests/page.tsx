import { PermissionGate } from '@/src/components/PermissionGate';
import HRRequestCenterPage from '@/src/views/HRRequestCenterPage';
export default function Page() {
  return (
    <PermissionGate permissions={['approvals.read','approvals.write','approvals.decide']}>
      <HRRequestCenterPage />
    </PermissionGate>
  );
}
