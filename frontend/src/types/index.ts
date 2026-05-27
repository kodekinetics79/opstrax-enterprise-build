export type ApiEnvelope<T> = {
  success: boolean;
  data: T;
  message: string;
  errors: string[];
};

export type AnyRecord = Record<string, unknown>;

export type Kpi = {
  label: string;
  valueText?: string;
  value?: string | number;
  trend?: string;
  trendValue?: string;
  status?: string;
};

export type ModuleConfig = {
  key: string;
  title: string;
  route: string;
  group:
    | "Control Tower"
    | "CRM & Growth"
    | "Commercial"
    | "Transport Operations"
    | "Fleet"
    | "Telematics & IoT"
    | "Safety & Compliance"
    | "Maintenance"
    | "Financials"
    | "Governance"
    | "Intelligence"
    | "Platform";
  description: string;
  accent: "blue" | "teal" | "amber" | "red" | "green" | "purple";
};

export type UserSession = {
  token: string;
  user: AnyRecord;
  role: string;
  company: AnyRecord;
  permissions: string[];
};
