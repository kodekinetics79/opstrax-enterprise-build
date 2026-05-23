import client from './client';

// ── Types ─────────────────────────────────────────────────────────────────────

export interface ManpowerRequisition {
  id: string;
  tenantId: string;
  requisitionNumber: string;
  departmentName: string;
  designationTitle: string;
  headCount: number;
  employmentType: string;
  priority: string;
  justification: string;
  requiredSkills: string;
  minExperienceYears: number | null;
  maxExperienceYears: number | null;
  budgetFrom: number | null;
  budgetTo: number | null;
  targetJoiningDate: string | null;
  status: string;
  requestedByName: string;
  rejectionReason: string;
  createdAtUtc: string;
  submittedAtUtc: string | null;
  approvedAtUtc: string | null;
}

export interface JobOpening {
  id: string;
  jobCode: string;
  requisitionId: string | null;
  title: string;
  departmentName: string;
  designationTitle: string;
  employmentType: string;
  headCount: number;
  filledCount: number;
  description: string;
  requirements: string;
  responsibilities: string;
  salaryFrom: number | null;
  salaryTo: number | null;
  location: string;
  status: string;
  assignedHrName: string;
  createdAtUtc: string;
  activeApplications?: number;
  remaining?: number;
}

export interface Candidate {
  id: string;
  firstName: string;
  lastName: string;
  fullName: string;
  email: string;
  phone: string;
  currentJobTitle: string;
  currentCompany: string;
  totalExperienceYears: number;
  educationLevel: string;
  nationality: string;
  linkedInUrl: string;
  source: string;
  status: string;
  tags: string;
  createdAtUtc: string;
  applicationCount?: number;
}

export interface JobApplication {
  id: string;
  jobOpeningId: string;
  jobTitle: string;
  candidateId: string;
  candidateName: string;
  candidateEmail: string;
  stage: string;
  stageOrder: number;
  status: string;
  rejectionReason: string;
  offeredSalary: number | null;
  appliedAtUtc: string;
  stageChangedAtUtc: string | null;
  hiredAtUtc: string | null;
  onboardingDraftId: string | null;
}

export interface ApplicationEvent {
  id: string;
  eventType: string;
  stage: string;
  notes: string;
  performedByName: string;
  createdAtUtc: string;
}

export interface InterviewSchedule {
  id: string;
  applicationId: string;
  interviewType: string;
  interviewerNames: string;
  scheduledAt: string;
  durationMinutes: number;
  mode: string;
  meetingLink: string;
  location: string;
  status: string;
  overallRating: number | null;
  recommendation: string;
  feedbackNotes: string;
  completedAt: string | null;
  createdAtUtc: string;
}

export interface OfferLetter {
  id: string;
  applicationId: string;
  candidateName: string;
  offeredJobTitle: string;
  offeredDepartment: string;
  startDate: string;
  basicSalary: number;
  housingAllowance: number;
  transportAllowance: number;
  otherAllowances: number;
  grossSalary: number;
  probationMonths: number;
  status: string;
  generatedAtUtc: string;
  sentAtUtc: string | null;
  responseDeadline: string | null;
  acceptedAtUtc: string | null;
}

export interface KanbanStage {
  stage: string;
  order: number;
  applications: JobApplication[];
}

export interface ApplicationDetail {
  application: JobApplication;
  candidate: Candidate | null;
  events: ApplicationEvent[];
  interviews: InterviewSchedule[];
  offer: OfferLetter | null;
}

export interface RecruitmentStats {
  openPositions: number;
  totalOpenings: number;
  activeApplications: number;
  hiredThisMonth: number;
  offersPending: number;
}

export interface RequisitionStats {
  total: number;
  draft: number;
  pending: number;
  approved: number;
  converted: number;
}

// ── API ───────────────────────────────────────────────────────────────────────

export const requisitionsApi = {
  list: (params: { status?: string; priority?: string; page?: number; pageSize?: number } = {}) =>
    client.get<{ items: ManpowerRequisition[]; total: number }>('/api/recruitment/requisitions', { params }).then(r => r.data),

  get: (id: string) =>
    client.get<ManpowerRequisition>(`/api/recruitment/requisitions/${id}`).then(r => r.data),

  create: (body: {
    departmentId?: string; departmentName: string; designationId?: string; designationTitle: string;
    headCount: number; employmentType: string; priority: string; justification: string;
    requiredSkills: string; minExperienceYears?: number; maxExperienceYears?: number;
    budgetFrom?: number; budgetTo?: number; targetJoiningDate?: string; requestedByName: string;
  }) => client.post<ManpowerRequisition>('/api/recruitment/requisitions', body).then(r => r.data),

  submit: (id: string) =>
    client.post<ManpowerRequisition>(`/api/recruitment/requisitions/${id}/submit`).then(r => r.data),

  approve: (id: string) =>
    client.post<ManpowerRequisition>(`/api/recruitment/requisitions/${id}/approve`, {}).then(r => r.data),

  reject: (id: string, reason: string) =>
    client.post<ManpowerRequisition>(`/api/recruitment/requisitions/${id}/reject`, { reason }).then(r => r.data),

  stats: () =>
    client.get<RequisitionStats>('/api/recruitment/requisitions/stats').then(r => r.data),
};

