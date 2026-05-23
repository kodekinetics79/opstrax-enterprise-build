import { useCallback, useEffect, useState } from 'react';
import { Award, Building2, GitBranch, Layers, Landmark, Tag, Plus, Pencil } from 'lucide-react';
import {
  companiesApi,
  branchesApi,
  departmentsApi,
  designationsApi,
  gradesApi,
  costCentersApi,
} from '../api/organization';
import type {
  CompanyDto,
  CompanyRequest,
  BranchDto,
  BranchRequest,
  DepartmentDto,
  DepartmentRequest,
  DesignationDto,
  DesignationRequest,
  GradeDto,
  GradeRequest,
  CostCenterDto,
  CostCenterRequest,
} from '../api/organization';
import { Modal } from '../components/Modal';

type Tab = 'companies' | 'branches' | 'departments' | 'designations' | 'grades' | 'costCenters';

const tabs: { id: Tab; label: string; icon: React.ElementType }[] = [
  { id: 'companies', label: 'Companies', icon: Building2 },
  { id: 'branches', label: 'Branches', icon: GitBranch },
  { id: 'departments', label: 'Departments', icon: Layers },
  { id: 'designations', label: 'Designations', icon: Tag },
  { id: 'grades', label: 'Grades', icon: Award },
  { id: 'costCenters', label: 'Cost Centers', icon: Landmark },
];

// ─── Companies ───────────────────────────────────────────────────────────────

function CompaniesTab() {
  const [items, setItems] = useState<CompanyDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [modalOpen, setModalOpen] = useState(false);
  const [editing, setEditing] = useState<CompanyDto | null>(null);
  const [form, setForm] = useState<CompanyRequest>(emptyCompany());
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  const load = useCallback(async () => {
    setLoading(true);
    try { const r = await companiesApi.list(); setItems(r.items); } catch { /**/ }
    finally { setLoading(false); }
  }, []);

  useEffect(() => { load(); }, [load]);

  const openNew = () => { setEditing(null); setForm(emptyCompany()); setError(''); setModalOpen(true); };
  const openEdit = (c: CompanyDto) => {
    setEditing(c);
    setForm({ legalNameEn: c.legalNameEn, legalNameAr: c.legalNameAr, tradeName: c.tradeName, countryCode: c.countryCode, registrationNumber: c.registrationNumber, taxNumber: c.taxNumber, wpsEmployerId: c.wpsEmployerId, gosiEmployerId: c.gosiEmployerId, qiwaEstablishmentId: c.qiwaEstablishmentId, defaultCurrency: c.defaultCurrency, isActive: c.isActive });
    setError('');
    setModalOpen(true);
  };

  const handleSave = async () => {
    if (!form.legalNameEn.trim()) { setError('Legal name (English) is required'); return; }
    setSaving(true); setError('');
    try {
      if (editing) await companiesApi.update(editing.id, form);
      else await companiesApi.create(form);
      setModalOpen(false);
      load();
    } catch { setError('Failed to save. Please try again.'); }
    finally { setSaving(false); }
  };

  const f = (key: keyof CompanyRequest, v: string | boolean) => setForm((x) => ({ ...x, [key]: v }));

  return (
    <>
      <TableShell
        columns={['Legal Name', 'Trade Name', 'Country', 'Currency', 'Status']}
        onAdd={openNew}
        addLabel="Add Company"
        loading={loading}
        empty={items.length === 0}
        emptyLabel="No companies yet"
      >
        {items.map((c) => (
          <tr key={c.id} className="group hover:bg-slate-50 dark:hover:bg-white/[0.03]">
            <td className="px-4 py-3 font-medium text-slate-900 dark:text-white">{c.legalNameEn}</td>
            <td className="px-4 py-3 text-slate-600 dark:text-slate-300">{c.tradeName || '—'}</td>
            <td className="px-4 py-3 text-slate-600 dark:text-slate-300">{c.countryCode || '—'}</td>
            <td className="px-4 py-3 text-slate-600 dark:text-slate-300">{c.defaultCurrency}</td>
            <td className="px-4 py-3">
              <ActiveBadge active={c.isActive} />
            </td>
            <td className="px-4 py-3">
              <button type="button" onClick={() => openEdit(c)} className="btn-secondary h-7 px-2 text-xs opacity-0 group-hover:opacity-100">
                <Pencil className="h-3 w-3" /> Edit
              </button>
            </td>
          </tr>
        ))}
      </TableShell>

      <Modal
        isOpen={modalOpen}
        title={editing ? 'Edit Company' : 'Add Company'}
        onClose={() => setModalOpen(false)}
        size="lg"
        footer={
          <>
            <button type="button" onClick={() => setModalOpen(false)} className="btn-secondary">Cancel</button>
            <button type="button" onClick={handleSave} disabled={saving} className="btn-primary disabled:opacity-60">{saving ? 'Saving…' : 'Save'}</button>
          </>
        }
      >
        <FormError error={error} />
        <div className="grid grid-cols-2 gap-3">
          <FormField label="Legal Name (EN)" required>
            <input type="text" value={form.legalNameEn} onChange={(e) => f('legalNameEn', e.target.value)} className="input w-full" placeholder="Acme Corp LLC" />
          </FormField>
          <FormField label="Legal Name (AR)">
            <input type="text" value={form.legalNameAr ?? ''} onChange={(e) => f('legalNameAr', e.target.value)} className="input w-full" dir="rtl" />
          </FormField>
          <FormField label="Trade Name">
            <input type="text" value={form.tradeName ?? ''} onChange={(e) => f('tradeName', e.target.value)} className="input w-full" />
          </FormField>
          <FormField label="Country Code" required>
            <input type="text" value={form.countryCode} onChange={(e) => f('countryCode', e.target.value)} className="input w-full" placeholder="AE" maxLength={10} />
          </FormField>
          <FormField label="Registration Number" required>
            <input type="text" value={form.registrationNumber} onChange={(e) => f('registrationNumber', e.target.value)} className="input w-full" />
          </FormField>
          <FormField label="Tax Number">
            <input type="text" value={form.taxNumber ?? ''} onChange={(e) => f('taxNumber', e.target.value)} className="input w-full" />
          </FormField>
          <FormField label="Default Currency" required>
            <select value={form.defaultCurrency} onChange={(e) => f('defaultCurrency', e.target.value)} className="select w-full">
              {['AED', 'SAR', 'QAR', 'KWD', 'BHD', 'OMR', 'USD'].map((c) => <option key={c}>{c}</option>)}
            </select>
          </FormField>
          <FormField label="Status">
            <select value={form.isActive ? 'true' : 'false'} onChange={(e) => f('isActive', e.target.value === 'true')} className="select w-full">
              <option value="true">Active</option>
              <option value="false">Inactive</option>
            </select>
          </FormField>
          <FormField label="WPS Employer ID">
            <input type="text" value={form.wpsEmployerId ?? ''} onChange={(e) => f('wpsEmployerId', e.target.value)} className="input w-full" />
          </FormField>
          <FormField label="GOSI Employer ID">
            <input type="text" value={form.gosiEmployerId ?? ''} onChange={(e) => f('gosiEmployerId', e.target.value)} className="input w-full" />
          </FormField>
        </div>
      </Modal>
    </>
  );
}

