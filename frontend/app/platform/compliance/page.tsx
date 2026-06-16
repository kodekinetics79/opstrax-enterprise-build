'use client';

import { ComingSoon } from '@/src/components/platform/ComingSoon';

export default function CompliancePage() {
  return (
    <div className="space-y-5">
      <h1 className="text-lg font-bold text-white">Compliance Center</h1>
      <ComingSoon
        title="Compliance & Regulatory"
        description="Ensure tenants meet regulatory requirements. Track data residency, DPA signing, retention policies, and labor law adherence across jurisdictions."
        badge="todo"
        apis={[
          { method: 'GET', path: '/api/platform/compliance/profiles',     note: 'Compliance status per tenant' },
          { method: 'PUT', path: '/api/platform/compliance/profiles/:id', note: 'Update status / record waiver' },
          { method: 'GET', path: '/api/platform/compliance/reports',      note: 'Export evidence bundle' },
        ]}
        plannedFeatures={[
          'Compliance checklist per tenant: DPA signed, data residency set, retention policy active',
          'Regulatory framework tags: GDPR, PDPA, Saudi Labor Law, ZATCA, SOC 2',
          'Data retention policy config per tenant (30 / 90 / 365 / custom days)',
          'Audit evidence export for external auditors (PDF bundle)',
          'Non-compliance alerts surfaced in Attention Queue on dashboard',
        ]}
      />
    </div>
  );
}
