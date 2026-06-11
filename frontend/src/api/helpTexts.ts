import client from './client';

export interface FieldHelpText {
  fieldKey: string;
  text: string;
  updatedAtUtc: string;
}

export const helpTextsApi = {
  list: () =>
    client.get<FieldHelpText[]>('/api/help-texts').then(r => r.data),

  upsert: (fieldKey: string, text: string) =>
    client.put<FieldHelpText>(`/api/help-texts/${encodeURIComponent(fieldKey)}`, { text }).then(r => r.data),

  remove: (fieldKey: string) =>
    client.delete(`/api/help-texts/${encodeURIComponent(fieldKey)}`),
};
