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
  KeyRound,
} from 'lucide-react';
import type { NavGroup } from '../types/ui';

export const navigationGroups: NavGroup[] = [
  {
    label: 'Core HR',
    items: [
      { label: 'Executive Dashboard', icon: Gauge, path: '/dashboard', requiredPermissions: ['dashboard.read'] },
      { label: 'Employee Self-Service', icon: UserCircle2, path: '/ess', requiredPermissions: ['ess.read'] },
      { label: 'People', icon: UsersRound, path: '/people', requiredPermissions: ['employees.read'] },
      { label: 'Attendance', icon: Clock3, badge: 12, path: '/attendance', requiredPermissions: ['attendance.read', 'attendance.write', 'attendance.kiosk'] },
      { label: 'Leave Management', icon: ClipboardList, path: '/leave', requiredPermissions: ['leave.read', 'leave.write'] },
      { label: 'Shifts & Rosters', icon: CalendarCheck, path: '/shifts', requiredPermissions: ['attendance.read'] },
    ],
  },
  {
    label: 'Leave & Time',
    items: [
      { label: 'Overtime', icon: TimerReset, path: '/overtime', requiredPermissions: ['overtime.read', 'overtime.write'] },
    ],
  },
  {
    label: 'Finance',
    items: [
      { label: 'Payroll', icon: WalletCards, badge: 4, path: '/payroll', requiredPermissions: ['payroll.read'] },
      { label: 'Loans & Advances', icon: Landmark, path: '/loans', requiredPermissions: ['loans.read', 'loans.write'] },
    ],
  },
  {
    label: 'Talent',
    items: [
      { label: 'Recruitment', icon: BriefcaseBusiness, path: '/recruitment', requiredPermissions: ['recruitment.read', 'recruitment.write'] },
      { label: 'Performance & Appraisals', icon: BarChart3, path: '/performance', requiredPermissions: ['performance.read', 'performance.write'] },
      { label: 'Compliance & Contracts', icon: ShieldCheck, path: '/compliance', requiredPermissions: ['compliance.read', 'compliance.write'] },
      { label: 'Documents & Letters', icon: FileText, path: '/documents', requiredPermissions: ['employees.documents'] },
    ],
  },
  {
    label: 'Intelligence',
    items: [
      { label: 'AI HR Assistant', icon: Bot, path: '/ai-assistant' },
      { label: 'Reports & Analytics', icon: Layers3, path: '/reports', requiredPermissions: ['reports.read', 'reports.schedule'] },
    ],
  },
  {
    label: 'Service Desk',
    items: [
      { label: 'HR Request Center', icon: Headphones, path: '/hr-requests', requiredPermissions: ['approvals.read', 'approvals.write', 'approvals.decide'] },
    ],
  },
  {
    label: 'Admin',
    items: [
      { label: 'Approval Center', icon: ShieldCheck, badge: 18, path: '/approvals', requiredPermissions: ['approvals.read', 'approvals.decide'] },
      { label: 'User Management & Access Control', icon: KeyRound, path: '/user-management', requiredPermissions: ['users.manage', 'roles.manage', 'security.manage'] },
      { label: 'Tenant Administration', icon: Settings2, path: '/tenant-admin', requiredPermissions: ['security.manage'] },
      { label: 'Setup & Administration', icon: UserRoundCog, path: '/setup', requiredPermissions: ['organization.write'] },
    ],
  },
];

export const navigationItems = navigationGroups.flatMap((g) => g.items);
