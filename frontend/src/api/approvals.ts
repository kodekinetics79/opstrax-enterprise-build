import client from './client';
import type { PagedResult } from './organization';

export interface ApprovalRequest {
  id: string;
  workflowId: string;
  entityName: string;
  entityId: string;
  title: string;
  status: string;
  currentStepOrder: number;
  requestedByUserId: string | null;
  createdAtUtc: string;
  completedAtUtc: string | null;
  decisions: ApprovalDecision[];
}

export interface ApprovalDecision {
  id: string;
  stepOrder: number;
  decision: string;
  comments: string;
  decidedAtUtc: string;
}

export const approvalsApi = {
  list: (params: { status?: string; entityName?: string; page?: number; pageSize?: number } = {}) =>
    client.get<PagedResult<ApprovalRequest>>('/api/approval-requests', { params }).then((r) => r.data),

  get: (id: string) =>
    client.get<ApprovalRequest>(`/api/approval-requests/${id}`).then((r) => r.data),

  decide: (id: string, decision: 'Approved' | 'Rejected', comments = '') =>
    client.post<ApprovalRequest>(`/api/approval-requests/${id}/decisions`, { decision, comments }).then((r) => r.data),
};