// ─── Branches ────────────────────────────────────────────────────────────────

function BranchesTab({ companies }: { companies: CompanyDto[] }) {
  const [items, setItems] = useState<BranchDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [filterCompany, setFilterCompany] = useState('');
  const [modalOpen, setModalOpen] = useState(false);
  const [editing, setEditing] = useState<BranchDto | null>(null);
  const [form, setForm] = useState<BranchRequest>(emptyBranch(companies[0]?.id ?? ''));
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  const load = useCallback(async () => {
    setLoading(true);
    try { const r = await branchesApi.list(filterCompany || undefined); setItems(r.items); } catch { /**/ }
    finally { setLoading(false); }
  }, [filterCompany]);

  useEffect(() => { load(); }, [load]);

  const openNew = () => { setEditing(null); setForm(emptyBranch(companies[0]?.id ?? '')); setError(''); setModalOpen(true); };
  const openEdit = (b: BranchDto) => {
    setEditing(b);
    setForm({ companyId: b.companyId, code: b.code, nameEn: b.nameEn, nameAr: b.nameAr, countryCode: b.countryCode, city: b.city, addressLine1: b.addressLine1, addressLine2: b.addressLine2, timeZoneId: b.timeZoneId, laborOfficeCode: b.laborOfficeCode, isHeadOffice: b.isHeadOffice, isActive: b.isActive });
    setError(''); setModalOpen(true);
  };

  const handleSave = async () => {
    if (!form.nameEn.trim() || !form.code.trim()) { setError('Name and code are required'); return; }
    setSaving(true); setError('');
    try {
      if (editing) await branchesApi.update(editing.id, form);
      else await branchesApi.create(form);
      setModalOpen(false); load();
    } catch { setError('Failed to save. Please try again.'); }
    finally { setSaving(false); }
  };

  const f = (key: keyof BranchRequest, v: string | boolean) => setForm((x) => ({ ...x, [key]: v }));

  const companyName = (id: string) => companies.find((c) => c.id === id)?.legalNameEn ?? id;

  return (
    <>
      <TableShell
        columns={['Code', 'Name', 'Company', 'City', 'Head Office', 'Status']}
        onAdd={openNew}
        addLabel="Add Branch"
        loading={loading}
        empty={items.length === 0}
        emptyLabel="No branches yet"
        filter={
          companies.length > 0 ? (
            <select value={filterCompany} onChange={(e) => setFilterCompany(e.target.value)} className="select">
              <option value="">All Companies</option>
              {companies.map((c) => <option key={c.id} value={c.id}>{c.legalNameEn}</option>)}
            </select>
          ) : undefined
        }
      >
        {items.map((b) => (
          <tr key={b.id} className="group hover:bg-slate-50 dark:hover:bg-white/[0.03]">
            <td className="px-4 py-3 font-mono text-xs text-slate-500 dark:text-slate-400">{b.code}</td>
            <td className="px-4 py-3 font-medium text-slate-900 dark:text-white">{b.nameEn}</td>
            <td className="px-4 py-3 text-slate-600 dark:text-slate-300">{companyName(b.companyId)}</td>
            <td className="px-4 py-3 text-slate-600 dark:text-slate-300">{b.city || '—'}</td>
            <td className="px-4 py-3">{b.isHeadOffice ? <span className="rounded-full bg-sapphire/10 px-2 py-0.5 text-xs font-semibold text-sapphire dark:bg-sapphire/20">HQ</span> : '—'}</td>
            <td className="px-4 py-3"><ActiveBadge active={b.isActive} /></td>
            <td className="px-4 py-3">
              <button type="button" onClick={() => openEdit(b)} className="btn-secondary h-7 px-2 text-xs opacity-0 group-hover:opacity-100">
                <Pencil className="h-3 w-3" /> Edit
              </button>
            </td>
          </tr>
        ))}
      </TableShell>

      <Modal
        isOpen={modalOpen}
        title={editing ? 'Edit Branch' : 'Add Branch'}
        onClose={() => setModalOpen(false)}
        size="lg"
        footer={
          <>
            <button type="button" onClick={() => setModalOpen(false)} className="btn-secondary">Cancel</button>
            <button type="button" onClick={handleSave} disabled={saving} className="btn-primary disabled:opacity-60">{saving ? 'Saving…' : 'Save'}</button>
          </>
        }
      >
        <FormError error={error} />
        <div className="grid grid-cols-2 gap-3">
          <FormField label="Company" required>
            <select value={form.companyId} onChange={(e) => f('companyId', e.target.value)} className="select w-full">
              <option value="">Select company</option>
              {companies.map((c) => <option key={c.id} value={c.id}>{c.legalNameEn}</option>)}
            </select>
          </FormField>
          <FormField label="Code" required>
            <input type="text" value={form.code} onChange={(e) => f('code', e.target.value)} className="input w-full" placeholder="DXB-01" />
          </FormField>
          <FormField label="Name (EN)" required>
            <input type="text" value={form.nameEn} onChange={(e) => f('nameEn', e.target.value)} className="input w-full" placeholder="Dubai Head Office" />
          </FormField>
          <FormField label="Name (AR)">
            <input type="text" value={form.nameAr ?? ''} onChange={(e) => f('nameAr', e.target.value)} className="input w-full" dir="rtl" />
          </FormField>
          <FormField label="Country Code" required>
            <input type="text" value={form.countryCode} onChange={(e) => f('countryCode', e.target.value)} className="input w-full" placeholder="AE" maxLength={10} />
          </FormField>
          <FormField label="City" required>
            <input type="text" value={form.city} onChange={(e) => f('city', e.target.value)} className="input w-full" placeholder="Dubai" />
          </FormField>
          <FormField label="Time Zone" required>
            <input type="text" value={form.timeZoneId} onChange={(e) => f('timeZoneId', e.target.value)} className="input w-full" placeholder="Asia/Dubai" />
          </FormField>
          <FormField label="Status">
            <select value={form.isActive ? 'true' : 'false'} onChange={(e) => f('isActive', e.target.value === 'true')} className="select w-full">
              <option value="true">Active</option>
              <option value="false">Inactive</option>
            </select>
          </FormField>
          <div className="col-span-2 flex items-center gap-2">
            <input type="checkbox" id="isHQ" checked={form.isHeadOffice} onChange={(e) => f('isHeadOffice', e.target.checked)} className="h-4 w-4 accent-sapphire" />
            <label htmlFor="isHQ" className="text-sm font-medium text-slate-700 dark:text-slate-300">This is the head office</label>
          </div>
        </div>
      </Modal>
    </>
  );
}

