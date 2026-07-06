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
    | "Operations"
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
  /** Permission key required to see this module. Omit for unrestricted access. */
  requiredPermission?: string;
  /** ISO-3166 alpha-2 country codes this module is scoped to. The tenant's
      operating country (companies.country, set by the platform admin when
      provisioning the client) must match one of these for the module to be
      visible or routable. Omit for region-agnostic modules. */
  requiredCountries?: string[];
};

export type UserSession = {
  token: string;
  csrfToken: string;
  user: AnyRecord;
  role: string;
  company: AnyRecord;
  permissions: string[];
};
