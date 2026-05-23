import client from './client';
import type { PagedResult } from './organization';

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

export const payrollApi = {
  listRuns: (params: { status?: string; page?: number; pageSize?: number } = {}) =>
    client.get<PagedResult<PayrollRun>>('/api/payroll/runs', { params }).then((r) => r.data),

  createRun: (year: number, month: number) =>
    client.post<PayrollRun>('/api/payroll/runs', { year, month }).then((r) => r.data),

  processRun: (id: string) =>
    client.post<PayrollRun>(`/api/payroll/runs/${id}/process`).then((r) => r.data),

  lockRun: (id: string) =>
    client.post<PayrollRun>(`/api/payroll/runs/${id}/lock`).then((r) => r.data),

  slips: (runId: string, params: { page?: number; pageSize?: number } = {}) =>
    client.get<PagedResult<PayrollSlip>>(`/api/payroll/runs/${runId}/slips`, { params }).then((r) => r.data),
};
