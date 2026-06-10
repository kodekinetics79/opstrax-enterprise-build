import { PermissionGate } from '@/src/components/PermissionGate';
import { ShiftsPage } from '@/src/views/ShiftsPage';
export default function Page() {
  return (
    <PermissionGate permissions={['attendance.read']}>
      <ShiftsPage />
    </PermissionGate>
  );
}
