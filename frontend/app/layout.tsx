import type { Metadata, Viewport } from 'next';
import { Providers } from '@/src/components/Providers';
import '@/src/styles/index.css';

export const metadata: Metadata = {
  title: 'KynexOne — One Platform for Every Workforce Operation',
  description: 'HR, payroll, recruitment, attendance and compliance — unified.',
};

export const viewport: Viewport = {
  themeColor: '#0B1020',
};

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en">
      <head>
        <link rel="preconnect" href="https://fonts.googleapis.com" />
        <link rel="preconnect" href="https://fonts.gstatic.com" crossOrigin="" />
        <link
          href="https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700;800&display=swap"
          rel="stylesheet"
        />
      </head>
      <body>
        <Providers>{children}</Providers>
      </body>
    </html>
  );
}
