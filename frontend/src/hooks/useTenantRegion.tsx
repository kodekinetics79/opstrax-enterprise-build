import { type ReactNode } from "react";
import { Globe2 } from "lucide-react";
import { useAuth } from "./useAuth";
import type { ModuleConfig } from "@/types";

export { GCC_COUNTRIES } from "@/modules/moduleConfig";

const COUNTRY_LABELS: Record<string, string> = {
  SA: "Saudi Arabia",
  AE: "United Arab Emirates",
  QA: "Qatar",
  KW: "Kuwait",
  BH: "Bahrain",
  OM: "Oman",
  CA: "Canada",
  US: "United States",
};

export function countryLabel(code: string | null): string {
  if (!code) return "Not set";
  return COUNTRY_LABELS[code] ?? code;
}

/** The tenant's operating country (ISO alpha-2), assigned by the platform admin
    when the client was provisioned. Null when the tenant has no region yet. */
export function useTenantCountry(): string | null {
  const { session } = useAuth();
  const raw = session?.company?.country;
  const code = typeof raw === "string" ? raw.trim().toUpperCase() : "";
  return code.length === 2 ? code : null;
}

export function useTenantCurrency(): string | null {
  const { session } = useAuth();
  const raw = session?.company?.currency;
  const code = typeof raw === "string" ? raw.trim().toUpperCase() : "";
  return code || null;
}

export function moduleAvailableForCountry(module: Pick<ModuleConfig, "requiredCountries">, country: string | null): boolean {
  if (!module.requiredCountries?.length) return true;
  return country !== null && module.requiredCountries.includes(country);
}

/** Route guard for region-scoped modules. Renders the child only when the
    tenant's operating country is in `countries`; otherwise explains why the
    module is unavailable instead of exposing another market's compliance UI. */
export function RequireRegion({ countries, moduleTitle, children }: { countries: string[]; moduleTitle: string; children: ReactNode }) {
  const country = useTenantCountry();
  if (country && countries.includes(country)) return <>{children}</>;

  return (
    <div className="grid min-h-[60vh] place-items-center px-4">
      <div className="panel max-w-xl p-8 text-center">
        <div className="mx-auto flex h-14 w-14 items-center justify-center rounded-2xl border border-sky-400/20 bg-sky-400/10 text-sky-600">
          <Globe2 className="h-7 w-7" />
        </div>
        <p className="mt-5 text-[11px] font-bold uppercase tracking-[0.22em] text-sky-600">
          Regional module
        </p>
        <h1 className="mt-2 text-2xl font-bold tracking-tight text-slate-900">
          {moduleTitle} is not enabled for this workspace
        </h1>
        <p className="mt-3 text-sm leading-6 text-slate-500">
          This module serves tenants operating in {countries.map((code) => countryLabel(code)).join(", ")}.
          Your workspace region is <span className="font-semibold text-slate-700">{countryLabel(country)}</span>.
          The operating region is assigned by your platform administrator when the
          account is provisioned and drives which country compliance packs appear here.
        </p>
      </div>
    </div>
  );
}
