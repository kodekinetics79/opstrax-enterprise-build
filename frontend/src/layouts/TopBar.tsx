import { Bell, Bot, LogOut, Menu, Moon, Sun, UserCircle2, X } from 'lucide-react';
import { useEffect, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { Avatar } from '../components/Avatar';
import { useAuth } from '../contexts/AuthContext';
import { notificationsApi } from '../api/notifications';
import type { NotificationItem } from '../api/notifications';
import type { ThemeMode } from '../types/ui';

interface TopBarProps {
  theme: ThemeMode;
  onToggleTheme: () => void;
  onOpenSidebar: () => void;
  onOpenSearch: () => void;
  onAskKynexOne: () => void;
}

function timeAgo(utc: string) {
  const diff = Math.floor((Date.now() - new Date(utc).getTime()) / 1000);
  if (diff < 60) return 'just now';
  if (diff < 3600) return `${Math.floor(diff / 60)}m ago`;
  if (diff < 86400) return `${Math.floor(diff / 3600)}h ago`;
  return `${Math.floor(diff / 86400)}d ago`;
}

function NotificationPanel({ onClose }: { onClose: () => void }) {
  const [items, setItems] = useState<NotificationItem[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    notificationsApi.list().then(setItems).catch(() => {}).finally(() => setLoading(false));
  }, []);

  const markRead = async (id: string) => {
    await notificationsApi.markRead(id).catch(() => {});
    setItems((prev) => prev.map((n) => n.id === id ? { ...n, status: 'Read' } : n));
  };

  const unread = items.filter((n) => n.status === 'Unread');

  return (
    <div className="absolute right-0 top-full z-50 mt-2 w-80 rounded-xl border border-slate-200 bg-white shadow-2xl dark:border-white/10 dark:bg-[#0D1221]">
      <div className="flex items-center justify-between border-b border-slate-100 px-4 py-3 dark:border-white/[0.07]">
        <div className="flex items-center gap-2">
          <p className="text-sm font-semibold text-slate-900 dark:text-white">Notifications</p>
          {unread.length > 0 && (
            <span className="rounded-full bg-sapphire px-1.5 py-0.5 text-[10px] font-bold text-white">{unread.length}</span>
          )}
        </div>
        <button type="button" onClick={onClose} className="grid h-6 w-6 place-items-center rounded text-slate-400 hover:bg-slate-100 dark:hover:bg-white/10">
          <X className="h-3.5 w-3.5" />
        </button>
      </div>

      <div className="max-h-80 overflow-y-auto">
        {loading && <div className="flex justify-center py-8"><div className="h-5 w-5 animate-spin rounded-full border-2 border-sapphire border-t-transparent" /></div>}
        {!loading && items.length === 0 && <p className="py-8 text-center text-sm text-slate-400 dark:text-slate-500">No notifications</p>}
        {!loading && items.map((n) => (
          <div
            key={n.id}
            className={`flex gap-3 px-4 py-3 transition-colors hover:bg-slate-50 dark:hover:bg-white/[0.03] ${n.status === 'Unread' ? 'bg-sapphire/[0.03] dark:bg-sapphire/[0.06]' : ''}`}
          >
            <div className="mt-0.5 flex-1">
              <p className="text-xs font-semibold text-slate-900 dark:text-white">{n.title}</p>
              <p className="mt-0.5 text-xs leading-relaxed text-slate-500 dark:text-slate-400">{n.message}</p>
              <p className="mt-1 text-[10px] text-slate-400 dark:text-slate-500">{timeAgo(n.createdAtUtc)}</p>
            </div>
            {n.status === 'Unread' && (
              <button type="button" onClick={() => markRead(n.id)} title="Mark as read" aria-label="Mark as read" className="mt-1 flex h-5 w-5 shrink-0 items-center justify-center rounded-full bg-sapphire/10 text-sapphire hover:bg-sapphire/20">
                <span className="h-1.5 w-1.5 rounded-full bg-sapphire" />
              </button>
            )}
          </div>
        ))}
      </div>
    </div>
  );
}

