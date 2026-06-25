import { Navigate, Route, Routes } from "react-router-dom";
import { PlatformAuthProvider, usePlatformAuth } from "@/hooks/usePlatformAuth";
import { PlatformShell } from "@/layouts/PlatformShell";
import { PlatformLoginPage } from "./PlatformLoginPage";
import { PlatformCommandCenterPage } from "./PlatformCommandCenterPage";
import { PlatformTenantsPage } from "./PlatformTenantsPage";
import { PlatformPackagesPage } from "./PlatformPackagesPage";
import { PlatformBillingPage } from "./PlatformBillingPage";
import { PlatformHealthPage } from "./PlatformHealthPage";
import { PlatformAuditPage } from "./PlatformAuditPage";

// Permission-gated wrapper: redirects to the platform login if not authenticated,
// and to the command center if the role lacks the required permission.
function Guard({ permission, children }: { permission?: string; children: React.ReactNode }) {
  const { session, can } = usePlatformAuth();
  if (!session) return <Navigate to="/platform/login" replace />;
  if (permission && !can(permission)) return <Navigate to="/platform" replace />;
  return <>{children}</>;
}

function ProtectedShell() {
  const { session } = usePlatformAuth();
  return session ? <PlatformShell /> : <Navigate to="/platform/login" replace />;
}

// Entire Platform Admin surface. Mounted at /platform/* with its OWN auth context,
// fully isolated from the tenant application's session and routing.
export default function PlatformApp() {
  return (
    <PlatformAuthProvider>
      <Routes>
        <Route path="login" element={<PlatformLoginPage />} />
        <Route element={<ProtectedShell />}>
          <Route index element={<Guard permission="platform:dashboard:view"><PlatformCommandCenterPage /></Guard>} />
          <Route path="tenants" element={<Guard permission="platform:tenants:view"><PlatformTenantsPage /></Guard>} />
          <Route path="packages" element={<Guard permission="platform:packages:view"><PlatformPackagesPage /></Guard>} />
          <Route path="billing" element={<Guard permission="platform:billing:view"><PlatformBillingPage /></Guard>} />
          <Route path="health" element={<Guard permission="platform:health:view"><PlatformHealthPage /></Guard>} />
          <Route path="audit" element={<Guard permission="platform:audit:view"><PlatformAuditPage /></Guard>} />
        </Route>
        <Route path="*" element={<Navigate to="/platform" replace />} />
      </Routes>
    </PlatformAuthProvider>
  );
}
