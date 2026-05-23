import {
  BadgeDollarSign,
  CalendarPlus,
  FileCheck2,
  PlaneTakeoff,
  UserPlus,
} from 'lucide-react';
import type { AiInsight, ApprovalQueueItem, KpiMetric, QuickAction } from '../../types/ui';

export const kpiMetrics: KpiMetric[] = [
  { label: 'Active workforce', value: '8,426', delta: '+4.8% vs last month', tone: 'blue', trend: 'up' },
  { label: 'Present today', value: '7,918', delta: '94.0% attendance rate', tone: 'emerald', trend: 'up' },
  { label: 'Pending approvals', value: '18', delta: '6 high priority', tone: 'amber', trend: 'down' },
  { label: 'Payroll readiness', value: '96%', delta: 'AED 18.4M forecast', tone: 'cyan', trend: 'up' },
];

export const quickActions: QuickAction[] = [
  { label: 'Add employee', description: 'Start GCC onboarding flow', icon: UserPlus },
  { label: 'Run payroll checks', description: 'Validate WPS and exceptions', icon: BadgeDollarSign },
  { label: 'Create roster', description: 'Plan branch coverage', icon: CalendarPlus },
  { label: 'Issue letter', description: 'Generate bilingual document', icon: FileCheck2 },
];

export const approvalQueue: ApprovalQueueItem[] = [
  { id: 'APR-1042', title: 'Final settlement approval', owner: 'Maha Al Nuaimi', module: 'Payroll', due: 'Today', priority: 'High' },
  { id: 'APR-1038', title: 'Overtime exception batch', owner: 'Riyadh Ops', module: 'Overtime', due: 'Today', priority: 'High' },
  { id: 'APR-1031', title: 'New joiner offer letter', owner: 'Sara Ahmed', module: 'People', due: 'Tomorrow', priority: 'Medium' },
  { id: 'APR-1024', title: 'Annual leave escalation', owner: 'Doha Retail', module: 'Leave', due: 'May 26', priority: 'Low' },
];

export const attendanceByHour = [
  { hour: '06:00', present: 1180, late: 42 },
  { hour: '08:00', present: 4260, late: 118 },
  { hour: '10:00', present: 7620, late: 146 },
  { hour: '12:00', present: 7918, late: 151 },
  { hour: '14:00', present: 7890, late: 151 },
  { hour: '16:00', present: 7710, late: 144 },
];

export const payrollSummary = [
  { label: 'Gross payroll', value: 'AED 18.4M' },
  { label: 'WPS ready', value: '96%' },
  { label: 'Exceptions', value: '23' },
  { label: 'Cutoff date', value: 'May 28' },
];

export const attendanceSummary = [
  { label: 'Clocked in', value: '7,918' },
  { label: 'Late arrivals', value: '151' },
  { label: 'On leave', value: '322' },
  { label: 'Unassigned shifts', value: '37' },
];

export const aiInsights: AiInsight[] = [
  {
    title: 'Visa expiry risk',
    body: '42 employees have visa or residency documents expiring within 60 days across UAE and KSA entities.',
    severity: 'warning',
  },
  {
    title: 'Payroll anomaly',
    body: 'WPS validation found 11 missing IBANs and 7 employees with allowance changes above the policy threshold.',
    severity: 'critical',
  },
  {
    title: 'Attendance pattern',
    body: 'Late arrivals are concentrated in Dubai Logistics between 08:00–09:00 for the third consecutive week.',
    severity: 'info',
  },
];

export const workforceMix = [
  { name: 'UAE', value: 42 },
  { name: 'KSA', value: 31 },
  { name: 'Qatar', value: 16 },
  { name: 'Oman', value: 11 },
];

export const selfServiceItems = [
  { label: 'Leave requests', value: '214 open' },
  { label: 'Letter requests', value: '38 pending' },
  { label: 'Profile updates', value: '61 awaiting HR' },
];

export const alertItems = [
  { label: 'Payroll cutoff in 3 days', tone: 'amber' as const },
  { label: '6 high priority approvals', tone: 'rose' as const },
  { label: 'Branch coverage below 85%', tone: 'amber' as const },
];

export const featuredAction = {
  label: 'Open travel readiness',
  description: 'Review employees blocked by document expiry before Eid travel.',
  icon: PlaneTakeoff,
};

export const payrollByEntity = [
  { entity: 'UAE', amount: 8.4 },
  { entity: 'KSA', amount: 5.9 },
  { entity: 'Qatar', amount: 2.6 },
  { entity: 'Oman', amount: 1.5 },
];
