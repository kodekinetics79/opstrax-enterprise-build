import { PermissionGate } from '@/src/components/PermissionGate';
import { AttendancePage } from '@/src/views/AttendancePage';
export default function Page() {
  return (
    <PermissionGate permissions={['attendance.read','attendance.write','attendance.kiosk']}>
      <AttendancePage />
    </PermissionGate>
  );
}
