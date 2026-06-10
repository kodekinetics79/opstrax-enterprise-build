'use client';

import { useEffect } from 'react';
import { useRouter } from 'next/navigation';

export default function PlatformRootPage() {
  const router = useRouter();

  useEffect(() => {
    const token = localStorage.getItem('platform_access_token');
    if (token) {
      router.replace('/platform/dashboard');
    } else {
      router.replace('/platform/login');
    }
  }, [router]);

  return null;
}
