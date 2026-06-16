'use client';

import { useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { useAuth } from '@/src/contexts/AuthContext';

interface PermissionGateProps {
  permissions: string[];
  children: React.ReactNode;
}

export function PermissionGate({ permissions, children }: PermissionGateProps) {
  const { user, hasPermission } = useAuth();
  const router = useRouter();
  const hasAccess = permissions.length === 0 || permissions.some(p => hasPermission(p));

  useEffect(() => {
    if (user && !hasAccess) router.replace('/access-denied');
  }, [user, hasAccess, router]);

  if (!hasAccess) return null;
  return <>{children}</>;
}
