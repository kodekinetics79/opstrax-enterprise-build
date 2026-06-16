import { PermissionGate } from '@/src/components/PermissionGate';
import { PerformancePage } from '@/src/views/PerformancePage';
export default function Page() {
  return (
    <PermissionGate permissions={['performance.read', 'performance.write']}>
      <PerformancePage initialTab="my-reviews" />
    </PermissionGate>
  );
}
