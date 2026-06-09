interface LogoProps {
  collapsed?: boolean;
  size?: 'sm' | 'md' | 'lg';
}

export function Logo({ collapsed, size = 'md' }: LogoProps) {
  const dim = size === 'lg' ? 44 : size === 'sm' ? 28 : 36;

  return (
    <div className="flex items-center gap-2.5">
      <svg
        width={dim}
        height={dim}
        viewBox="0 0 32 32"
        fill="none"
        xmlns="http://www.w3.org/2000/svg"
        className="shrink-0"
        aria-hidden="true"
      >
        {/* Rounded square base with gradient */}
        <rect width="32" height="32" rx="8" fill="url(#logo-bg)" />

        {/* Subtle grid dots */}
        <circle cx="9" cy="9" r="1" fill="white" fillOpacity="0.18" />
        <circle cx="16" cy="9" r="1" fill="white" fillOpacity="0.18" />
        <circle cx="23" cy="9" r="1" fill="white" fillOpacity="0.18" />
        <circle cx="9" cy="16" r="1" fill="white" fillOpacity="0.18" />
        <circle cx="23" cy="16" r="1" fill="white" fillOpacity="0.18" />
        <circle cx="9" cy="23" r="1" fill="white" fillOpacity="0.18" />
        <circle cx="16" cy="23" r="1" fill="white" fillOpacity="0.18" />
        <circle cx="23" cy="23" r="1" fill="white" fillOpacity="0.18" />

        {/* K letterform — bold, clean */}
        <path
          d="M10 9.5V22.5M10 16L20.5 9.5M10 16L20.5 22.5"
          stroke="white"
          strokeWidth="2.4"
          strokeLinecap="round"
          strokeLinejoin="round"
        />

        {/* Accent dot — top right */}
        <circle cx="23.5" cy="8.5" r="2.5" fill="url(#logo-dot)" />

        <defs>
          <linearGradient id="logo-bg" x1="0" y1="0" x2="32" y2="32" gradientUnits="userSpaceOnUse">
            <stop offset="0%" stopColor="#1e40af" />
            <stop offset="100%" stopColor="#2F6BFF" />
          </linearGradient>
          <linearGradient id="logo-dot" x1="21" y1="6" x2="26" y2="11" gradientUnits="userSpaceOnUse">
            <stop offset="0%" stopColor="#5EEBFF" />
            <stop offset="100%" stopColor="#00C896" />
          </linearGradient>
        </defs>
      </svg>

      {!collapsed && (
        <div className="min-w-0 leading-none">
          <p className="truncate text-[15px] font-bold tracking-tight text-slate-900 dark:text-white">
            Kynex<span className="font-black text-sapphire dark:text-[#7AABFF]">One</span>
          </p>
          <p className="truncate text-[10px] font-medium tracking-wide text-slate-400 dark:text-slate-500">
            Workforce Platform
          </p>
        </div>
      )}
    </div>
  );
}
