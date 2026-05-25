import client from './client';

// ── Loan Types ────────────────────────────────────────────────────────────────

export interface LoanType {
  id: string;
  code: string;
  nameEn: string;
  nameAr: string;
  maxAmount: number;
  maxInstallments: number;
  repaymentFrequency: string;
  isInterestFree: boolean;
  interestRate: number;
  minServiceMonths: number;
  requiresApproval: boolean;
  isActive: boolean;
}

export interface EmployeeLoan {
  id: string;
  employeeId: string;
  employeeName: string;
  loanTypeId: string;
  loanTypeName: string;
  loanNumber: string;
  requestedAmount: number;
  approvedAmount: number;
  requestedInstallments: number;
  approvedInstallments: number;
  installmentAmount: number;
  repaymentFrequency: string;
  disbursementDate?: string;
  repaymentStartDate?: string;
  totalRepaid: number;
  outstandingBalance: number;
  status: string;
  rejectionReason?: string;
  notes: string;
  isLockedByPayroll: boolean;
  createdAtUtc: string;
}

export interface LoanApproval {
  id: string;
  loanId: string;
  stepOrder: number;
  approverRole: string;
  approvedByName: string;
  status: string;
  comments: string;
  decidedAtUtc?: string;
}

export interface LoanInstallment {
  id: string;
  loanId: string;
  installmentNumber: number;
  dueDate: string;
  amountDue: number;
  amountPaid: number;
  status: string;
  paidDate?: string;
}

// ── Advance Types ─────────────────────────────────────────────────────────────

export interface AdvancePolicy {
  id: string;
  policyName: string;
  maxPercentageOfSalary: number;
  maxAdvancesPerYear: number;
  minServiceMonths: number;
  allowInstallments: boolean;
  maxInstallments: number;
  cooldownMonths: number;
  requiresApproval: boolean;
  isActive: boolean;
}

export interface SalaryAdvance {
  id: string;
  employeeId: string;
  employeeName: string;
  advanceNumber: string;
  requestedAmount: number;
  approvedAmount: number;
  repaymentType: string;
  installments: number;
  installmentAmount: number;
  repaymentStartDate?: string;
  totalRepaid: number;
  outstandingBalance: number;
  reason: string;
  status: string;
  rejectionReason?: string;
  isLockedByPayroll: boolean;
  createdAtUtc: string;
}

export interface AdvanceInstallment {
  id: string;
  advanceId: string;
  installmentNumber: number;
  dueDate: string;
  amountDue: number;
  amountPaid: number;
  status: string;
}

// ── Bonus Types ───────────────────────────────────────────────────────────────

export interface BonusType {
  id: string;
  code: string;
  nameEn: string;
  nameAr: string;
  calculationMethod: string;
  isTaxable: boolean;
  isActive: boolean;
}

export interface BonusBatch {
  id: string;
  bonusTypeId: string;
  bonusTypeName: string;
  batchNumber: string;
  batchName: string;
  paymentPeriod: string;
  paymentDate: string;
  totalAmount: number;
  employeeCount: number;
  status: string;
  notes: string;
  isLockedByPayroll: boolean;
  createdAtUtc: string;
}

export interface EmployeeBonus {
  id: string;
  bonusBatchId: string;
  employeeId: string;
  employeeName: string;
  department: string;
  bonusTypeName: string;
  basicSalary: number;
  calculationMethod: string;
  calculationValue: number;
  bonusAmount: number;
  paymentPeriod: string;
  status: string;
  notes: string;
}

// ── API clients ───────────────────────────────────────────────────────────────

export const loanTypesApi = {
  list: () =>
    client.get<LoanType[]>('/api/finance/loans/types').then(r => r.data),
  create: (body: { code: string; nameEn: string; nameAr?: string; maxAmount: number; maxInstallments: number; repaymentFrequency: string; isInterestFree: boolean; interestRate: number; minServiceMonths: number; requiresApproval: boolean }) =>
    client.post<LoanType>('/api/finance/loans/types', body).then(r => r.data),
};

