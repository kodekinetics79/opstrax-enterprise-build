import { ChevronLeft, ChevronRight, LogOut, X } from 'lucide-react';
import { useNavigate, useLocation } from 'react-router-dom';
import { Avatar } from '../components/Avatar';
import { Logo } from '../components/Logo';
import { navigationGroups } from '../routes/navigation';
import { useAuth } from '../contexts/AuthContext';

interface SidebarProps {
  isOpen: boolean;
  isCollapsed: boolean;
  onClose: () => void;
  onToggleCollapse: () => void;
}

export function Sidebar({ isOpen, isCollapsed, onClose, onToggleCollapse }: SidebarProps) {
  const navigate = useNavigate();
  const { pathname } = useLocation();
  const { user, logout, hasPermission } = useAuth();

  const canSee = (requiredPermissions?: string[]) => {
    if (!requiredPermissions || requiredPermissions.length === 0) return true;
    return requiredPermissions.some(p => hasPermission(p));
  };

  const handleNav = (path?: string) => {
    if (path) navigate(path);
    onClose();
  };

  const handleLogout = async () => {
    await logout();
    navigate('/login', { replace: true });
  };

  const isActive = (path?: string) => {
    if (!path) return false;
    if (path === '/dashboard') return pathname === '/dashboard' || pathname === '/';
    return pathname.startsWith(path);
  };

  return (
    <>
      {/* Mobile overlay */}
      <div
        className={`fixed inset-0 z-30 bg-midnight/60 backdrop-blur-sm transition-opacity duration-200 lg:hidden ${
          isOpen ? 'opacity-100' : 'pointer-events-none opacity-0'
        }`}
        onClick={onClose}
        aria-hidden="true"
      />

      <aside
        className={`fixed inset-y-0 left-0 z-40 flex flex-col border-r border-slate-200 bg-white transition-all duration-200 dark:border-white/[0.07] dark:bg-[#0D1221] lg:static lg:translate-x-0 ${
          isOpen ? 'translate-x-0' : '-translate-x-full'
        } ${isCollapsed ? 'lg:w-16' : 'lg:w-[260px]'} w-[260px]`}
      >
        {/* Header */}
        <div
          className={`flex h-[60px] shrink-0 items-center border-b border-slate-200 px-3 dark:border-white/[0.07] ${
            isCollapsed ? 'justify-center' : 'justify-between'
          }`}
        >
          <Logo collapsed={isCollapsed} />
          <div className="flex items-center">
            <button
              type="button"
              aria-label="Close navigation"
              onClick={onClose}
              className="grid h-8 w-8 place-items-center rounded-lg text-slate-400 hover:bg-slate-100 lg:hidden dark:hover:bg-white/10"
            >
              <X className="h-4 w-4" />
            </button>
            {!isCollapsed && (
              <button
                type="button"
                aria-label="Collapse sidebar"
                onClick={onToggleCollapse}
                className="hidden h-8 w-8 place-items-center rounded-lg text-slate-400 hover:bg-slate-100 hover:text-slate-700 lg:grid dark:hover:bg-white/10 dark:hover:text-slate-300"
              >
                <ChevronLeft className="h-4 w-4" />
              </button>
            )}
          </div>
        </div>

        {/* Navigation */}
        <nav aria-label="Primary navigation" className="flex-1 overflow-y-auto py-2">
          {navigationGroups.map((group, gi) => {
            const visibleItems = group.items.filter(item => canSee(item.requiredPermissions));
            if (visibleItems.length === 0) return null;
            return (
            <div key={group.label} className={gi > 0 ? 'mt-1' : ''}>
              {!isCollapsed ? (
                <p className="mb-0.5 mt-3 px-4 text-[10px] font-bold uppercase tracking-[0.14em] text-slate-400 first:mt-2 dark:text-slate-500">
                  {group.label}
                </p>
              ) : (
                gi > 0 && <div className="mx-3 mb-1 mt-2 h-px bg-slate-100 dark:bg-white/[0.07]" />
              )}

              <div className="space-y-0.5 px-2">
                {visibleItems.map((item) => {
                  const Icon = item.icon;
                  const active = isActive(item.path);

                  return (
                    <button
                      key={item.label}
                      type="button"
                      title={isCollapsed ? item.label : undefined}
                      onClick={() => handleNav(item.path)}
                      className={`nav-item relative ${active ? 'nav-item-active' : 'nav-item-idle'} ${
                        isCollapsed ? 'justify-center' : ''
                      }`}
                    >
                      <Icon className="h-4 w-4 shrink-0" />

                      {!isCollapsed && (
                        <span className="min-w-0 flex-1 truncate">{item.label}</span>
                      )}

                      {item.badge != null && !isCollapsed && (
                        <span
                          className={`rounded-full px-1.5 py-0.5 text-[10px] font-bold leading-none ${
                            active
                              ? 'bg-white/20 text-white'
                              : 'bg-sapphire/10 text-sapphire dark:bg-white/10 dark:text-slate-300'
                          }`}
                        >
                          {item.badge}
                        </span>
                      )}

                      {item.badge != null && isCollapsed && (
                        <span className="absolute right-2 top-2 h-1.5 w-1.5 rounded-full bg-sapphire" />
                      )}
                    </button>
                  );
                })}
              </div>
            </div>
          );
          })}
        </nav>

        {/* Footer */}
        <div className="shrink-0 border-t border-slate-200 dark:border-white/[0.07]">
          {isCollapsed ? (
            <button
              type="button"
              aria-label="Expand sidebar"
              onClick={onToggleCollapse}
              className="hidden w-full items-center justify-center py-3 text-slate-400 hover:text-slate-600 lg:flex dark:hover:text-slate-300"
            >
              <ChevronRight className="h-4 w-4" />
            </button>
          ) : (
            <div className="p-3">
              <div className="flex items-center gap-2.5 rounded-lg px-2 py-2">
                <Avatar name={user?.fullName ?? 'User'} size="sm" />
                <div className="min-w-0 flex-1 text-left">
                  <p className="truncate text-sm font-semibold text-slate-900 dark:text-white">
                    {user?.fullName ?? 'User'}
                  </p>
                  <p className="truncate text-[11px] text-slate-500 dark:text-slate-400">
                    {user?.roles[0] ?? 'Member'}
                  </p>
                </div>
                <button
                  type="button"
                  aria-label="Sign out"
                  onClick={handleLogout}
                  className="grid h-7 w-7 shrink-0 place-items-center rounded-lg text-slate-400 hover:bg-slate-100 hover:text-slate-700 dark:hover:bg-white/10 dark:hover:text-white"
                >
                  <LogOut className="h-3.5 w-3.5" />
                </button>
              </div>
            </div>
          )}
        </div>
      </aside>
    </>
  );
}
