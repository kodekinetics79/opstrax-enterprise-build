import { PermissionGate } from '@/src/components/PermissionGate';
import { LeavePage } from '@/src/views/LeavePage';
export default function Page() {
  return (
    <PermissionGate permissions={['leave.read','leave.write']}>
      <LeavePage />
    </PermissionGate>
  );
}
