import type { NextConfig } from 'next';

const apiUrl = process.env.NEXT_PUBLIC_API_URL ?? 'http://localhost:5000';

const nextConfig: NextConfig = {
  poweredByHeader: false,

  // Proxy /api/* to the backend so local dev avoids CORS entirely.
  async rewrites() {
    return [
      {
        source: '/api/:path*',
        destination: `${apiUrl}/api/:path*`,
      },
    ];
  },

  // Allow images from any HTTPS source (Railway-hosted avatars, CV links, etc.)
  images: {
    remotePatterns: [{ protocol: 'https', hostname: '**' }],
  },
};

export default nextConfig;
