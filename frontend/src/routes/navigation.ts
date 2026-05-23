import {
  BarChart3,
  Bot,
  BriefcaseBusiness,
  CalendarCheck,
  ClipboardList,
  Clock3,
  FileText,
  Gauge,
  Headphones,
  Landmark,
  Layers3,
  Settings2,
  ShieldCheck,
  TimerReset,
  UserCircle2,
  UserRoundCog,
  UsersRound,
  WalletCards,
} from 'lucide-react';
import type { NavGroup } from '../types/ui';

export const navigationGroups: NavGroup[] = [
  {
    label: 'Core HR',
    items: [
      { label: 'Executive Dashboard', icon: Gauge, path: '/dashboard' },
      { label: 'Employee Self-Service', icon: UserCircle2, path: '/ess' },
      { label: 'People', icon: UsersRound, path: '/people' },
      { label: 'Attendance', icon: Clock3, badge: 12, path: '/attendance' },
      { label: 'Leave Management', icon: ClipboardList, path: '/leave' },
      { label: 'Shifts & Rosters', icon: CalendarCheck, path: '/shifts' },
    ],
  },
  {
    label: 'Leave & Time',
    items: [
      { label: 'Overtime', icon: TimerReset, path: '/overtime' },
    ],
  },
  {
    label: 'Finance',
    items: [
      { label: 'Payroll', icon: WalletCards, badge: 4, path: '/payroll' },
      { label: 'Loans & Advances', icon: Landmark, path: '/loans' },
    ],
  },
  {
    label: 'Talent',
    items: [
      { label: 'Recruitment', icon: BriefcaseBusiness, path: '/recruitment' },
      { label: 'Performance & Appraisals', icon: BarChart3, path: '/performance' },
      { label: 'Compliance & Contracts', icon: ShieldCheck, path: '/compliance' },
      { label: 'Documents & Letters', icon: FileText, path: '/documents' },
    ],
  },
  {
    label: 'Intelligence',
    items: [
      { label: 'AI HR Assistant', icon: Bot, path: '/ai-assistant' },
      { label: 'Reports & Analytics', icon: Layers3, path: '/reports' },
    ],
  },
  {
    label: 'Service Desk',
    items: [
      { label: 'HR Request Center', icon: Headphones, path: '/hr-requests' },
    ],
  },
  {
    label: 'Admin',
    items: [
      { label: 'Approval Center', icon: ShieldCheck, badge: 18, path: '/approvals' },
      { label: 'Tenant Administration', icon: Settings2, path: '/tenant-admin' },
      { label: 'Setup & Administration', icon: UserRoundCog, path: '/setup' },
    ],
  },
];

export const navigationItems = navigationGroups.flatMap((g) => g.items);
