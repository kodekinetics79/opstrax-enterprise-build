'use client';

import { useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { useAuth } from '@/src/contexts/AuthContext';

interface ProtectedRouteProps {
  children: React.ReactNode;
  requiredPermissions?: string[];
}

export function ProtectedRoute({ children, requiredPermissions }: ProtectedRouteProps) {
  const { user, isLoading, hasPermission } = useAuth();
  const router = useRouter();

  useEffect(() => {
    if (!isLoading && !user) router.replace('/login');
  }, [isLoading, user, router]);

  useEffect(() => {
    if (user && requiredPermissions?.length) {
      const hasAccess = requiredPermissions.some(p => hasPermission(p));
      if (!hasAccess) router.replace('/dashboard');
    }
  }, [user, requiredPermissions, hasPermission, router]);

  if (isLoading || !user) {
    return (
      <div className="flex min-h-screen items-center justify-center bg-lightBg dark:bg-midnight">
        <div className="h-8 w-8 animate-spin rounded-full border-2 border-sapphire border-t-transparent" />
      </div>
    );
  }

  if (requiredPermissions?.length && !requiredPermissions.some(p => hasPermission(p))) {
    return null;
  }

  return <>{children}</>;
}
