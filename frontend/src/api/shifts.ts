import client from './client';

export interface ShiftDefinition {
  id: string;
  tenantId: string;
  code: string;
  name: string;
  startTime: string;
  endTime: string;
  breakMinutes: number;
  color: string;
  isActive: boolean;
  createdAtUtc: string;
}

export interface RosterEmployee {
  id: number;
  fullName: string;
  department: string;
  employeeCode: string;
}

export interface RosterAssignment {
  id: string;
  employeeId: number;
  date: string;
  shiftDefinitionId: string;
  shiftName: string;
  shiftCode: string;
  shiftColor: string;
}

export interface RosterResponse {
  from: string;
  to: string;
  employees: RosterEmployee[];
  assignments: RosterAssignment[];
}

export const shiftsApi = {
  listDefinitions: () =>
    client.get<ShiftDefinition[]>('/api/shifts/definitions').then((r) => r.data),

  createDefinition: (body: {
    code: string;
    name: string;
    startTime: string;
    endTime: string;
    breakMinutes: number;
    color: string;
  }) => client.post<ShiftDefinition>('/api/shifts/definitions', body).then((r) => r.data),

  updateDefinition: (
    id: string,
    body: { code: string; name: string; startTime: string; endTime: string; breakMinutes: number; color: string }
  ) => client.put<ShiftDefinition>(`/api/shifts/definitions/${id}`, body).then((r) => r.data),

  deleteDefinition: (id: string) => client.delete(`/api/shifts/definitions/${id}`),

  getRoster: (from: string, to: string) =>
    client.get<RosterResponse>('/api/shifts/roster', { params: { from, to } }).then((r) => r.data),

  assign: (body: { employeeId: number; shiftDefinitionId: string; date: string; notes?: string }) =>
    client.post('/api/shifts/roster/assign', body).then((r) => r.data),

  removeAssignment: (id: string) => client.delete(`/api/shifts/roster/${id}`),

  autoPlan: (body: {
    dateFrom: string;
    dateTo: string;
    shiftIds: string[];
    pattern: 'fixed' | 'alternating' | 'rotating';
    skipWeekend: boolean;
    overwriteExisting: boolean;
    employeeIds?: number[];
  }) => client.post<{ created: number; skipped: number; employees: number; days: number }>('/api/shifts/roster/auto-plan', body).then(r => r.data),
};
