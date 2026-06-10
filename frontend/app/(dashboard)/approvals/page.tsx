import { PermissionGate } from '@/src/components/PermissionGate';
import { ApprovalsPage } from '@/src/views/ApprovalsPage';
export default function Page() {
  return (
    <PermissionGate permissions={['approvals.read','approvals.decide']}>
      <ApprovalsPage />
    </PermissionGate>
  );
}
