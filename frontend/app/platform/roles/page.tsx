'use client';

import { Shield } from 'lucide-react';
import { PLATFORM_ROLES } from '@/src/api/platform';

// Actual RBAC matrix — mirrors RequirePlatformRoleAttribute on PlatformController
const MATRIX: { category: string; actions: { label: string; roles: string[] }[] }[] = [
  {
    category: 'Dashboard & Stats',
    actions: [
      { label: 'View stats / metrics', roles: ['Owner', 'Admin', 'Finance', 'Support', 'Auditor'] },
      { label: 'System health', roles: ['Owner', 'Admin', 'Support', 'Auditor'] },
    ],
  },
  {
    category: 'Tenant Management',
    actions: [
      { label: 'List / view tenants', roles: ['Owner', 'Admin', 'Finance', 'Support', 'Auditor'] },
      { label: 'Create tenant', roles: ['Owner', 'Admin'] },
      { label: 'Edit tenant name', roles: ['Owner', 'Admin'] },
      { label: 'Suspend / reactivate', roles: ['Owner', 'Admin'] },
      { label: 'Toggle feature flags', roles: ['Owner', 'Admin'] },
      { label: 'Impersonate user', roles: ['Owner', 'Admin', 'Support'] },
    ],
  },
  {
    category: 'Billing & Invoices',
    actions: [
      { label: 'View subscription', roles: ['Owner', 'Admin', 'Finance'] },
      { label: 'Update subscription', roles: ['Owner', 'Admin', 'Finance'] },
      { label: 'View / create invoices', roles: ['Owner', 'Admin', 'Finance'] },
      { label: 'Download PDF / send email', roles: ['Owner', 'Admin', 'Finance'] },
      { label: 'Delete invoice', roles: ['Owner', 'Admin', 'Finance'] },
      { label: 'View / set plan pricing', roles: ['Owner', 'Admin', 'Finance', 'Auditor'] },
      { label: 'Update plan pricing', roles: ['Owner', 'Admin', 'Finance'] },
      { label: 'Billing summary', roles: ['Owner', 'Admin', 'Finance'] },
    ],
  },
  {
    category: 'Platform Team',
    actions: [
      { label: 'View team members', roles: ['Owner', 'Admin', 'Support'] },
      { label: 'Invite team member', roles: ['Owner', 'Admin'] },
      { label: 'Change role', roles: ['Owner', 'Admin'] },
      { label: 'Deactivate member', roles: ['Owner'] },
    ],
  },
  {
    category: 'Support & Users',
    actions: [
      { label: 'View tenant users', roles: ['Owner', 'Admin', 'Support'] },
      { label: 'View tenant admins', roles: ['Owner', 'Admin', 'Support'] },
      { label: 'Edit user profile (name, email, status, role)', roles: ['Owner', 'Admin'] },
      { label: 'Send password reset email', roles: ['Owner', 'Admin', 'Support'] },
      { label: 'Force password reset (set temp password)', roles: ['Owner', 'Admin'] },
      { label: 'Unlock locked account', roles: ['Owner', 'Admin', 'Support'] },
      { label: 'Disable MFA for user', roles: ['Owner', 'Admin'] },
      { label: 'Revoke all sessions for user', roles: ['Owner', 'Admin', 'Support'] },
      { label: 'Start support session', roles: ['Owner', 'Admin', 'Support'] },
      { label: 'End support session', roles: ['Owner', 'Admin', 'Support'] },
      { label: 'View support sessions', roles: ['Owner', 'Admin', 'Support', 'Auditor'] },
    ],
  },
  {
    category: 'Branding & Localization',
    actions: [
      { label: 'Update tenant branding (logo, colors, title)', roles: ['Owner', 'Admin'] },
      { label: 'Update tenant localization (language, timezone, calendar)', roles: ['Owner', 'Admin'] },
      { label: 'Delete / deactivate tenant', roles: ['Owner'] },
      { label: 'List tenant roles', roles: ['Owner', 'Admin', 'Support'] },
    ],
  },
  {
    category: 'Audit & Compliance',
    actions: [
      { label: 'View audit logs', roles: ['Owner', 'Admin', 'Support', 'Auditor'] },
      { label: 'View AI usage', roles: ['Owner', 'Admin', 'Auditor'] },
      { label: 'View app version', roles: ['Owner', 'Admin', 'Auditor'] },
    ],
  },
  {
    category: 'Marketing & Leads',
    actions: [
      { label: 'View announcements', roles: ['Owner', 'Admin', 'Marketing'] },
      { label: 'Create / edit / publish', roles: ['Owner', 'Admin', 'Marketing'] },
      { label: 'Archive announcement', roles: ['Owner', 'Admin', 'Marketing'] },
      { label: 'View leads', roles: ['Owner', 'Admin', 'Marketing'] },
      { label: 'Create / advance leads', roles: ['Owner', 'Admin', 'Marketing'] },
      { label: 'Convert lead to tenant', roles: ['Owner', 'Admin', 'Marketing'] },
    ],
  },
  {
    category: 'Settings',
    actions: [
      { label: 'View SMTP / platform settings', roles: ['Owner', 'Admin'] },
      { label: 'Update SMTP settings', roles: ['Owner', 'Admin'] },
      { label: 'Send test email', roles: ['Owner', 'Admin'] },
    ],
  },
];

