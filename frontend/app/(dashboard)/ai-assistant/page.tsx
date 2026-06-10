import { PermissionGate } from '@/src/components/PermissionGate';
import AIAssistantPage from '@/src/views/AIAssistantPage';
export default function Page() {
  return (
    <PermissionGate permissions={['ai.query','ai.insights_view']}>
      <AIAssistantPage />
    </PermissionGate>
  );
}
