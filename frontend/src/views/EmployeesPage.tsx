'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { FileUp, History, Plus, RefreshCw, Search, Send, UserRound, Users } from 'lucide-react';
import { useSearchParams } from 'next/navigation';
import { employeesApi } from '../api/employees';
import type { EmployeeCreateRequest, EmployeeDetail, EmployeeListItem } from '../api/employees';
import { ImportExportToolbar, downloadCsv } from '../components/ImportExportToolbar';
import client from '../api/client';

const employeesImportExport = {
  export: async () => {
    const csv = await client.get<string>('/api/employees/export', { responseType: 'text' }).then(r => r.data);
    downloadCsv(csv, 'employees.csv');
  },
  template: async () => {
    const csv = await client.get<string>('/api/employees/import-template', { responseType: 'text' }).then(r => r.data);
    downloadCsv(csv, 'employees-template.csv');
  },
  import: (csvContent: string) =>
    client.post<{ received: number; created: number; skipped: number; errors: string[] }>('/api/employees/import', { csvContent }).then(r => r.data),
};
import { branchesApi, companiesApi, costCentersApi, departmentsApi, designationsApi, gradesApi } from '../api/organization';
import type { BranchDto, CompanyDto, CostCenterDto, DepartmentDto, DesignationDto, GradeDto } from '../api/organization';
import { Avatar } from '../components/Avatar';
import { Modal } from '../components/Modal';
import { StatusChip } from '../components/StatusChip';

type StatusFilter = '' | 'Draft' | 'Pre-boarding' | 'Active' | 'Probation' | 'Confirmed' | 'On leave' | 'Suspended' | 'Resigned' | 'Notice period' | 'Terminated' | 'Retired' | 'Absconded' | 'Inactive' | 'Blacklisted';
type DetailTab = 'personal' | 'employment' | 'payroll' | 'compliance' | 'documents' | 'history' | 'transfers';

const statusOptions: StatusFilter[] = ['', 'Draft', 'Pre-boarding', 'Active', 'Probation', 'Confirmed', 'On leave', 'Suspended', 'Resigned', 'Notice period', 'Terminated', 'Retired', 'Absconded', 'Inactive', 'Blacklisted'];
const tabs: { id: DetailTab; label: string }[] = [
  { id: 'personal', label: 'Personal Information' },
  { id: 'employment', label: 'Employment Information' },
  { id: 'payroll', label: 'Payroll Profile' },
  { id: 'compliance', label: 'Compliance' },
  { id: 'documents', label: 'Documents' },
  { id: 'history', label: 'History' },
  { id: 'transfers', label: 'Transfers' },
];

const emptyEmployee = (): EmployeeCreateRequest => ({
  manualEmployeeCode: false,
  englishName: '',
  arabicName: '',
  preferredName: '',
  gender: '',
  nationality: '',
  maritalStatus: '',
  personalEmail: '',
  workEmail: '',
  mobileNumber: '',
  companyId: '',
  branchId: '',
  departmentId: '',
  designationId: '',
  gradeId: '',
  costCenterId: '',
  jobTitle: '',
  employmentType: 'Full-Time',
  contractType: 'Unlimited',
  joiningDate: new Date().toISOString().slice(0, 10),
  workLocation: '',
  payrollGroup: '',
  shiftPolicyCode: '',
  leavePolicyCode: '',
  attendancePolicyCode: '',
  payrollProfile: {
    bankName: '',
    iban: '',
    accountNumber: '',
    paymentMethod: 'BankTransfer',
    salaryCurrency: 'AED',
    payrollGroup: '',
    salaryStructureReference: '',
    wpsEligible: true,
    eosbEligible: true,
    socialInsuranceReference: '',
  },
  complianceRecords: [
    { countryCode: 'UAE', fieldKey: 'passport_number', fieldLabel: 'Passport Number', fieldValue: '', isSensitive: true, isRequired: true },
    { countryCode: 'UAE', fieldKey: 'visa_number', fieldLabel: 'Visa Number', fieldValue: '', isSensitive: true, isRequired: false },
    { countryCode: 'UAE', fieldKey: 'emirates_id', fieldLabel: 'Emirates ID', fieldValue: '', isSensitive: true, isRequired: false },
  ],
});

