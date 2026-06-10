'use client';

import { useEffect, useMemo, useState } from 'react';
import { Bot, Search, X } from 'lucide-react';
import { useRouter, usePathname } from 'next/navigation';
import { Sidebar } from './Sidebar';
import { TopBar } from './TopBar';
import { employeesApi } from '../api/employees';
import { reportsApi } from '../api/reports';
import { usersApi } from '../api/identity';
import { useAuth } from '../contexts/AuthContext';
import { navigationItems } from '../routes/navigation';
import type { ThemeMode } from '../types/ui';

interface AppLayoutProps {
  children: React.ReactNode;
  theme: ThemeMode;
  onToggleTheme: () => void;
}

interface PaletteItem {
  id: string;
  label: string;
  sublabel: string;
  icon: 'bot' | 'search';
  onSelect: () => void;
}

export function AppLayout({ children, theme, onToggleTheme }: AppLayoutProps) {
  const router = useRouter();
  const pathname = usePathname();
  const { hasPermission } = useAuth();
  const [sidebarOpen, setSidebarOpen] = useState(false);
  const [sidebarCollapsed, setSidebarCollapsed] = useState(() => typeof window !== 'undefined' && localStorage.getItem('sidebar-collapsed') === 'true');
  const [commandOpen, setCommandOpen] = useState(false);
  const [commandQuery, setCommandQuery] = useState('');
  const [employeeResults, setEmployeeResults] = useState<Array<{ id: number; label: string; sublabel: string }>>([]);
  const [userResults, setUserResults] = useState<Array<{ id: string; label: string; sublabel: string; searchText: string }>>([]);
  const [reportResults, setReportResults] = useState<Array<{ key: string; label: string; sublabel: string }>>([]);
  const [searching, setSearching] = useState(false);
  const [activeIndex, setActiveIndex] = useState(0);

  const commandItems = useMemo(() => {
    const base = navigationItems
      .filter((item): item is typeof item & { path: string } => Boolean(item.path))
      .map((item) => ({
        label: item.label,
        path: item.path,
        description: item.requiredPermissions?.length
          ? `Requires ${item.requiredPermissions.join(' or ')}`
          : 'Open module',
      }));

    const extras = [
      { label: 'Ask KynexOne AI', path: '/ai-assistant', description: 'Open the advisory AI assistant' },
      { label: 'Search Employees', path: '/people', description: 'Jump to employee search and records' },
      { label: 'Review Approvals', path: '/approvals', description: 'Open approval center' },
      { label: 'View Reports', path: '/reports', description: 'Open reports and analytics' },
    ];

    return [...new Map([...extras, ...base].map((item) => [item.path, item])).values()];
  }, []);

  const visibleModules = useMemo(
    () => commandItems.filter((item) => {
      const navMatch = navigationItems.find((nav) => nav.path === item.path);
      if (!navMatch?.requiredPermissions?.length) return true;
      return navMatch.requiredPermissions.every((permission) => hasPermission(permission));
    }),
    [commandItems, hasPermission],
  );

  const filteredCommands = useMemo(() => {
    const term = commandQuery.trim().toLowerCase();
    if (!term) return visibleModules.slice(0, 10);
    return visibleModules
      .filter((item) =>
        item.label.toLowerCase().includes(term) ||
        item.path.toLowerCase().includes(term) ||
        item.description.toLowerCase().includes(term)
      )
      .slice(0, 10);
  }, [commandQuery, visibleModules]);

  const showingSearchResults = commandQuery.trim().length >= 2
    && (employeeResults.length > 0 || userResults.length > 0 || reportResults.length > 0);

  useEffect(() => {
    if (!commandOpen) return;
    const term = commandQuery.trim();
    if (term.length < 2) {
      setEmployeeResults([]);
      setUserResults([]);
      setReportResults([]);
      setSearching(false);
      return;
    }

    let cancelled = false;
    setSearching(true);
    const timer = window.setTimeout(async () => {
      try {
        const tasks: Promise<void>[] = [];

        if (hasPermission('employees.read')) {
          tasks.push(
            employeesApi.list({ search: term, page: 1, pageSize: 5 })
              .then((result) => {
                if (cancelled) return;
                setEmployeeResults(
                  result.items.map((employee) => ({
                    id: employee.id,
                    label: `${employee.fullName} · ${employee.employeeCode}`,
                    sublabel: `${employee.department || 'No department'} · ${employee.designation || 'No title'}`,
                  })),
                );
              })
              .catch(() => {
                if (!cancelled) setEmployeeResults([]);
              }),
          );
        } else {
          setEmployeeResults([]);
        }

        if (hasPermission('users.manage') || hasPermission('security.manage')) {
          tasks.push(
            usersApi.list({ search: term, page: 1, pageSize: 5 })
              .then((result) => {
                if (cancelled) return;
                setUserResults(
                  result.items.map((user) => ({
                    id: user.id,
                    label: `${user.fullName} · ${user.email}`,
                    sublabel: `${user.status} · ${user.roles.join(', ') || 'No roles'}`,
                    searchText: user.email || user.fullName,
                  })),
                );
              })
              .catch(() => {
                if (!cancelled) setUserResults([]);
              }),
          );
        } else {
          setUserResults([]);
        }

        if (hasPermission('reports.read')) {
          tasks.push(
            reportsApi.catalog()
              .then((catalog) => {
                if (cancelled) return;
                const matches = catalog
                  .filter((report) =>
                    report.name.toLowerCase().includes(term.toLowerCase()) ||
                    report.key.toLowerCase().includes(term.toLowerCase()) ||
                    report.description.toLowerCase().includes(term.toLowerCase()) ||
                    report.category.toLowerCase().includes(term.toLowerCase()),
                  )
                  .slice(0, 5)
                  .map((report) => ({
                    key: report.key,
                    label: report.name,
                    sublabel: `${report.category} · ${report.description}`,
                  }));
                setReportResults(matches);
              })
              .catch(() => {
                if (!cancelled) setReportResults([]);
              }),
          );
        } else {
          setReportResults([]);
        }

        await Promise.allSettled(tasks);
      } finally {
        if (!cancelled) setSearching(false);
      }
    }, 220);

    return () => {
      cancelled = true;
      window.clearTimeout(timer);
    };
  }, [commandOpen, commandQuery, hasPermission]);

  useEffect(() => {
    if (commandOpen) setActiveIndex(0);
  }, [commandOpen, commandQuery]);

  const openCommandPalette = () => {
    setCommandQuery('');
    setCommandOpen(true);
  };

  const closeCommandPalette = () => setCommandOpen(false);

  const runCommand = (path: string) => {
    router.push(path);
    closeCommandPalette();
  };

  const openEmployeeResult = (id: number) => {
    router.push(`/people?employeeId=${id}`);
    closeCommandPalette();
  };

  const openUserResult = (searchText: string) => {
    router.push(`/user-management?search=${encodeURIComponent(searchText)}`);
    closeCommandPalette();
  };

  const openReportResult = (key: string) => {
    router.push(`/reports?report=${encodeURIComponent(key)}`);
    closeCommandPalette();
  };

  const paletteItems = useMemo<PaletteItem[]>(() => {
    if (showingSearchResults) {
      return [
        ...employeeResults.map((item) => ({
          id: `employee-${item.id}`,
          label: item.label,
          sublabel: item.sublabel,
          icon: 'search' as const,
          onSelect: () => openEmployeeResult(item.id),
        })),
        ...userResults.map((item) => ({
          id: `user-${item.id}`,
          label: item.label,
          sublabel: item.sublabel,
          icon: 'search' as const,
          onSelect: () => openUserResult(item.searchText),
        })),
        ...reportResults.map((item) => ({
          id: `report-${item.key}`,
          label: item.label,
          sublabel: item.sublabel,
          icon: 'search' as const,
          onSelect: () => openReportResult(item.key),
        })),
      ];
    }

    return filteredCommands.map((item) => ({
      id: `${item.path}-${item.label}`,
      label: item.label,
      sublabel: item.description,
      icon: item.path === '/ai-assistant' ? 'bot' as const : 'search' as const,
      onSelect: () => runCommand(item.path),
    }));
  }, [employeeResults, filteredCommands, reportResults, showingSearchResults, userResults]);

  useEffect(() => {
    if (!commandOpen) {
      setActiveIndex(0);
      return;
    }
    setActiveIndex((current) => (paletteItems.length === 0 ? 0 : Math.min(current, paletteItems.length - 1)));
  }, [commandOpen, paletteItems.length]);

  useEffect(() => {
    const handler = (event: KeyboardEvent) => {
      if (commandOpen) {
        if (event.key === 'ArrowDown') {
          event.preventDefault();
          setActiveIndex((current) => (paletteItems.length === 0 ? 0 : (current + 1) % paletteItems.length));
          return;
        }

        if (event.key === 'ArrowUp') {
          event.preventDefault();
          setActiveIndex((current) => (paletteItems.length === 0 ? 0 : (current - 1 + paletteItems.length) % paletteItems.length));
          return;
        }

        if (event.key === 'Enter') {
          event.preventDefault();
          paletteItems[activeIndex]?.onSelect();
          return;
        }
      }

      if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === 'k') {
        event.preventDefault();
        openCommandPalette();
        return;
      }

      if (event.key === 'Escape') {
        setCommandOpen(false);
      }
    };

    window.addEventListener('keydown', handler);
    return () => window.removeEventListener('keydown', handler);
  }, [activeIndex, commandOpen, paletteItems]);

  return (
    <div className="min-h-screen overflow-x-hidden bg-lightBg text-slate-950 dark:bg-midnight dark:text-white">
      <div className="flex min-h-screen">
        <Sidebar
          isOpen={sidebarOpen}
          isCollapsed={sidebarCollapsed}
          onClose={() => setSidebarOpen(false)}
          onToggleCollapse={() => setSidebarCollapsed((c) => {
            const next = !c;
            localStorage.setItem('sidebar-collapsed', String(next));
            return next;
          })}
        />
        <div className="min-w-0 flex-1 overflow-x-hidden">
          <TopBar
            theme={theme}
            onToggleTheme={onToggleTheme}
            onOpenSidebar={() => setSidebarOpen(true)}
            onOpenSearch={openCommandPalette}
            onAskKynexOne={() => router.push('/ai-assistant')}
          />
          <main key={pathname} className="animate-fade-in-up px-4 py-6 sm:px-6 lg:px-8">{children}</main>
        </div>
      </div>

      {commandOpen && (
        <div
          className="fixed inset-0 z-50 flex items-start justify-center bg-slate-950/50 px-4 pt-24 backdrop-blur-sm"
          onMouseDown={(event) => {
            if (event.target === event.currentTarget) closeCommandPalette();
          }}
        >
          <div className="w-full max-w-2xl overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-2xl dark:border-white/10 dark:bg-[#0D1221]">
            <div className="flex items-center gap-3 border-b border-slate-100 px-4 py-4 dark:border-white/[0.07]">
              <Search className="h-4 w-4 text-slate-400" />
              <input
                autoFocus
                value={commandQuery}
                onChange={(event) => setCommandQuery(event.target.value)}
                placeholder="Search modules, employees, reports, or ask KynexOne..."
                className="w-full bg-transparent text-sm text-slate-900 outline-none placeholder:text-slate-400 dark:text-white dark:placeholder:text-slate-500"
              />
              <button
                type="button"
                aria-label="Close search"
                onClick={closeCommandPalette}
                className="grid h-7 w-7 place-items-center rounded-md text-slate-400 hover:bg-slate-100 dark:hover:bg-white/10"
              >
                <X className="h-4 w-4" />
              </button>
            </div>

            <div className="max-h-[26rem] overflow-y-auto p-2">
              {showingSearchResults ? (
                <div className="space-y-3 p-2">
                  {employeeResults.length > 0 && (
                    <div>
                      <p className="px-3 pb-1 text-[10px] font-bold uppercase tracking-wide text-slate-400">Employees</p>
                      <div className="space-y-1">
                        {employeeResults.map((item) => (
                          (() => {
                            const index = paletteItems.findIndex((entry) => entry.id === `employee-${item.id}`);
                            const selected = index === activeIndex;
                            return (
                          <button
                            key={`employee-${item.id}`}
                            type="button"
                            onClick={() => openEmployeeResult(item.id)}
                            className={`flex w-full items-center gap-3 rounded-xl px-4 py-3 text-left transition ${selected ? 'bg-sapphire/10 dark:bg-cyanAccent/10' : 'hover:bg-slate-50 dark:hover:bg-white/[0.05]'}`}
                          >
                            <span className="grid h-9 w-9 shrink-0 place-items-center rounded-lg bg-sapphire/10 text-sapphire dark:bg-cyanAccent/10 dark:text-cyanAccent">
                              <Search className="h-4 w-4" />
                            </span>
                            <span className="min-w-0 flex-1">
                              <span className="block truncate text-sm font-semibold text-slate-900 dark:text-white">{item.label}</span>
                              <span className="block truncate text-xs text-slate-500 dark:text-slate-400">{item.sublabel}</span>
                            </span>
                          </button>
                            );
                          })()
                        ))}
                      </div>
                    </div>
                  )}

                  {userResults.length > 0 && (
                    <div>
                      <p className="px-3 pb-1 text-[10px] font-bold uppercase tracking-wide text-slate-400">Users</p>
                      <div className="space-y-1">
                        {userResults.map((item) => (
                          (() => {
                            const index = paletteItems.findIndex((entry) => entry.id === `user-${item.id}`);
                            const selected = index === activeIndex;
                            return (
                          <button
                            key={`user-${item.id}`}
                            type="button"
                            onClick={() => openUserResult(item.searchText)}
                            className={`flex w-full items-center gap-3 rounded-xl px-4 py-3 text-left transition ${selected ? 'bg-sapphire/10 dark:bg-cyanAccent/10' : 'hover:bg-slate-50 dark:hover:bg-white/[0.05]'}`}
                          >
                            <span className="grid h-9 w-9 shrink-0 place-items-center rounded-lg bg-slate-100 text-slate-500 dark:bg-white/[0.06] dark:text-slate-300">
                              <Search className="h-4 w-4" />
                            </span>
                            <span className="min-w-0 flex-1">
                              <span className="block truncate text-sm font-semibold text-slate-900 dark:text-white">{item.label}</span>
                              <span className="block truncate text-xs text-slate-500 dark:text-slate-400">{item.sublabel}</span>
                            </span>
                          </button>
                            );
                          })()
                        ))}
                      </div>
                    </div>
                  )}

                  {reportResults.length > 0 && (
                    <div>
                      <p className="px-3 pb-1 text-[10px] font-bold uppercase tracking-wide text-slate-400">Reports</p>
                      <div className="space-y-1">
                        {reportResults.map((item) => (
                          (() => {
                            const index = paletteItems.findIndex((entry) => entry.id === `report-${item.key}`);
                            const selected = index === activeIndex;
                            return (
                          <button
                            key={`report-${item.key}`}
                            type="button"
                            onClick={() => openReportResult(item.key)}
                            className={`flex w-full items-center gap-3 rounded-xl px-4 py-3 text-left transition ${selected ? 'bg-sapphire/10 dark:bg-cyanAccent/10' : 'hover:bg-slate-50 dark:hover:bg-white/[0.05]'}`}
                          >
                            <span className="grid h-9 w-9 shrink-0 place-items-center rounded-lg bg-slate-100 text-slate-500 dark:bg-white/[0.06] dark:text-slate-300">
                              <Search className="h-4 w-4" />
                            </span>
                            <span className="min-w-0 flex-1">
                              <span className="block truncate text-sm font-semibold text-slate-900 dark:text-white">{item.label}</span>
                              <span className="block truncate text-xs text-slate-500 dark:text-slate-400">{item.sublabel}</span>
                            </span>
                          </button>
                            );
                          })()
                        ))}
                      </div>
                    </div>
                  )}

                  {searching && (
                    <div className="px-4 py-3 text-sm text-slate-500 dark:text-slate-400">Searching across employees, users, and reports…</div>
                  )}
                </div>
              ) : filteredCommands.length === 0 ? (
                <div className="px-4 py-10 text-center text-sm text-slate-500 dark:text-slate-400">
                  No matches found. Try a module name like payroll, leave, or AI.
                </div>
              ) : (
                filteredCommands.map((item) => {
                  const isAi = item.path === '/ai-assistant';
                  const index = paletteItems.findIndex((entry) => entry.id === `${item.path}-${item.label}`);
                  const selected = index === activeIndex;
                  return (
                    <button
                      key={`${item.path}-${item.label}`}
                      type="button"
                      onClick={() => runCommand(item.path)}
                      className={`flex w-full items-center gap-3 rounded-xl px-4 py-3 text-left transition ${selected ? 'bg-sapphire/10 dark:bg-cyanAccent/10' : 'hover:bg-slate-50 dark:hover:bg-white/[0.05]'}`}
                    >
                      <span className={`grid h-9 w-9 shrink-0 place-items-center rounded-lg ${isAi ? 'bg-sapphire/10 text-sapphire dark:bg-cyanAccent/10 dark:text-cyanAccent' : 'bg-slate-100 text-slate-500 dark:bg-white/[0.06] dark:text-slate-300'}`}>
                        {isAi ? <Bot className="h-4 w-4" /> : <Search className="h-4 w-4" />}
                      </span>
                      <span className="min-w-0 flex-1">
                        <span className="block truncate text-sm font-semibold text-slate-900 dark:text-white">{item.label}</span>
                        <span className="block truncate text-xs text-slate-500 dark:text-slate-400">{item.description}</span>
                      </span>
                      <span className="rounded-full border border-slate-200 px-2 py-0.5 text-[10px] font-medium text-slate-400 dark:border-white/10 dark:text-slate-500">
                        {item.path}
                      </span>
                    </button>
                  );
                })
              )}
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