// ─── Departments ─────────────────────────────────────────────────────────────

function DepartmentsTab({ costCenters }: { costCenters: CostCenterDto[] }) {
  const [items, setItems] = useState<DepartmentDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [modalOpen, setModalOpen] = useState(false);
  const [editing, setEditing] = useState<DepartmentDto | null>(null);
  const [form, setForm] = useState<DepartmentRequest>(emptyDept());
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  const load = useCallback(async () => {
    setLoading(true);
    try { const r = await departmentsApi.list(); setItems(r.items); } catch { /**/ }
    finally { setLoading(false); }
  }, []);

  useEffect(() => { load(); }, [load]);

  const openNew = () => { setEditing(null); setForm(emptyDept()); setError(''); setModalOpen(true); };
  const openEdit = (d: DepartmentDto) => {
    setEditing(d);
    setForm({ branchId: d.branchId, parentDepartmentId: d.parentDepartmentId, costCenterId: d.costCenterId, code: d.code, nameEn: d.nameEn, nameAr: d.nameAr, managerEmployeeId: d.managerEmployeeId, isActive: d.isActive });
    setError(''); setModalOpen(true);
  };

  const handleSave = async () => {
    if (!form.nameEn.trim() || !form.code.trim()) { setError('Name and code are required'); return; }
    setSaving(true); setError('');
    try {
      if (editing) await departmentsApi.update(editing.id, form);
      else await departmentsApi.create(form);
      setModalOpen(false); load();
    } catch { setError('Failed to save. Please try again.'); }
    finally { setSaving(false); }
  };

  const f = (key: keyof DepartmentRequest, v: string | boolean | number | undefined) => setForm((x) => ({ ...x, [key]: v }));

  return (
    <>
      <TableShell
        columns={['Code', 'Name', 'Status']}
        onAdd={openNew}
        addLabel="Add Department"
        loading={loading}
        empty={items.length === 0}
        emptyLabel="No departments yet"
      >
        {items.map((d) => (
          <tr key={d.id} className="group hover:bg-slate-50 dark:hover:bg-white/[0.03]">
            <td className="px-4 py-3 font-mono text-xs text-slate-500 dark:text-slate-400">{d.code}</td>
            <td className="px-4 py-3 font-medium text-slate-900 dark:text-white">{d.nameEn}</td>
            <td className="px-4 py-3"><ActiveBadge active={d.isActive} /></td>
            <td className="px-4 py-3">
              <button type="button" onClick={() => openEdit(d)} className="btn-secondary h-7 px-2 text-xs opacity-0 group-hover:opacity-100">
                <Pencil className="h-3 w-3" /> Edit
              </button>
            </td>
          </tr>
        ))}
      </TableShell>

      <Modal
        isOpen={modalOpen}
        title={editing ? 'Edit Department' : 'Add Department'}
        onClose={() => setModalOpen(false)}
        footer={
          <>
            <button type="button" onClick={() => setModalOpen(false)} className="btn-secondary">Cancel</button>
            <button type="button" onClick={handleSave} disabled={saving} className="btn-primary disabled:opacity-60">{saving ? 'Saving…' : 'Save'}</button>
          </>
        }
      >
        <FormError error={error} />
        <div className="space-y-3">
          <FormField label="Code" required>
            <input type="text" value={form.code} onChange={(e) => f('code', e.target.value)} className="input w-full" placeholder="ENG" />
          </FormField>
          <FormField label="Name (EN)" required>
            <input type="text" value={form.nameEn} onChange={(e) => f('nameEn', e.target.value)} className="input w-full" placeholder="Engineering" />
          </FormField>
          <FormField label="Name (AR)">
            <input type="text" value={form.nameAr ?? ''} onChange={(e) => f('nameAr', e.target.value)} className="input w-full" dir="rtl" />
          </FormField>
          <FormField label="Cost Center">
            <select value={form.costCenterId ?? ''} onChange={(e) => f('costCenterId', e.target.value || undefined)} className="select w-full">
              <option value="">None</option>
              {costCenters.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
            </select>
          </FormField>
          <FormField label="Status">
            <select value={form.isActive ? 'true' : 'false'} onChange={(e) => f('isActive', e.target.value === 'true')} className="select w-full">
              <option value="true">Active</option>
              <option value="false">Inactive</option>
            </select>
          </FormField>
        </div>
      </Modal>
    </>
  );
}

// ─── Designations ─────────────────────────────────────────────────────────────

function DesignationsTab({ grades }: { grades: GradeDto[] }) {
  const [items, setItems] = useState<DesignationDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [modalOpen, setModalOpen] = useState(false);
  const [editing, setEditing] = useState<DesignationDto | null>(null);
  const [form, setForm] = useState<DesignationRequest>(emptyDesig());
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  const load = useCallback(async () => {
    setLoading(true);
    try { const r = await designationsApi.list(); setItems(r.items); } catch { /**/ }
    finally { setLoading(false); }
  }, []);

  useEffect(() => { load(); }, [load]);

  const openNew = () => { setEditing(null); setForm(emptyDesig()); setError(''); setModalOpen(true); };
  const openEdit = (d: DesignationDto) => {
    setEditing(d);
    setForm({ departmentId: d.departmentId, code: d.code, titleEn: d.titleEn, titleAr: d.titleAr, jobGrade: d.jobGrade, gradeId: d.gradeId, jobLevel: d.jobLevel, jobDescription: d.jobDescription, isManagerRole: d.isManagerRole, isActive: d.isActive });
    setError(''); setModalOpen(true);
  };

  const handleSave = async () => {
    if (!form.titleEn.trim() || !form.code.trim()) { setError('Title and code are required'); return; }
    setSaving(true); setError('');
    try {
      if (editing) await designationsApi.update(editing.id, form);
      else await designationsApi.create(form);
      setModalOpen(false); load();
    } catch { setError('Failed to save. Please try again.'); }
    finally { setSaving(false); }
  };

  const f = (key: keyof DesignationRequest, v: string | boolean | undefined) => setForm((x) => ({ ...x, [key]: v }));

  return (
    <>
      <TableShell
        columns={['Code', 'Title', 'Job Grade', 'Manager Role', 'Status']}
        onAdd={openNew}
        addLabel="Add Designation"
        loading={loading}
        empty={items.length === 0}
        emptyLabel="No designations yet"
      >
        {items.map((d) => (
          <tr key={d.id} className="group hover:bg-slate-50 dark:hover:bg-white/[0.03]">
            <td className="px-4 py-3 font-mono text-xs text-slate-500 dark:text-slate-400">{d.code}</td>
            <td className="px-4 py-3 font-medium text-slate-900 dark:text-white">{d.titleEn}</td>
            <td className="px-4 py-3 text-slate-600 dark:text-slate-300">{d.jobGrade || '—'}</td>
            <td className="px-4 py-3">{d.isManagerRole ? <span className="rounded-full bg-violet-500/10 px-2 py-0.5 text-xs font-semibold text-violet-600 dark:text-violet-400">Manager</span> : '—'}</td>
            <td className="px-4 py-3"><ActiveBadge active={d.isActive} /></td>
            <td className="px-4 py-3">
              <button type="button" onClick={() => openEdit(d)} className="btn-secondary h-7 px-2 text-xs opacity-0 group-hover:opacity-100">
                <Pencil className="h-3 w-3" /> Edit
              </button>
            </td>
          </tr>
        ))}
      </TableShell>

      <Modal
        isOpen={modalOpen}
        title={editing ? 'Edit Designation' : 'Add Designation'}
        onClose={() => setModalOpen(false)}
        footer={
          <>
            <button type="button" onClick={() => setModalOpen(false)} className="btn-secondary">Cancel</button>
            <button type="button" onClick={handleSave} disabled={saving} className="btn-primary disabled:opacity-60">{saving ? 'Saving…' : 'Save'}</button>
          </>
        }
      >
        <FormError error={error} />
        <div className="space-y-3">
          <FormField label="Code" required>
            <input type="text" value={form.code} onChange={(e) => f('code', e.target.value)} className="input w-full" placeholder="SWE-L3" />
          </FormField>
          <FormField label="Title (EN)" required>
            <input type="text" value={form.titleEn} onChange={(e) => f('titleEn', e.target.value)} className="input w-full" placeholder="Software Engineer" />
          </FormField>
          <FormField label="Title (AR)">
            <input type="text" value={form.titleAr ?? ''} onChange={(e) => f('titleAr', e.target.value)} className="input w-full" dir="rtl" />
          </FormField>
          <FormField label="Job Grade">
            <input type="text" value={form.jobGrade ?? ''} onChange={(e) => f('jobGrade', e.target.value)} className="input w-full" placeholder="L3" />
          </FormField>
          <FormField label="Grade / Band">
            <select value={form.gradeId ?? ''} onChange={(e) => f('gradeId', e.target.value || undefined)} className="select w-full">
              <option value="">None</option>
              {grades.map((g) => <option key={g.id} value={g.id}>{g.name}</option>)}
            </select>
          </FormField>
          <FormField label="Job Level">
            <input type="text" value={form.jobLevel ?? ''} onChange={(e) => f('jobLevel', e.target.value)} className="input w-full" placeholder="Officer" />
          </FormField>
          <FormField label="Job Description">
            <textarea value={form.jobDescription ?? ''} onChange={(e) => f('jobDescription', e.target.value)} className="input min-h-24 w-full" placeholder="Core responsibilities and scope" />
          </FormField>
          <div className="flex items-center gap-2">
            <input type="checkbox" id="isMgr" checked={form.isManagerRole} onChange={(e) => f('isManagerRole', e.target.checked)} className="h-4 w-4 accent-sapphire" />
            <label htmlFor="isMgr" className="text-sm font-medium text-slate-700 dark:text-slate-300">Manager role</label>
          </div>
          <FormField label="Status">
            <select value={form.isActive ? 'true' : 'false'} onChange={(e) => f('isActive', e.target.value === 'true')} className="select w-full">
              <option value="true">Active</option>
              <option value="false">Inactive</option>
            </select>
          </FormField>
        </div>
      </Modal>
    </>
  );
}

// ─── Grades ─────────────────────────────────────────────────────────────────

function GradesTab() {
  const [items, setItems] = useState<GradeDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [modalOpen, setModalOpen] = useState(false);
  const [editing, setEditing] = useState<GradeDto | null>(null);
  const [form, setForm] = useState<GradeRequest>(emptyGrade());
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  const load = useCallback(async () => {
    setLoading(true);
    try { const r = await gradesApi.list(); setItems(r.items); } catch { /**/ }
    finally { setLoading(false); }
  }, []);

  useEffect(() => { load(); }, [load]);

  const openNew = () => { setEditing(null); setForm(emptyGrade()); setError(''); setModalOpen(true); };
  const openEdit = (g: GradeDto) => {
    setEditing(g);
    setForm({ code: g.code, name: g.name, band: g.band, level: g.level, isActive: g.isActive });
    setError('');
    setModalOpen(true);
  };
  const handleSave = async () => {
    if (!form.code.trim() || !form.name.trim()) { setError('Code and name are required'); return; }
    setSaving(true); setError('');
    try {
      if (editing) await gradesApi.update(editing.id, form);
      else await gradesApi.create(form);
      setModalOpen(false); load();
    } catch { setError('Failed to save. Please try again.'); }
    finally { setSaving(false); }
  };
  const f = (key: keyof GradeRequest, v: string | boolean | number) => setForm((x) => ({ ...x, [key]: v }));

  return (
    <>
      <TableShell columns={['Code', 'Name', 'Band', 'Level', 'Status']} onAdd={openNew} addLabel="Add Grade" loading={loading} empty={items.length === 0} emptyLabel="No grades yet">
        {items.map((g) => (
          <tr key={g.id} className="group hover:bg-slate-50 dark:hover:bg-white/[0.03]">
            <td className="px-4 py-3 font-mono text-xs text-slate-500 dark:text-slate-400">{g.code}</td>
            <td className="px-4 py-3 font-medium text-slate-900 dark:text-white">{g.name}</td>
            <td className="px-4 py-3 text-slate-600 dark:text-slate-300">{g.band || '—'}</td>
            <td className="px-4 py-3 text-slate-600 dark:text-slate-300">{g.level}</td>
            <td className="px-4 py-3"><ActiveBadge active={g.isActive} /></td>
            <td className="px-4 py-3"><button type="button" onClick={() => openEdit(g)} className="btn-secondary h-7 px-2 text-xs opacity-0 group-hover:opacity-100"><Pencil className="h-3 w-3" /> Edit</button></td>
          </tr>
        ))}
      </TableShell>
      <Modal isOpen={modalOpen} title={editing ? 'Edit Grade' : 'Add Grade'} onClose={() => setModalOpen(false)} footer={<><button type="button" onClick={() => setModalOpen(false)} className="btn-secondary">Cancel</button><button type="button" onClick={handleSave} disabled={saving} className="btn-primary disabled:opacity-60">{saving ? 'Saving…' : 'Save'}</button></>}>
        <FormError error={error} />
        <div className="space-y-3">
          <FormField label="Code" required><input value={form.code} onChange={(e) => f('code', e.target.value)} className="input w-full" placeholder="G5" /></FormField>
          <FormField label="Name" required><input value={form.name} onChange={(e) => f('name', e.target.value)} className="input w-full" placeholder="Professional Grade 5" /></FormField>
          <FormField label="Band"><input value={form.band ?? ''} onChange={(e) => f('band', e.target.value)} className="input w-full" placeholder="Professional" /></FormField>
          <FormField label="Level"><input type="number" value={form.level} onChange={(e) => f('level', Number(e.target.value))} className="input w-full" /></FormField>
          <FormField label="Status"><select value={form.isActive ? 'true' : 'false'} onChange={(e) => f('isActive', e.target.value === 'true')} className="select w-full"><option value="true">Active</option><option value="false">Inactive</option></select></FormField>
        </div>
      </Modal>
    </>
  );
}

// ─── Cost Centers ───────────────────────────────────────────────────────────

function CostCentersTab({ companies }: { companies: CompanyDto[] }) {
  const [items, setItems] = useState<CostCenterDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [modalOpen, setModalOpen] = useState(false);
  const [editing, setEditing] = useState<CostCenterDto | null>(null);
  const [form, setForm] = useState<CostCenterRequest>(emptyCostCenter());
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  const load = useCallback(async () => {
    setLoading(true);
    try { const r = await costCentersApi.list(); setItems(r.items); } catch { /**/ }
    finally { setLoading(false); }
  }, []);
  useEffect(() => { load(); }, [load]);

  const openNew = () => { setEditing(null); setForm(emptyCostCenter(companies[0]?.id)); setError(''); setModalOpen(true); };
  const openEdit = (c: CostCenterDto) => {
    setEditing(c);
    setForm({ companyId: c.companyId, code: c.code, name: c.name, isActive: c.isActive });
    setError('');
    setModalOpen(true);
  };
  const handleSave = async () => {
    if (!form.code.trim() || !form.name.trim()) { setError('Code and name are required'); return; }
    setSaving(true); setError('');
    try {
      if (editing) await costCentersApi.update(editing.id, form);
      else await costCentersApi.create(form);
      setModalOpen(false); load();
    } catch { setError('Failed to save. Please try again.'); }
    finally { setSaving(false); }
  };
  const f = (key: keyof CostCenterRequest, v: string | boolean | undefined) => setForm((x) => ({ ...x, [key]: v }));
  const companyName = (id?: string) => companies.find((c) => c.id === id)?.legalNameEn ?? '—';

  return (
    <>
      <TableShell columns={['Code', 'Name', 'Company', 'Status']} onAdd={openNew} addLabel="Add Cost Center" loading={loading} empty={items.length === 0} emptyLabel="No cost centers yet">
        {items.map((c) => (
          <tr key={c.id} className="group hover:bg-slate-50 dark:hover:bg-white/[0.03]">
            <td className="px-4 py-3 font-mono text-xs text-slate-500 dark:text-slate-400">{c.code}</td>
            <td className="px-4 py-3 font-medium text-slate-900 dark:text-white">{c.name}</td>
            <td className="px-4 py-3 text-slate-600 dark:text-slate-300">{companyName(c.companyId)}</td>
            <td className="px-4 py-3"><ActiveBadge active={c.isActive} /></td>
            <td className="px-4 py-3"><button type="button" onClick={() => openEdit(c)} className="btn-secondary h-7 px-2 text-xs opacity-0 group-hover:opacity-100"><Pencil className="h-3 w-3" /> Edit</button></td>
          </tr>
        ))}
      </TableShell>
      <Modal isOpen={modalOpen} title={editing ? 'Edit Cost Center' : 'Add Cost Center'} onClose={() => setModalOpen(false)} footer={<><button type="button" onClick={() => setModalOpen(false)} className="btn-secondary">Cancel</button><button type="button" onClick={handleSave} disabled={saving} className="btn-primary disabled:opacity-60">{saving ? 'Saving…' : 'Save'}</button></>}>
        <FormError error={error} />
        <div className="space-y-3">
          <FormField label="Company"><select value={form.companyId ?? ''} onChange={(e) => f('companyId', e.target.value || undefined)} className="select w-full"><option value="">None</option>{companies.map((c) => <option key={c.id} value={c.id}>{c.legalNameEn}</option>)}</select></FormField>
          <FormField label="Code" required><input value={form.code} onChange={(e) => f('code', e.target.value)} className="input w-full" placeholder="HR-OPS" /></FormField>
          <FormField label="Name" required><input value={form.name} onChange={(e) => f('name', e.target.value)} className="input w-full" placeholder="HR Operations" /></FormField>
          <FormField label="Status"><select value={form.isActive ? 'true' : 'false'} onChange={(e) => f('isActive', e.target.value === 'true')} className="select w-full"><option value="true">Active</option><option value="false">Inactive</option></select></FormField>
        </div>
      </Modal>
    </>
  );
}

// ─── Shared helpers ──────────────────────────────────────────────────────────

function TableShell({
  columns, onAdd, addLabel, loading, empty, emptyLabel, filter, children,
}: {
  columns: string[];
  onAdd: () => void;
  addLabel: string;
  loading: boolean;
  empty: boolean;
  emptyLabel: string;
  filter?: React.ReactNode;
  children: React.ReactNode;
}) {
  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2">{filter}</div>
        <button type="button" onClick={onAdd} className="btn-primary">
          <Plus className="h-4 w-4" />
          {addLabel}
        </button>
      </div>
      <div className="surface overflow-hidden">
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-slate-100 dark:border-white/[0.07]">
                {columns.map((col) => (
                  <th key={col} className="px-4 py-3 text-left text-xs font-bold uppercase tracking-wide text-slate-400 dark:text-slate-500">
                    {col}
                  </th>
                ))}
                <th className="w-20 px-4 py-3" />
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100 dark:divide-white/[0.05]">
              {loading && (
                <tr>
                  <td colSpan={columns.length + 1} className="py-12 text-center">
                    <div className="mx-auto h-6 w-6 animate-spin rounded-full border-2 border-sapphire border-t-transparent" />
                  </td>
                </tr>
              )}
              {!loading && empty && (
                <tr>
                  <td colSpan={columns.length + 1} className="py-12 text-center text-sm text-slate-400 dark:text-slate-500">
                    {emptyLabel}
                  </td>
                </tr>
              )}
              {!loading && children}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}

function ActiveBadge({ active }: { active: boolean }) {
  return (
    <span className={`inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-semibold ${active ? 'bg-emeraldZ/10 text-emeraldZ dark:bg-emeraldZ/20' : 'bg-slate-100 text-slate-400 dark:bg-white/10 dark:text-slate-500'}`}>
      <span className={`h-1.5 w-1.5 rounded-full ${active ? 'bg-emeraldZ' : 'bg-slate-400'}`} />
      {active ? 'Active' : 'Inactive'}
    </span>
  );
}

function FormField({ label, required, children }: { label: string; required?: boolean; children: React.ReactNode }) {
  return (
    <div>
      <label className="mb-1.5 block text-sm font-medium text-slate-700 dark:text-slate-300">
        {label} {required && <span className="text-red-500">*</span>}
      </label>
      {children}
    </div>
  );
}

function FormError({ error }: { error: string }) {
  if (!error) return null;
  return (
    <p className="mb-3 rounded-lg bg-red-50 px-3 py-2.5 text-sm text-red-600 dark:bg-red-500/10 dark:text-red-400">
      {error}
    </p>
  );
}

// ─── Empty form factories ────────────────────────────────────────────────────

const emptyCompany = (): CompanyRequest => ({
  legalNameEn: '', legalNameAr: '', tradeName: '', countryCode: '', registrationNumber: '',
  taxNumber: '', wpsEmployerId: '', gosiEmployerId: '', qiwaEstablishmentId: '',
  defaultCurrency: 'AED', isActive: true,
});

const emptyBranch = (companyId: string): BranchRequest => ({
  companyId, code: '', nameEn: '', nameAr: '', countryCode: '', city: '',
  addressLine1: '', addressLine2: '', timeZoneId: '', laborOfficeCode: '',
  isHeadOffice: false, isActive: true,
});

const emptyDept = (): DepartmentRequest => ({
  code: '', nameEn: '', nameAr: '', costCenterId: undefined, isActive: true,
});

const emptyDesig = (): DesignationRequest => ({
  code: '', titleEn: '', titleAr: '', jobGrade: '', gradeId: undefined, jobLevel: '', jobDescription: '', isManagerRole: false, isActive: true,
});

const emptyGrade = (): GradeRequest => ({
  code: '', name: '', band: '', level: 0, isActive: true,
});

const emptyCostCenter = (companyId?: string): CostCenterRequest => ({
  companyId, code: '', name: '', isActive: true,
});

// ─── SetupPage ───────────────────────────────────────────────────────────────

export function SetupPage() {
  const [activeTab, setActiveTab] = useState<Tab>('companies');
  const [companies, setCompanies] = useState<CompanyDto[]>([]);
  const [grades, setGrades] = useState<GradeDto[]>([]);
  const [costCenters, setCostCenters] = useState<CostCenterDto[]>([]);

  useEffect(() => {
    companiesApi.list(1, 100).then((r) => setCompanies(r.items)).catch(() => {});
    gradesApi.list(1, 100).then((r) => setGrades(r.items)).catch(() => {});
    costCentersApi.list(undefined, 1, 100).then((r) => setCostCenters(r.items)).catch(() => {});
  }, []);

  return (
    <div className="space-y-5">
      <div>
        <h1 className="text-xl font-bold text-slate-900 dark:text-white">Setup & Administration</h1>
        <p className="mt-0.5 text-sm text-slate-500 dark:text-slate-400">
          Manage your organization structure, branches, and designations
        </p>
      </div>

      {/* Tab bar */}
      <div className="flex items-center gap-1 border-b border-slate-200 dark:border-white/[0.08]">
        {tabs.map(({ id, label, icon: Icon }) => (
          <button
            key={id}
            type="button"
            onClick={() => setActiveTab(id)}
            className={`flex items-center gap-2 border-b-2 px-4 py-2.5 text-sm font-semibold transition ${
              activeTab === id
                ? 'border-sapphire text-sapphire'
                : 'border-transparent text-slate-500 hover:text-slate-800 dark:text-slate-400 dark:hover:text-slate-200'
            }`}
          >
            <Icon className="h-4 w-4" />
            {label}
          </button>
        ))}
      </div>

      {/* Tab content */}
      <div>
        {activeTab === 'companies' && <CompaniesTab />}
        {activeTab === 'branches' && <BranchesTab companies={companies} />}
        {activeTab === 'departments' && <DepartmentsTab costCenters={costCenters} />}
        {activeTab === 'designations' && <DesignationsTab grades={grades} />}
        {activeTab === 'grades' && <GradesTab />}
        {activeTab === 'costCenters' && <CostCentersTab companies={companies} />}
      </div>
    </div>
  );
}
