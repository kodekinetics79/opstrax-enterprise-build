import client from './client';

export interface PolicyDocument {
  id: string;
  originalName: string;
  mimeType: string;
  fileSizeBytes: number;
  status: 'Processing' | 'Ready' | 'Failed';
  chunkCount: number;
  errorMessage?: string;
  createdAtUtc: string;
}

export interface PolicyAskResponse {
  answer: string;
  sources: string[];
  isGrounded: boolean;
}

export const policyDocumentsApi = {
  list: () => client.get<PolicyDocument[]>('/api/ai/policy/documents').then(r => r.data),
  upload: (file: File) => {
    const form = new FormData();
    form.append('file', file);
    return client.post<PolicyDocument>('/api/ai/policy/documents/upload', form, {
      headers: { 'Content-Type': 'multipart/form-data' },
    }).then(r => r.data);
  },
  delete: (id: string) => client.delete(`/api/ai/policy/documents/${id}`),
  ask: (question: string) =>
    client.post<PolicyAskResponse>('/api/ai/policy/ask', { question }).then(r => r.data),
};