export function TopBar({ theme, onToggleTheme, onOpenSidebar, onOpenSearch, onAskKynexOne }: TopBarProps) {
  const ThemeIcon = theme === 'dark' ? Sun : Moon;
  const { user, logout } = useAuth();
  const navigate = useNavigate();
  const [notifOpen, setNotifOpen] = useState(false);
  const [userMenuOpen, setUserMenuOpen] = useState(false);
  const [unreadCount, setUnreadCount] = useState(0);
  const bellRef = useRef<HTMLDivElement>(null);
  const userRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!userMenuOpen) return;
    const handler = (e: MouseEvent) => {
      if (userRef.current && !userRef.current.contains(e.target as Node)) setUserMenuOpen(false);
    };
    document.addEventListener('mousedown', handler);
    return () => document.removeEventListener('mousedown', handler);
  }, [userMenuOpen]);

  useEffect(() => {
    notificationsApi.list()
      .then((items) => setUnreadCount(items.filter((n) => n.status === 'Unread').length))
      .catch(() => {});
  }, [notifOpen]);

  useEffect(() => {
    if (!notifOpen) return;
    const handler = (e: MouseEvent) => {
      if (bellRef.current && !bellRef.current.contains(e.target as Node)) setNotifOpen(false);
    };
    document.addEventListener('mousedown', handler);
    return () => document.removeEventListener('mousedown', handler);
  }, [notifOpen]);

  return (
    <header className="sticky top-0 z-20 flex h-[60px] items-center gap-3 border-b border-slate-200 bg-white/90 px-4 backdrop-blur-xl dark:border-white/[0.07] dark:bg-[#0D1221]/90 sm:px-5">
      <button
        type="button"
        aria-label="Open navigation"
        onClick={onOpenSidebar}
        className="grid h-8 w-8 place-items-center rounded-lg border border-slate-200 text-slate-600 transition hover:bg-slate-50 lg:hidden dark:border-white/10 dark:text-slate-300 dark:hover:bg-white/10"
      >
        <Menu className="h-4 w-4" />
      </button>

      <button
        type="button"
        aria-label="Open command search"
        onClick={onOpenSearch}
        className="flex h-8 max-w-md flex-1 items-center gap-2.5 rounded-lg border border-slate-200 bg-slate-50/80 px-3 text-left text-sm transition hover:border-slate-300 hover:bg-white dark:border-white/[0.08] dark:bg-white/[0.04] dark:hover:bg-white/[0.07]"
      >
        <svg className="h-3.5 w-3.5 shrink-0 text-slate-400" viewBox="0 0 16 16" fill="none" aria-hidden="true">
          <circle cx="6.5" cy="6.5" r="4.5" stroke="currentColor" strokeWidth="1.5" />
          <path d="M10.5 10.5L13.5 13.5" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" />
        </svg>
        <span className="flex-1 truncate text-slate-400 dark:text-slate-500">Search employees, payroll, approvals…</span>
        <kbd className="hidden rounded-md border border-slate-200 bg-white px-1.5 py-0.5 font-mono text-[10px] text-slate-400 sm:inline dark:border-white/10 dark:bg-white/[0.06] dark:text-slate-500">⌘K</kbd>
      </button>

      <div className="ml-auto flex shrink-0 items-center gap-1.5">
        <button
          type="button"
          aria-label="Ask KynexOne AI"
          onClick={onAskKynexOne}
          className="hidden h-8 items-center gap-1.5 rounded-lg border border-sapphire/25 bg-sapphire/[0.07] px-3 text-xs font-semibold text-sapphire transition hover:bg-sapphire/[0.12] sm:flex dark:border-cyanAccent/20 dark:bg-cyanAccent/[0.07] dark:text-cyanAccent dark:hover:bg-cyanAccent/[0.12]"
        >
          <Bot className="h-3.5 w-3.5" />
          Ask KynexOne
        </button>

        <button
          type="button"
          aria-label={theme === 'dark' ? 'Switch to light theme' : 'Switch to dark theme'}
          onClick={onToggleTheme}
          className="grid h-8 w-8 place-items-center rounded-lg border border-slate-200 text-slate-500 transition hover:bg-slate-50 hover:text-slate-900 dark:border-white/10 dark:text-slate-400 dark:hover:bg-white/10 dark:hover:text-white"
        >
          <ThemeIcon className="h-4 w-4" />
        </button>

        {/* Notification bell with dropdown */}
        <div ref={bellRef} className="relative">
          <button
            type="button"
            aria-label="Notifications"
            onClick={() => setNotifOpen((o) => !o)}
            className="relative grid h-8 w-8 place-items-center rounded-lg border border-slate-200 text-slate-500 transition hover:bg-slate-50 hover:text-slate-900 dark:border-white/10 dark:text-slate-400 dark:hover:bg-white/10 dark:hover:text-white"
          >
            <Bell className="h-4 w-4" />
            {unreadCount > 0 && (
              <span className="absolute right-1.5 top-1.5 h-2 w-2 rounded-full bg-rose-500 ring-2 ring-white dark:ring-[#0D1221]" />
            )}
          </button>
          {notifOpen && <NotificationPanel onClose={() => setNotifOpen(false)} />}
        </div>

        <div ref={userRef} className="relative">
          <button
            type="button"
            aria-label="Open user menu"
            onClick={() => setUserMenuOpen((o) => !o)}
            className="hidden h-8 items-center gap-2 rounded-lg border border-slate-200 bg-slate-50/80 pl-1 pr-3 transition hover:border-slate-300 hover:bg-white md:flex dark:border-white/[0.08] dark:bg-white/[0.04] dark:hover:bg-white/[0.07]"
          >
            <Avatar name={user?.fullName ?? 'User'} size="xs" />
            <div className="text-left">
              <p className="text-xs font-semibold leading-tight text-slate-900 dark:text-white">{user?.fullName ?? 'User'}</p>
              <p className="text-[10px] leading-tight text-slate-500 dark:text-slate-400">{user?.roles[0] ?? 'Member'}</p>
            </div>
          </button>
          {userMenuOpen && (
            <div className="animate-fade-in absolute right-0 top-full z-50 mt-2 w-52 overflow-hidden rounded-xl border border-slate-200 bg-white shadow-2xl dark:border-white/10 dark:bg-[#0D1221]">
              <div className="border-b border-slate-100 px-4 py-3 dark:border-white/[0.07]">
                <p className="truncate text-sm font-semibold text-slate-900 dark:text-white">{user?.fullName ?? 'User'}</p>
                <p className="truncate text-[11px] text-slate-500 dark:text-slate-400">{user?.email ?? ''}</p>
              </div>
              <button
                type="button"
                onClick={() => { setUserMenuOpen(false); navigate('/ess'); }}
                className="flex w-full items-center gap-2.5 px-4 py-2.5 text-left text-sm text-slate-700 transition hover:bg-slate-50 dark:text-slate-300 dark:hover:bg-white/[0.04]"
              >
                <UserCircle2 className="h-4 w-4 text-slate-400" /> My Profile
              </button>
              <button
                type="button"
                onClick={async () => { setUserMenuOpen(false); await logout(); navigate('/login', { replace: true }); }}
                className="flex w-full items-center gap-2.5 border-t border-slate-100 px-4 py-2.5 text-left text-sm text-rose-600 transition hover:bg-rose-50 dark:border-white/[0.07] dark:text-rose-400 dark:hover:bg-rose-500/10"
              >
                <LogOut className="h-4 w-4" /> Sign out
              </button>
            </div>
          )}
        </div>
      </div>
    </header>
  );
}
