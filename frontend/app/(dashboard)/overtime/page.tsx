import { PermissionGate } from '@/src/components/PermissionGate';
import { OvertimePage } from '@/src/views/OvertimePage';
export default function Page() {
  return (
    <PermissionGate permissions={['overtime.read','overtime.write']}>
      <OvertimePage />
    </PermissionGate>
  );
}
