import client from './client';
import type { PagedResult } from './organization';

export interface SalaryStructure {
  id: string;
  code: string;
  name: string;
  currency: string;
  effectiveDate: string;
  isActive: boolean;
  createdAtUtc: string;
}

export interface SalaryComponent {
  id: string;
  salaryStructureId: string | null;
  code: string;
  name: string;
  componentType: string;
  calculationType: string;
  amount: number;
  percentage: number;
  isTaxable: boolean;
  isActive: boolean;
}

export interface EmployeeSalaryStructure {
  id: string;
  employeeId: number;
  salaryStructureId: string;
  basicSalary: number;
  housingAllowance: number;
  transportAllowance: number;
  foodAllowance: number;
  mobileAllowance: number;
  otherAllowance: number;
  fixedDeduction: number;
  effectiveDate: string;
  currency: string;
  isActive: boolean;
  createdAtUtc: string;
}

export interface PayrollRun {
  id: string;
  year: number;
  month: number;
  status: string;
  totalGrossSalary: number;
  totalDeductions: number;
  totalNetSalary: number;
  employeeCount: number;
  createdAtUtc: string;
  processedAtUtc: string | null;
  lockedAtUtc: string | null;
}

export interface PayrollSlip {
  id: string;
  runId: string;
  employeeId: number;
  employeeCode: string;
  employeeName: string;
  department: string;
  basicSalary: number;
  housingAllowance: number;
  transportAllowance: number;
  otherAllowances: number;
  grossSalary: number;
  deductions: number;
  netSalary: number;
  status: string;
}

export interface PayrollValidationResult {
  id: string;
  employeeId: number | null;
  severity: string;
  code: string;
  message: string;
  isResolved: boolean;
}

export interface Payslip {
  id: string;
  payrollRunId: string;
  employeeId: number;
  payslipNumber: string;
  language: string;
  isPublishedToEss: boolean;
  createdAtUtc: string;
  publishedAtUtc: string | null;
}

export interface PayrollPaymentBatch {
  id: string;
  payrollRunId: string;
  batchNumber: string;
  paymentMethod: string;
  totalAmount: number;
  currency: string;
  status: string;
  createdAtUtc: string;
}

export interface PayrollPaymentRecord {
  id: string;
  paymentBatchId: string;
  employeeId: number;
  amount: number;
  iban: string;
  status: string;
  wpsReference: string;
}

export interface PayrollApproval {
  id: string;
  payrollRunId: string;
  approvalLevel: string;
  decision: string;
  notes: string;
  decidedByUserId: string | null;
  decidedAtUtc: string | null;
}

export interface PayrollGroup {
  id: string;
  code: string;
  name: string;
  currency: string;
  isActive: boolean;
}

export interface WPSFileBatch {
  id: string;
  paymentBatchId: string;
  sifFileName: string;
  status: string;
  createdAtUtc: string;
}

export interface PayrollSummary {
  totalRuns: number;
  lockedRuns: number;
  totalEmployeesPaid: number;
  totalGrossYtd: number;
  totalNetYtd: number;
}

export const payrollApi = {
  listRuns: (params: { status?: string; page?: number; pageSize?: number } = {}) =>
    client.get<PagedResult<PayrollRun>>('/api/payroll/runs', { params }).then((r) => r.data),

  createRun: (year: number, month: number) =>
    client.post<PayrollRun>('/api/payroll/runs', { year, month }).then((r) => r.data),

  processRun: (id: string) =>
    client.post<PayrollRun>(`/api/payroll/runs/${id}/process`).then((r) => r.data),

  lockRun: (id: string) =>
    client.post<PayrollRun>(`/api/payroll/runs/${id}/lock`).then((r) => r.data),

  approveRun: (id: string, notes?: string) =>
    client.post<PayrollRun>(`/api/payroll/runs/${id}/approve`, { notes }).then((r) => r.data),

  slips: (runId: string, params: { page?: number; pageSize?: number } = {}) =>
    client.get<PagedResult<PayrollSlip>>(`/api/payroll/runs/${runId}/slips`, { params }).then((r) => r.data),

  validateRun: (id: string) =>
    client.post<PayrollValidationResult[]>(`/api/payroll/runs/${id}/validate`).then((r) => r.data),

  listValidationResults: (runId: string) =>
    client.get<PayrollValidationResult[]>(`/api/payroll/runs/${runId}/validate`).then((r) => r.data).catch(() => [] as PayrollValidationResult[]),

  runApprovals: (runId: string) =>
    client.get<PayrollApproval[]>(`/api/payroll/runs/${runId}/approvals`).then((r) => r.data),

  generatePayslips: (id: string) =>
    client.post<Payslip[]>(`/api/payroll/runs/${id}/payslips/generate`).then((r) => r.data),

  listPayslips: (runId: string, params: { page?: number; pageSize?: number } = {}) =>
    client.get<PagedResult<Payslip>>(`/api/payroll/runs/${runId}/payslips`, { params }).then((r) => r.data),

  createPaymentBatch: (runId: string, paymentMethod = 'WPS', currency = 'AED') =>
    client.post<PayrollPaymentBatch>(`/api/payroll/runs/${runId}/payment-batches`, { paymentMethod, currency }).then((r) => r.data),

  listPaymentBatches: (runId?: string) =>
    client.get<PayrollPaymentBatch[]>('/api/payroll/payment-batches', { params: runId ? { runId } : undefined }).then((r) => r.data),

  paymentRecords: (batchId: string) =>
    client.get<PayrollPaymentRecord[]>(`/api/payroll/payment-batches/${batchId}/records`).then((r) => r.data),

  generateWpsFile: (batchId: string) =>
    client.post<WPSFileBatch>(`/api/payroll/payment-batches/${batchId}/wps-file`).then((r) => r.data),

  listSalaryStructures: () =>
    client.get<SalaryStructure[]>('/api/payroll/salary-structures').then((r) => r.data),

  createSalaryStructure: (payload: unknown) =>
    client.post<SalaryStructure>('/api/payroll/salary-structures', payload).then((r) => r.data),

  listEmployeeSalaryStructures: (employeeId?: number) =>
    client.get<EmployeeSalaryStructure[]>('/api/payroll/employee-salary-structures', { params: employeeId ? { employeeId } : undefined }).then((r) => r.data),

  assignEmployeeSalary: (payload: unknown) =>
    client.post<EmployeeSalaryStructure>('/api/payroll/employee-salary-structures', payload).then((r) => r.data),

  listGroups: () =>
    client.get<PayrollGroup[]>('/api/payroll/groups').then((r) => r.data),

  createGroup: (code: string, name: string, currency = 'AED') =>
    client.post<PayrollGroup>('/api/payroll/groups', { code, name, currency }).then((r) => r.data),

  reportRegister: (runId: string) =>
    client.get<PayrollSlip[]>('/api/payroll/reports/register', { params: { runId } }).then((r) => r.data),

  reportSummary: () =>
    client.get<PayrollSummary>('/api/payroll/reports/summary').then((r) => r.data),

  aiValidation: (runId: string) =>
    client.get('/api/payroll/ai-validation', { params: { runId } }).then((r) => r.data),
};
