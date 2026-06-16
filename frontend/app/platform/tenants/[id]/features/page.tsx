'use client';

import { useParams, useRouter } from 'next/navigation';
import Link from 'next/link';
import { ArrowLeft } from 'lucide-react';
import { useEffect } from 'react';

// Feature flags are managed in the tenant detail page (/platform/tenants/[id])
// under the Features tab. This route redirects there.
export default function TenantFeaturesRedirect() {
  const { id } = useParams<{ id: string }>();
  const router  = useRouter();
  useEffect(() => { router.replace(`/platform/tenants/${id}?tab=features`); }, [id, router]);
  return (
    <div className="flex items-center justify-center min-h-[60vh] text-slate-500 text-sm">
      Redirecting to tenant features…
    </div>
  );
}
