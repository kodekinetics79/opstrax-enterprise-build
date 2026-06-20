import client from './client';

export interface JurisdictionOption {
  code: string;
  label: string;
}

export interface CountryPackOption {
  countryCode: string;
  nameEn: string;
  nameAr: string;
  jurisdictions: JurisdictionOption[];
}

export interface StatutorySummary {
  countryCode: string;
  jurisdiction: string;
  countryNameEn: string;
  countryNameAr: string;
  socialInsuranceScheme: string;
  socialInsuranceDescription: string;
  eosbFormula: string;
  wpsFormat: string;
  wpsFormatLabel: string;
  nationalizationScheme: string;
  currencyCode: string;
  currencySymbol: string;
  localeCode: string;
  isRtl: boolean;
  calendarSystem: string;
}

export interface StatutoryRuleDto {
  id: string;
  countryCode: string;
  jurisdiction: string;
  ruleKey: string;
  ruleValue: string;
  dataType: string;
  description: string;
  effectiveFrom: string;
  effectiveTo: string | null;
  isTenantOverride: boolean;
}

export interface CreateStatutoryRuleRequest {
  countryCode: string;
  jurisdiction: string;
  ruleKey: string;
  ruleValue: string;
  dataType: string;
  description: string;
  effectiveFrom: string;
  effectiveTo?: string | null;
}

export interface UpdateStatutoryRuleRequest {
  ruleValue: string;
  description: string;
  effectiveFrom: string;
  effectiveTo?: string | null;
}

export const countryPacksApi = {
  available: () =>
    client.get<CountryPackOption[]>('/api/country-packs/available').then((r) => r.data),

  statutorySummary: (companyId: string) =>
    client.get<StatutorySummary>(`/api/country-packs/company/${companyId}/statutory-summary`).then((r) => r.data),
};

export const statutoryRulesApi = {
  list: (countryCode?: string, jurisdiction?: string) =>
    client
      .get<StatutoryRuleDto[]>('/api/statutory-rules', { params: { countryCode, jurisdiction } })
      .then((r) => r.data),

  create: (data: CreateStatutoryRuleRequest) =>
    client.post<StatutoryRuleDto>('/api/statutory-rules', data).then((r) => r.data),

  update: (id: string, data: UpdateStatutoryRuleRequest) =>
    client.put<StatutoryRuleDto>(`/api/statutory-rules/${id}`, data).then((r) => r.data),

  remove: (id: string) => client.delete(`/api/statutory-rules/${id}`),
};
