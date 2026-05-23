import type { Config } from 'tailwindcss';

export default {
  darkMode: 'class',
  content: ['./index.html', './src/**/*.{ts,tsx}'],
  theme: {
    extend: {
      colors: {
        midnight: '#0B1020',
        sapphire: '#2F6BFF',
        cyanAccent: '#5EEBFF',
        darkSlate: '#111827',
        lightBg: '#F8FAFC',
        emeraldZ: '#00C896',
        sidebarDark: '#0D1221',
      },
      boxShadow: {
        soft: '0 4px 24px rgba(15, 23, 42, 0.07)',
        'soft-md': '0 8px 32px rgba(15, 23, 42, 0.10)',
        glow: '0 0 0 1px rgba(94, 235, 255, 0.20), 0 8px 32px rgba(47, 107, 255, 0.20)',
        'glow-sm': '0 0 0 1px rgba(47, 107, 255, 0.22)',
        panel: '0 1px 3px rgba(0,0,0,0.06), 0 1px 2px rgba(0,0,0,0.04)',
        'kpi': '0 2px 8px rgba(15, 23, 42, 0.06)',
      },
      fontFamily: {
        sans: ['Inter', 'ui-sans-serif', 'system-ui', 'sans-serif'],
      },
      borderRadius: {
        xl: '12px',
        '2xl': '16px',
      },
      transitionProperty: {
        width: 'width',
      },
    },
  },
  plugins: [],
} satisfies Config;
