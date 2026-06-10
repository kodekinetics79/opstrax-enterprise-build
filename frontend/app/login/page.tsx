import { Suspense } from 'react';
import { LoginPage } from '@/src/views/LoginPage';

export default function Page() {
  return (
    <Suspense>
      <LoginPage />
    </Suspense>
  );
}