const ROLE_CLS: Record<string, string> = {
  Owner:     'bg-amber-500/20 text-amber-300',
  Admin:     'bg-blue-500/20 text-blue-300',
  Finance:   'bg-emerald-500/20 text-emerald-300',
  Support:   'bg-cyan-500/20 text-cyan-300',
  Marketing: 'bg-purple-500/20 text-purple-300',
  Auditor:   'bg-slate-600/50 text-slate-400',
};

export default function PlatformRolesPage() {
  return (
    <div className="space-y-5">
      <div>
        <h1 className="text-lg font-bold text-white">Roles & Permissions</h1>
        <p className="text-xs text-slate-500 mt-0.5">
          Enforced by <span className="font-mono text-slate-400">RequirePlatformRoleAttribute</span> on every API endpoint.
          The frontend matches backend enforcement — no role can call an API it is not listed for.
        </p>
      </div>

      {/* Legend */}
      <div className="flex flex-wrap gap-2">
        {PLATFORM_ROLES.map(role => (
          <div key={role} className={`flex items-center gap-1.5 px-2.5 py-1 rounded-full text-xs font-semibold ${ROLE_CLS[role] ?? ''}`}>
            <Shield className="h-3 w-3" />
            {role}
          </div>
        ))}
      </div>

      {/* Matrix */}
      <div className="space-y-4">
        {MATRIX.map(section => (
          <div key={section.category} className="bg-[#161b22] border border-white/[0.07] rounded-xl overflow-hidden">
            <div className="px-4 py-2.5 bg-white/[0.02] border-b border-white/[0.06]">
              <p className="text-[11px] font-semibold text-slate-500 uppercase tracking-widest">{section.category}</p>
            </div>
            <div className="divide-y divide-white/[0.04]">
              {section.actions.map(action => (
                <div key={action.label} className="flex items-center gap-4 px-4 py-2.5">
                  <span className="text-sm text-slate-300 flex-1 min-w-0">{action.label}</span>
                  <div className="flex flex-wrap gap-1 justify-end">
                    {action.roles.map(role => (
                      <span key={role} className={`text-[10px] font-semibold px-1.5 py-0.5 rounded ${ROLE_CLS[role] ?? 'text-slate-400 bg-slate-700/50'}`}>
                        {role}
                      </span>
                    ))}
                  </div>
                </div>
              ))}
            </div>
          </div>
        ))}
      </div>

      <p className="text-[11px] text-slate-700">
        Owner is always allowed regardless of the roles listed. Role is read from the <span className="font-mono">platform_role</span> JWT claim issued at login.
      </p>
    </div>
  );
}
