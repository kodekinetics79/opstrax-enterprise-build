import { useState } from 'react';
import { Sidebar } from './Sidebar';
import { TopBar } from './TopBar';
import type { ThemeMode } from '../types/ui';

interface AppLayoutProps {
  children: React.ReactNode;
  theme: ThemeMode;
  onToggleTheme: () => void;
}

export function AppLayout({ children, theme, onToggleTheme }: AppLayoutProps) {
  const [sidebarOpen, setSidebarOpen] = useState(false);
  const [sidebarCollapsed, setSidebarCollapsed] = useState(false);

  return (
    <div className="min-h-screen bg-lightBg text-slate-950 dark:bg-midnight dark:text-white">
      <div className="flex min-h-screen">
        <Sidebar
          isOpen={sidebarOpen}
          isCollapsed={sidebarCollapsed}
          onClose={() => setSidebarOpen(false)}
          onToggleCollapse={() => setSidebarCollapsed((c) => !c)}
        />
        <div className="min-w-0 flex-1">
          <TopBar
            theme={theme}
            onToggleTheme={onToggleTheme}
            onOpenSidebar={() => setSidebarOpen(true)}
          />
          <main className="px-4 py-6 sm:px-6 lg:px-8">{children}</main>
        </div>
      </div>
    </div>
  );
}
