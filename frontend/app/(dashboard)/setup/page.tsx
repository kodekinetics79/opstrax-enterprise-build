import { PermissionGate } from '@/src/components/PermissionGate';
import { SetupPage } from '@/src/views/SetupPage';
export default function Page() {
  return (
    <PermissionGate permissions={['organization.write']}>
      <SetupPage />
    </PermissionGate>
  );
}