export const loansApi = {
  list: (params: { employeeId?: string; status?: string; page?: number; pageSize?: number } = {}) =>
    client.get<{ total: number; items: EmployeeLoan[] }>('/api/finance/loans', { params }).then(r => r.data),

  get: (id: string) =>
    client.get<{ loan: EmployeeLoan; installments: LoanInstallment[]; approvals: LoanApproval[] }>(`/api/finance/loans/${id}`).then(r => r.data),

  create: (body: { employeeId: string; employeeName: string; loanTypeId: string; requestedAmount: number; requestedInstallments: number; notes?: string }) =>
    client.post<EmployeeLoan>('/api/finance/loans', body).then(r => r.data),

  settle: (id: string, body: { settlementType: string; settlementAmount: number; settlementDate: string; notes?: string }) =>
    client.patch<{ loan: EmployeeLoan }>(`/api/finance/loans/${id}/settle`, body).then(r => r.data),

  getInstallments: (id: string) =>
    client.get<LoanInstallment[]>(`/api/finance/loans/${id}/installments`).then(r => r.data),

  decide: (loanId: string, approvalId: string, body: { decision: string; comments?: string; approvedAmount?: number; approvedInstallments?: number; repaymentStartDate?: string }) =>
    client.patch(`/api/finance/loans/${loanId}/approvals/${approvalId}/decide`, body).then(r => r.data),
};

export const advancePolicyApi = {
  get: () =>
    client.get<AdvancePolicy | null>('/api/finance/advances/policy').then(r => r.data),
  upsert: (body: Partial<AdvancePolicy>) =>
    client.post<AdvancePolicy>('/api/finance/advances/policy', body).then(r => r.data),
};

export const advancesApi = {
  list: (params: { employeeId?: string; status?: string; page?: number; pageSize?: number } = {}) =>
    client.get<{ total: number; items: SalaryAdvance[] }>('/api/finance/advances', { params }).then(r => r.data),

  get: (id: string) =>
    client.get<{ advance: SalaryAdvance; installments: AdvanceInstallment[] }>(`/api/finance/advances/${id}`).then(r => r.data),

  create: (body: { employeeId: string; employeeName: string; requestedAmount: number; repaymentType: string; installments: number; repaymentStartDate?: string; reason?: string }) =>
    client.post<SalaryAdvance>('/api/finance/advances', body).then(r => r.data),

  approve: (id: string, body: { approvedAmount: number; installments: number; repaymentStartDate?: string }) =>
    client.patch<SalaryAdvance>(`/api/finance/advances/${id}/approve`, body).then(r => r.data),

  reject: (id: string, reason?: string) =>
    client.patch<SalaryAdvance>(`/api/finance/advances/${id}/reject`, { reason }).then(r => r.data),
};

export const bonusTypesApi = {
  list: () =>
    client.get<BonusType[]>('/api/finance/bonuses/types').then(r => r.data),
  create: (body: { code: string; nameEn: string; nameAr?: string; calculationMethod: string; isTaxable: boolean }) =>
    client.post<BonusType>('/api/finance/bonuses/types', body).then(r => r.data),
};

export const bonusBatchesApi = {
  list: (params: { status?: string; page?: number; pageSize?: number } = {}) =>
    client.get<{ total: number; items: BonusBatch[] }>('/api/finance/bonuses/batches', { params }).then(r => r.data),

  get: (id: string) =>
    client.get<{ batch: BonusBatch; bonuses: EmployeeBonus[] }>(`/api/finance/bonuses/batches/${id}`).then(r => r.data),

  create: (body: { bonusTypeId: string; batchName: string; paymentPeriod: string; paymentDate: string; notes?: string }) =>
    client.post<BonusBatch>('/api/finance/bonuses/batches', body).then(r => r.data),

  addEmployee: (batchId: string, body: { employeeId: string; employeeName: string; department?: string; basicSalary: number; calculationMethod: string; calculationValue: number; notes?: string }) =>
    client.post<EmployeeBonus>(`/api/finance/bonuses/batches/${batchId}/employees`, body).then(r => r.data),

  removeEmployee: (batchId: string, bonusId: string) =>
    client.delete(`/api/finance/bonuses/batches/${batchId}/employees/${bonusId}`),

  submit: (id: string) =>
    client.patch<BonusBatch>(`/api/finance/bonuses/batches/${id}/submit`).then(r => r.data),

  approve: (id: string, comments?: string) =>
    client.patch<BonusBatch>(`/api/finance/bonuses/batches/${id}/approve`, { comments }).then(r => r.data),

  reject: (id: string, reason?: string) =>
    client.patch<BonusBatch>(`/api/finance/bonuses/batches/${id}/reject`, { reason }).then(r => r.data),

  payrollPending: (paymentPeriod: string) =>
    client.get<{ count: number; totalAmount: number; bonuses: EmployeeBonus[] }>('/api/finance/bonuses/payroll-pending', { params: { paymentPeriod } }).then(r => r.data),
};