export const openingsApi = {
  list: (params: { status?: string; page?: number } = {}) =>
    client.get<{ items: JobOpening[]; total: number }>('/api/recruitment/openings', { params }).then(r => r.data),

  get: (id: string) =>
    client.get<{ opening: JobOpening; stageCounts: { stage: string; count: number }[] }>(`/api/recruitment/openings/${id}`).then(r => r.data),

  create: (body: {
    requisitionId?: string; title: string; departmentName: string; designationTitle: string;
    employmentType: string; headCount: number; description: string; requirements: string;
    responsibilities: string; salaryFrom?: number; salaryTo?: number; location: string; assignedHrName: string;
  }) => client.post<JobOpening>('/api/recruitment/openings', body).then(r => r.data),

  close: (id: string) =>
    client.post<JobOpening>(`/api/recruitment/openings/${id}/close`).then(r => r.data),

  stats: () =>
    client.get<RecruitmentStats>('/api/recruitment/openings/stats').then(r => r.data),
};

export const candidatesApi = {
  list: (params: { search?: string; status?: string; page?: number; pageSize?: number } = {}) =>
    client.get<{ items: Candidate[]; total: number }>('/api/recruitment/candidates', { params }).then(r => r.data),

  create: (body: {
    firstName: string; lastName: string; email: string; phone: string;
    currentJobTitle: string; currentCompany: string; totalExperienceYears: number;
    educationLevel: string; nationality: string; linkedInUrl: string; source: string; tags: string;
  }) => client.post<Candidate>('/api/recruitment/candidates', body).then(r => r.data),
};

export const applicationsApi = {
  kanban: (jobOpeningId: string) =>
    client.get<{ stages: KanbanStage[]; rejected: JobApplication[] }>(`/api/recruitment/applications/kanban/${jobOpeningId}`).then(r => r.data),

  get: (id: string) =>
    client.get<ApplicationDetail>(`/api/recruitment/applications/${id}`).then(r => r.data),

  apply: (jobOpeningId: string, candidateId: string, notes?: string) =>
    client.post<JobApplication>('/api/recruitment/applications', { jobOpeningId, candidateId, notes }).then(r => r.data),

  advance: (id: string, notes?: string) =>
    client.post<JobApplication>(`/api/recruitment/applications/${id}/advance`, { notes }).then(r => r.data),

  reject: (id: string, reason: string) =>
    client.post<JobApplication>(`/api/recruitment/applications/${id}/reject`, { reason }).then(r => r.data),

  addNote: (id: string, notes: string) =>
    client.post(`/api/recruitment/applications/${id}/notes`, { notes }).then(r => r.data),

  scheduleInterview: (id: string, body: {
    interviewType: string; interviewerNames: string; scheduledAt: string;
    durationMinutes: number; mode: string; meetingLink: string; location: string;
  }) => client.post<InterviewSchedule>(`/api/recruitment/applications/${id}/interviews`, body).then(r => r.data),

  recordFeedback: (interviewId: string, body: { overallRating: number; recommendation: string; feedbackNotes: string }) =>
    client.post<InterviewSchedule>(`/api/recruitment/applications/interviews/${interviewId}/feedback`, body).then(r => r.data),

  generateOffer: (id: string, body: {
    department: string; startDate: string; basicSalary: number;
    housingAllowance: number; transportAllowance: number; otherAllowances: number; probationMonths: number;
  }) => client.post<OfferLetter>(`/api/recruitment/applications/${id}/offer`, body).then(r => r.data),

  sendOffer: (offerId: string) =>
    client.post<OfferLetter>(`/api/recruitment/applications/offers/${offerId}/send`).then(r => r.data),

  acceptOffer: (offerId: string) =>
    client.post(`/api/recruitment/applications/offers/${offerId}/accept`).then(r => r.data),

  declineOffer: (offerId: string, reason: string) =>
    client.post(`/api/recruitment/applications/offers/${offerId}/decline`, { reason }).then(r => r.data),

  getOfferHtml: (offerId: string) =>
    client.get<string>(`/api/recruitment/applications/offers/${offerId}/html`, { responseType: 'text' }).then(r => r.data),
};
