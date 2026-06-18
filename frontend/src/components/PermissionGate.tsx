'use client';

import { useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { useAuth } from '@/src/contexts/AuthContext';
import { useAppToast } from '@/src/components/ui/AppToast';

interface PermissionGateProps {
  permissions: string[];
  children: React.ReactNode;
}

export function PermissionGate({ permissions, children }: PermissionGateProps) {
  const { user, hasPermission } = useAuth();
  const router = useRouter();
  const toast  = useAppToast();
  const hasAccess = permissions.length === 0 || permissions.some(p => hasPermission(p));

  useEffect(() => {
    if (user && !hasAccess) {
      toast.error(
        'You do not have permission to view this page. Please contact your administrator.',
        'Access Denied',
      );
      router.replace('/dashboard');
    }
  }, [user, hasAccess, router, toast]);

  if (!hasAccess) return null;
  return <>{children}</>;
}