export function EmployeesPage() {
  const searchParams = useSearchParams();
  const [employees, setEmployees] = useState<EmployeeListItem[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState('');
  const [status, setStatus] = useState<StatusFilter>('');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [formOpen, setFormOpen] = useState(false);
  const [form, setForm] = useState<EmployeeCreateRequest>(emptyEmployee());
  const [saving, setSaving] = useState(false);
  const [selectedId, setSelectedId] = useState<number | null>(null);
  const [detail, setDetail] = useState<EmployeeDetail | null>(null);
  const [detailLoading, setDetailLoading] = useState(false);
  const [activeTab, setActiveTab] = useState<DetailTab>('personal');
  const [statusReason, setStatusReason] = useState('');
  const [newStatus, setNewStatus] = useState<StatusFilter>('Active');
  const [transferReason, setTransferReason] = useState('');
  const [transferDepartment, setTransferDepartment] = useState('');
  const [documentFile, setDocumentFile] = useState<File | null>(null);
  const [documentType, setDocumentType] = useState('Passport');
  const [documentExpiry, setDocumentExpiry] = useState('');
  const [companies, setCompanies] = useState<CompanyDto[]>([]);
  const [branches, setBranches] = useState<BranchDto[]>([]);
  const [departments, setDepartments] = useState<DepartmentDto[]>([]);
  const [designations, setDesignations] = useState<DesignationDto[]>([]);
  const [grades, setGrades] = useState<GradeDto[]>([]);
  const [costCenters, setCostCenters] = useState<CostCenterDto[]>([]);

  const pageSize = 25;
  const totalPages = Math.max(1, Math.ceil(total / pageSize));

  const load = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      const res = await employeesApi.list({ search, status, page, pageSize });
      setEmployees(res.items);
      setTotal(res.total);
    } catch {
      setError('Could not load employees from the API.');
    } finally {
      setLoading(false);
    }
  }, [page, search, status]);

  const loadLookups = useCallback(async () => {
    const [companyRes, branchRes, deptRes, desigRes, gradeRes, costRes] = await Promise.all([
      companiesApi.list(1, 100),
      branchesApi.list(undefined, 1, 100),
      departmentsApi.list(undefined, 1, 100),
      designationsApi.list(undefined, 1, 100),
      gradesApi.list(1, 100),
      costCentersApi.list(undefined, 1, 100),
    ]);
    setCompanies(companyRes.items);
    setBranches(branchRes.items);
    setDepartments(deptRes.items);
    setDesignations(desigRes.items);
    setGrades(gradeRes.items);
    setCostCenters(costRes.items);
  }, []);

  useEffect(() => { load(); }, [load]);
  useEffect(() => { loadLookups().catch(() => setError('Could not load organization setup data.')); }, [loadLookups]);
  useEffect(() => { setPage(1); }, [search, status]);
  useEffect(() => {
    const searchFromUrl = searchParams?.get('search') ?? null;
    if (searchFromUrl !== null && searchFromUrl !== search) {
      setSearch(searchFromUrl);
    }
  }, [search, searchParams]);
  useEffect(() => {
    const employeeId = searchParams?.get('employeeId') ?? null;
    if (!employeeId) return;
    const id = Number(employeeId);
    if (Number.isFinite(id) && id > 0 && selectedId !== id) {
      openDetail(id);
    }
  }, [searchParams, selectedId]);

  const selectedEmployee = useMemo(() => detail?.employee, [detail]);

  const openDetail = async (id: number) => {
    setSelectedId(id);
    setDetailLoading(true);
    setActiveTab('personal');
    try {
      setDetail(await employeesApi.get(id));
    } catch {
      setError('Could not load employee detail from the API.');
    } finally {
      setDetailLoading(false);
    }
  };

  const setField = (key: keyof EmployeeCreateRequest, value: string | boolean | number | undefined) =>
    setForm((current) => ({ ...current, [key]: value }));

  const setPayrollField = (key: string, value: string | boolean) =>
    setForm((current) => ({ ...current, payrollProfile: { ...current.payrollProfile!, [key]: value } }));

  const setComplianceValue = (index: number, key: string, value: string | boolean) =>
    setForm((current) => ({
      ...current,
      complianceRecords: (current.complianceRecords ?? []).map((record, i) => (i === index ? { ...record, [key]: value } : record)),
    }));

  const saveEmployee = async () => {
    setError('');
    if (!form.englishName.trim() || !form.gender) {
      setError('English full name and gender are required.');
      return;
    }
    setSaving(true);
    try {
      const created = await employeesApi.create(cleanPayload(form));
      setFormOpen(false);
      setForm(emptyEmployee());
      await load();
      await openDetail(created.employee.id);
    } catch {
      setError('Employee could not be saved. Check validation errors and try again.');
    } finally {
      setSaving(false);
    }
  };

  const changeStatus = async () => {
    if (!selectedId || !newStatus || !statusReason.trim()) return;
    const updated = await employeesApi.changeStatus(selectedId, {
      status: newStatus,
      effectiveDate: new Date().toISOString().slice(0, 10),
      reason: statusReason,
    });
    setDetail(updated);
    setStatusReason('');
    await load();
  };

  const uploadDocument = async () => {
    if (!selectedId || !documentFile) return;
    const uploaded = await employeesApi.uploadDocument(selectedId, {
      documentType,
      expiryDate: documentExpiry || undefined,
      renewalReminderDate: documentExpiry || undefined,
      isRequired: true,
      approvalStatus: 'Pending',
    }, documentFile);
    setDetail((current) => current ? { ...current, documents: [uploaded, ...current.documents] } : current);
    setDocumentFile(null);
    setDocumentExpiry('');
  };

  const requestTransfer = async () => {
    if (!selectedId || !transferDepartment || !transferReason.trim()) return;
    const dept = departments.find((item) => item.id === transferDepartment);
    const transfer = await employeesApi.transfer(selectedId, {
      newDepartment: dept?.nameEn ?? transferDepartment,
      effectiveDate: new Date().toISOString().slice(0, 10),
      reason: transferReason,
    });
    setDetail((current) => current ? { ...current, transfers: [transfer, ...current.transfers] } : current);
    setTransferDepartment('');
    setTransferReason('');
  };

  return (
    <div className="space-y-5">
      <div className="flex flex-col gap-3 md:flex-row md:items-center md:justify-between">
        <div>
          <h1 className="text-2xl font-extrabold text-slate-950 dark:text-white">Employee Management</h1>
          <p className="mt-0.5 text-sm text-slate-500 dark:text-slate-400">{total} live employee records from MySQL</p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <ImportExportToolbar
            entityName="Employees"
            onExport={employeesImportExport.export}
            onDownloadTemplate={employeesImportExport.template}
            onImport={employeesImportExport.import}
          />
          <button type="button" onClick={() => { setForm(emptyEmployee()); setFormOpen(true); }} className="btn-primary">
            <Plus className="h-4 w-4" />
            Add Employee
          </button>
        </div>
      </div>

      {error && <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600 dark:bg-red-500/10 dark:text-red-300">{error}</p>}

      <div className="grid gap-5 xl:grid-cols-[minmax(0,1fr)_420px]">
        <section className="space-y-4">
          <div className="flex flex-col gap-2 sm:flex-row">
            <div className="relative flex-1">
              <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
              <input value={search} onChange={(e) => setSearch(e.target.value)} className="input w-full pl-9" placeholder="Search employee code, name, email" />
            </div>
            <select value={status} onChange={(e) => setStatus(e.target.value as StatusFilter)} className="select sm:w-56" aria-label="Status filter">
              {statusOptions.map((item) => <option key={item || 'all'} value={item}>{item || 'All statuses'}</option>)}
            </select>
            <button type="button" onClick={load} className="btn-secondary">
              <RefreshCw className="h-4 w-4" />
              Refresh
            </button>
          </div>

          <div className="surface overflow-hidden">
            <div className="overflow-x-auto">
              <table className="w-full min-w-[820px] text-sm">
                <thead>
                  <tr className="border-b border-slate-100 dark:border-white/[0.07]">
                    {['Employee', 'Department', 'Designation', 'Branch', 'Status', 'Profile'].map((head) => (
                      <th key={head} className="px-4 py-3 text-left text-xs font-bold uppercase text-slate-400">{head}</th>
                    ))}
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100 dark:divide-white/[0.05]">
                  {loading && <EmptyRow label="Loading live employees..." />}
                  {!loading && employees.length === 0 && <EmptyRow label="No employees found" />}
                  {!loading && employees.map((employee) => (
                    <tr key={employee.id} onClick={() => openDetail(employee.id)} className="cursor-pointer hover:bg-slate-50 dark:hover:bg-white/[0.03]">
                      <td className="px-4 py-3">
                        <div className="flex items-center gap-3">
                          <Avatar name={employee.fullName} size="sm" />
                          <div>
                            <p className="font-semibold text-slate-900 dark:text-white">{employee.fullName}</p>
                            <p className="text-xs text-slate-400">{employee.employeeCode}</p>
                          </div>
                        </div>
                      </td>
                      <td className="px-4 py-3 text-slate-600 dark:text-slate-300">{employee.department || '-'}</td>
                      <td className="px-4 py-3 text-slate-600 dark:text-slate-300">{employee.designation || '-'}</td>
                      <td className="px-4 py-3 text-slate-600 dark:text-slate-300">{employee.branch || '-'}</td>
                      <td className="px-4 py-3"><StatusChip {...statusTone(employee.status)} /></td>
                      <td className="px-4 py-3 font-semibold text-slate-700 dark:text-slate-200">{employee.profileCompletenessScore.toFixed(0)}%</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <div className="flex items-center justify-between border-t border-slate-100 px-4 py-3 text-xs text-slate-500 dark:border-white/[0.07]">
              <span>Page {page} of {totalPages}</span>
              <div className="flex gap-1">
                <button type="button" onClick={() => setPage((p) => Math.max(1, p - 1))} disabled={page === 1} className="btn-secondary h-7 px-2 text-xs disabled:opacity-40">Prev</button>
                <button type="button" onClick={() => setPage((p) => Math.min(totalPages, p + 1))} disabled={page === totalPages} className="btn-secondary h-7 px-2 text-xs disabled:opacity-40">Next</button>
              </div>
            </div>
          </div>
        </section>

        <aside className="surface min-h-[560px] overflow-hidden">
          {!selectedId && (
            <div className="flex h-full min-h-[520px] flex-col items-center justify-center px-6 text-center">
              <Users className="mb-3 h-10 w-10 text-slate-300 dark:text-slate-700" />
              <p className="font-semibold text-slate-900 dark:text-white">Select an employee</p>
              <p className="mt-1 text-sm text-slate-500 dark:text-slate-400">Open a live profile with payroll, compliance, documents, history, and transfers.</p>
            </div>
          )}

          {selectedId && detailLoading && <div className="p-6 text-sm text-slate-500">Loading profile from API...</div>}

          {selectedEmployee && !detailLoading && (
            <div>
              <div className="border-b border-slate-100 p-4 dark:border-white/[0.07]">
                <div className="flex items-center gap-3">
                  <Avatar name={selectedEmployee.fullName} />
                  <div className="min-w-0">
                    <p className="truncate font-bold text-slate-900 dark:text-white">{selectedEmployee.fullName}</p>
                    <p className="text-xs text-slate-500">{selectedEmployee.employeeCode} · {selectedEmployee.status}</p>
                  </div>
                </div>
                <div className="mt-4 flex gap-1 overflow-x-auto">
                  {tabs.map((tab) => (
                    <button key={tab.id} type="button" onClick={() => setActiveTab(tab.id)} className={`whitespace-nowrap rounded-lg px-2.5 py-1.5 text-xs font-semibold ${activeTab === tab.id ? 'bg-sapphire text-white' : 'text-slate-500 hover:bg-slate-100 dark:hover:bg-white/[0.07]'}`}>
                      {tab.label}
                    </button>
                  ))}
                </div>
              </div>
              <div className="space-y-4 p-4">
                {activeTab === 'personal' && (
                  <DetailGrid rows={[
                    ['English name', selectedEmployee.englishName],
                    ['Arabic name', selectedEmployee.arabicName],
                    ['Preferred name', selectedEmployee.preferredName],
                    ['Gender', selectedEmployee.gender],
                    ['Nationality', selectedEmployee.nationality],
                    ['Personal email', selectedEmployee.personalEmail],
                    ['Work email', selectedEmployee.workEmail],
                    ['Mobile', selectedEmployee.phone],
                  ]} />
                )}
                {activeTab === 'employment' && (
                  <DetailGrid rows={[
                    ['Company', nameById(companies, selectedEmployee.companyId, 'legalNameEn')],
                    ['Branch', selectedEmployee.branch],
                    ['Department', selectedEmployee.department],
                    ['Designation', selectedEmployee.designation],
                    ['Grade', selectedEmployee.grade],
                    ['Job title', selectedEmployee.jobTitle],
                    ['Employment type', selectedEmployee.employmentType],
                    ['Contract type', selectedEmployee.contractType],
                    ['Joining date', dateLabel(selectedEmployee.joiningDate)],
                    ['Work location', selectedEmployee.workLocation],
                  ]} />
                )}
                {activeTab === 'payroll' && (
                  <DetailGrid rows={[
                    ['Bank', detail!.payrollProfile?.bankName],
                    ['IBAN', detail!.payrollProfile?.iban],
                    ['Account', detail!.payrollProfile?.accountNumber],
                    ['Payment method', detail!.payrollProfile?.paymentMethod],
                    ['Currency', detail!.payrollProfile?.salaryCurrency],
                    ['Payroll group', detail!.payrollProfile?.payrollGroup],
                    ['WPS eligible', detail!.payrollProfile?.wpsEligible ? 'Yes' : 'No'],
                    ['EOSB eligible', detail!.payrollProfile?.eosbEligible ? 'Yes' : 'No'],
                  ]} />
                )}
                {activeTab === 'compliance' && (
                  <div className="space-y-2">
                    {detail!.complianceRecords.length === 0 && <SmallEmpty label="No compliance records saved" />}
                    {detail!.complianceRecords.map((record) => (
                      <div key={`${record.countryCode}-${record.fieldKey}`} className="rounded-lg border border-slate-200 p-3 dark:border-white/10">
                        <p className="text-sm font-semibold text-slate-900 dark:text-white">{record.fieldLabel}</p>
                        <p className="mt-1 text-xs text-slate-500">{record.countryCode} · {record.fieldValue || '-'} · Expires {dateLabel(record.expiryDate)}</p>
                      </div>
                    ))}
                  </div>
                )}
                {activeTab === 'documents' && (
                  <div className="space-y-3">
                    <div className="grid gap-2">
                      <select value={documentType} onChange={(e) => setDocumentType(e.target.value)} className="select w-full" aria-label="Document type">
                        {['Passport', 'Visa', 'Iqama', 'Emirates ID', 'National ID', 'Labor card', 'Contract', 'Offer letter', 'NDA', 'Policy acknowledgment'].map((item) => <option key={item}>{item}</option>)}
                      </select>
                      <input type="date" value={documentExpiry} onChange={(e) => setDocumentExpiry(e.target.value)} className="input w-full" aria-label="Document expiry" />
                      <input type="file" onChange={(e) => setDocumentFile(e.target.files?.[0] ?? null)} className="input w-full" aria-label="Upload document file" />
                      <button type="button" onClick={uploadDocument} disabled={!documentFile} className="btn-primary justify-center disabled:opacity-50">
                        <FileUp className="h-4 w-4" />
                        Upload metadata and file
                      </button>
                    </div>
                    {detail!.documents.map((doc) => (
                      <div key={doc.id} className="rounded-lg border border-slate-200 p-3 dark:border-white/10">
                        <p className="text-sm font-semibold text-slate-900 dark:text-white">{doc.documentType}</p>
                        <p className="text-xs text-slate-500">{doc.fileName} · {doc.approvalStatus} · Expires {dateLabel(doc.expiryDate)}</p>
                      </div>
                    ))}
                  </div>
                )}
                {activeTab === 'history' && (
                  <Timeline items={detail!.history.map((item) => ({ id: item.id, title: item.eventType, body: `${item.oldValue || '-'} -> ${item.newValue || '-'} · ${item.reason || 'No reason'}`, date: item.createdAtUtc }))} />
                )}
                {activeTab === 'transfers' && (
                  <div className="space-y-3">
                    <select value={transferDepartment} onChange={(e) => setTransferDepartment(e.target.value)} className="select w-full" aria-label="Transfer to department">
                      <option value="">New department</option>
                      {departments.map((dept) => <option key={dept.id} value={dept.id}>{dept.nameEn}</option>)}
                    </select>
                    <input value={transferReason} onChange={(e) => setTransferReason(e.target.value)} className="input w-full" placeholder="Transfer reason" />
                    <button type="button" onClick={requestTransfer} className="btn-primary justify-center">
                      <Send className="h-4 w-4" />
                      Request Transfer
                    </button>
                    {detail!.transfers.map((transfer) => (
                      <div key={transfer.id} className="rounded-lg border border-slate-200 p-3 dark:border-white/10">
                        <p className="text-sm font-semibold text-slate-900 dark:text-white">{transfer.newDepartment || 'Transfer request'}</p>
                        <p className="text-xs text-slate-500">{transfer.status} · Effective {dateLabel(transfer.effectiveDate)}</p>
                      </div>
                    ))}
                  </div>
                )}

                <div className="rounded-lg border border-slate-200 p-3 dark:border-white/10">
                  <p className="mb-2 text-xs font-bold uppercase text-slate-400">Status change</p>
                  <div className="grid gap-2">
                    <select value={newStatus} onChange={(e) => setNewStatus(e.target.value as StatusFilter)} className="select w-full" aria-label="New employee status">
                      {statusOptions.filter(Boolean).map((item) => <option key={item} value={item}>{item}</option>)}
                    </select>
                    <input value={statusReason} onChange={(e) => setStatusReason(e.target.value)} className="input w-full" placeholder="Required reason" />
                    <button type="button" onClick={changeStatus} disabled={!statusReason.trim()} className="btn-secondary justify-center disabled:opacity-40">
                      <History className="h-4 w-4" />
                      Save status history
                    </button>
                  </div>
                </div>
              </div>
            </div>
          )}
        </aside>
      </div>

      <Modal isOpen={formOpen} title="Add Employee" size="lg" onClose={() => setFormOpen(false)} footer={
        <>
          <button type="button" onClick={() => setFormOpen(false)} className="btn-secondary">Cancel</button>
          <button type="button" onClick={saveEmployee} disabled={saving} className="btn-primary disabled:opacity-60">{saving ? 'Saving...' : 'Create Employee'}</button>
        </>
      }>
        <div className="grid gap-5 lg:grid-cols-2">
          <Section title="Master Profile">
            <Input label="Employee code" value={form.employeeCode ?? ''} onChange={(v) => setField('employeeCode', v)} placeholder="Leave blank for auto generation" />
            <label className="flex items-center gap-2 text-sm font-medium text-slate-700 dark:text-slate-300">
              <input type="checkbox" checked={form.manualEmployeeCode} onChange={(e) => setField('manualEmployeeCode', e.target.checked)} className="h-4 w-4 accent-sapphire" />
              Manual override
            </label>
            <Input label="English full name" required value={form.englishName} onChange={(v) => setField('englishName', v)} />
            <Input label="Arabic full name" value={form.arabicName ?? ''} onChange={(v) => setField('arabicName', v)} rtl />
            <Input label="Preferred name" value={form.preferredName ?? ''} onChange={(v) => setField('preferredName', v)} />
            <Select label="Gender" required value={form.gender} onChange={(v) => setField('gender', v)} options={['Male', 'Female', 'Other']} />
            <Input label="Nationality" value={form.nationality ?? ''} onChange={(v) => setField('nationality', v)} />
            <Input label="Personal email" value={form.personalEmail ?? ''} onChange={(v) => setField('personalEmail', v)} type="email" />
            <Input label="Work email" value={form.workEmail ?? ''} onChange={(v) => setField('workEmail', v)} type="email" />
            <Input label="Mobile number" value={form.mobileNumber ?? ''} onChange={(v) => setField('mobileNumber', v)} />
          </Section>

          <Section title="Employment Details">
            <Lookup label="Company" value={form.companyId ?? ''} onChange={(v) => setField('companyId', v)} items={companies} textKey="legalNameEn" />
            <Lookup label="Branch" value={form.branchId ?? ''} onChange={(v) => setField('branchId', v)} items={branches} textKey="nameEn" />
            <Lookup label="Department" value={form.departmentId ?? ''} onChange={(v) => setField('departmentId', v)} items={departments} textKey="nameEn" />
            <Lookup label="Designation" value={form.designationId ?? ''} onChange={(v) => setField('designationId', v)} items={designations} textKey="titleEn" />
            <Lookup label="Grade" value={form.gradeId ?? ''} onChange={(v) => setField('gradeId', v)} items={grades} textKey="name" />
            <Lookup label="Cost center" value={form.costCenterId ?? ''} onChange={(v) => setField('costCenterId', v)} items={costCenters} textKey="name" />
            <Input label="Job title" value={form.jobTitle ?? ''} onChange={(v) => setField('jobTitle', v)} />
            <Select label="Employment type" value={form.employmentType ?? ''} onChange={(v) => setField('employmentType', v)} options={['Full-Time', 'Part-Time', 'Contractor', 'Intern']} />
            <Select label="Contract type" value={form.contractType ?? ''} onChange={(v) => setField('contractType', v)} options={['Unlimited', 'Fixed-Term', 'Temporary']} />
            <Input label="Joining date" value={form.joiningDate ?? ''} onChange={(v) => setField('joiningDate', v)} type="date" />
            <Input label="Work location" value={form.workLocation ?? ''} onChange={(v) => setField('workLocation', v)} />
          </Section>

          <Section title="Payroll Profile">
            <Input label="Bank name" value={form.payrollProfile?.bankName ?? ''} onChange={(v) => setPayrollField('bankName', v)} />
            <Input label="IBAN" value={form.payrollProfile?.iban ?? ''} onChange={(v) => setPayrollField('iban', v)} />
            <Input label="Account number" value={form.payrollProfile?.accountNumber ?? ''} onChange={(v) => setPayrollField('accountNumber', v)} />
            <Select label="Salary currency" value={form.payrollProfile?.salaryCurrency ?? 'AED'} onChange={(v) => setPayrollField('salaryCurrency', v)} options={['AED', 'SAR', 'QAR', 'KWD', 'BHD', 'OMR', 'USD']} />
            <Input label="Payroll group" value={form.payrollProfile?.payrollGroup ?? ''} onChange={(v) => setPayrollField('payrollGroup', v)} />
            <Input label="Salary structure reference" value={form.payrollProfile?.salaryStructureReference ?? ''} onChange={(v) => setPayrollField('salaryStructureReference', v)} />
          </Section>

          <Section title="Configurable GCC Compliance">
            {(form.complianceRecords ?? []).map((record, index) => (
              <div key={record.fieldKey} className="grid gap-2 rounded-lg border border-slate-200 p-3 dark:border-white/10">
                <Input label={record.fieldLabel} value={record.fieldValue ?? ''} onChange={(v) => setComplianceValue(index, 'fieldValue', v)} />
                <Input label="Expiry date" value={record.expiryDate ?? ''} onChange={(v) => setComplianceValue(index, 'expiryDate', v)} type="date" />
              </div>
            ))}
          </Section>
        </div>
      </Modal>
    </div>
  );
}

function cleanPayload(form: EmployeeCreateRequest): EmployeeCreateRequest {
  const emptyToUndefined = (value?: string) => value?.trim() ? value.trim() : undefined;
  return {
    ...form,
    employeeCode: emptyToUndefined(form.employeeCode),
    companyId: emptyToUndefined(form.companyId),
    branchId: emptyToUndefined(form.branchId),
    departmentId: emptyToUndefined(form.departmentId),
    designationId: emptyToUndefined(form.designationId),
    gradeId: emptyToUndefined(form.gradeId),
    costCenterId: emptyToUndefined(form.costCenterId),
    complianceRecords: form.complianceRecords?.filter((item) => item.fieldValue?.trim() || item.isRequired),
  };
}

function statusTone(status: string): { label: string; tone: 'emerald' | 'blue' | 'amber' | 'rose' | 'slate' } {
  if (['Active', 'Confirmed'].includes(status)) return { label: status, tone: 'emerald' };
  if (['Draft', 'Pre-boarding', 'Probation', 'Notice period'].includes(status)) return { label: status, tone: 'amber' };
  if (['Suspended', 'Terminated', 'Blacklisted'].includes(status)) return { label: status, tone: 'rose' };
  return { label: status, tone: 'slate' };
}

function EmptyRow({ label }: { label: string }) {
  return <tr><td colSpan={6} className="py-16 text-center text-sm text-slate-400">{label}</td></tr>;
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return <fieldset className="space-y-3"><legend className="mb-3 text-xs font-bold uppercase tracking-wide text-slate-400">{title}</legend>{children}</fieldset>;
}

function Input({ label, value, onChange, required, type = 'text', placeholder, rtl }: { label: string; value: string; onChange: (value: string) => void; required?: boolean; type?: string; placeholder?: string; rtl?: boolean }) {
  return (
    <label className="block text-sm font-medium text-slate-700 dark:text-slate-300">
      <span>{label} {required && <span className="text-red-500">*</span>}</span>
      <input type={type} value={value} onChange={(e) => onChange(e.target.value)} placeholder={placeholder} dir={rtl ? 'rtl' : undefined} className="input mt-1.5 w-full" />
    </label>
  );
}

function Select({ label, value, onChange, options, required }: { label: string; value: string; onChange: (value: string) => void; options: string[]; required?: boolean }) {
  return (
    <label className="block text-sm font-medium text-slate-700 dark:text-slate-300">
      <span>{label} {required && <span className="text-red-500">*</span>}</span>
      <select value={value} onChange={(e) => onChange(e.target.value)} className="select mt-1.5 w-full">
        <option value="">Select</option>
        {options.map((option) => <option key={option} value={option}>{option}</option>)}
      </select>
    </label>
  );
}

function Lookup<T extends { id: string }>({ label, value, onChange, items, textKey }: { label: string; value: string; onChange: (value: string) => void; items: T[]; textKey: keyof T }) {
  return (
    <label className="block text-sm font-medium text-slate-700 dark:text-slate-300">
      <span>{label}</span>
      <select value={value} onChange={(e) => onChange(e.target.value)} className="select mt-1.5 w-full">
        <option value="">Select</option>
        {items.map((item) => <option key={item.id} value={item.id}>{String(item[textKey])}</option>)}
      </select>
    </label>
  );
}

function DetailGrid({ rows }: { rows: Array<[string, string | number | boolean | null | undefined]> }) {
  return <dl className="grid gap-3">{rows.map(([label, value]) => <div key={label} className="rounded-lg bg-slate-50 p-3 dark:bg-white/[0.04]"><dt className="text-xs font-semibold uppercase text-slate-400">{label}</dt><dd className="mt-1 text-sm text-slate-800 dark:text-slate-100">{value || '-'}</dd></div>)}</dl>;
}

function Timeline({ items }: { items: Array<{ id: string; title: string; body: string; date: string }> }) {
  if (items.length === 0) return <SmallEmpty label="No history records yet" />;
  return <div className="space-y-3">{items.map((item) => <div key={item.id} className="rounded-lg border border-slate-200 p-3 dark:border-white/10"><p className="text-sm font-semibold text-slate-900 dark:text-white">{item.title}</p><p className="mt-1 text-xs text-slate-500">{item.body}</p><p className="mt-2 text-[11px] text-slate-400">{dateLabel(item.date)}</p></div>)}</div>;
}

function SmallEmpty({ label }: { label: string }) {
  return <div className="rounded-lg border border-dashed border-slate-200 p-5 text-center text-sm text-slate-400 dark:border-white/10"><UserRound className="mx-auto mb-2 h-5 w-5" />{label}</div>;
}

function dateLabel(value?: string) {
  if (!value) return '-';
  return value.slice(0, 10);
}

function nameById<T extends { id: string }>(items: T[], id: string | undefined, key: keyof T) {
  if (!id) return '-';
  return String(items.find((item) => item.id === id)?.[key] ?? '-');
}
