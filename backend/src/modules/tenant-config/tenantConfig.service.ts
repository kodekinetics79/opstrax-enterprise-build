import {
  CompliancePackCode,
  CountryCode,
  DeviceType,
  IndustryCode,
  ModuleCode,
  TenantRuntimeConfig,
} from "./types";

import {
  countryDefaults,
  countryModules,
  deviceRequiredModules,
  industryModules,
} from "./tenantConfig.registry";

export interface BuildTenantConfigInput {
  tenantId: string;
  primaryCountry: CountryCode;
  operatingCountries?: CountryCode[];
  industries: IndustryCode[];
  enabledDeviceTypes: DeviceType[];
}

export function buildTenantRuntimeConfig(
  input: BuildTenantConfigInput
): TenantRuntimeConfig {
  const countrySetting = countryDefaults[input.primaryCountry];

  if (!countrySetting) {
    throw new Error(`Unsupported country: ${input.primaryCountry}`);
  }

  const modules = new Set<ModuleCode>();
  const compliancePacks = new Set<CompliancePackCode>();

  countryModules[input.primaryCountry].forEach((module) => modules.add(module));
  compliancePacks.add(countrySetting.compliancePack);

  input.industries.forEach((industry) => {
    const moduleList = industryModules[industry] || [];
    moduleList.forEach((module) => modules.add(module));
  });

  input.enabledDeviceTypes.forEach((deviceType) => {
    const moduleList = deviceRequiredModules[deviceType] || [];
    moduleList.forEach((module) => modules.add(module));
  });

  return {
    tenantId: input.tenantId,
    operatingCountries: input.operatingCountries || [input.primaryCountry],
    primaryCountry: input.primaryCountry,
    industries: input.industries,
    enabledDeviceTypes: input.enabledDeviceTypes,
    enabledCompliancePacks: Array.from(compliancePacks),
    enabledModules: Array.from(modules),
    languages: countrySetting.languages,
    currency: countrySetting.currency,
    timezone: countrySetting.timezone,
    distanceUnit: countrySetting.distanceUnit,
    fuelUnit: countrySetting.fuelUnit,
  };
}
