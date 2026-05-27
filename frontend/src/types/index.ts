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
  group: "Command" | "Fleet" | "Dispatch" | "Safety" | "Maintenance" | "Compliance" | "Finance" | "Intelligence" | "Platform";
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
