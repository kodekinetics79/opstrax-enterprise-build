'use client';

import { useRouter } from 'next/navigation';
import { useEffect } from 'react';

// Redirect to /platform/tenants — new tenant modal is launched there
export default function NewTenantRedirect() {
  const router = useRouter();
  useEffect(() => { router.replace('/platform/tenants'); }, [router]);
  return null;
}
