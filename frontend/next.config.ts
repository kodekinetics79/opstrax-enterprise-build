import type { NextConfig } from 'next';

const nextConfig: NextConfig = {
  poweredByHeader: false,

  // Allow images from any HTTPS source (Railway-hosted avatars, CV links, etc.)
  images: {
    remotePatterns: [{ protocol: 'https', hostname: '**' }],
  },
};

export default nextConfig;
