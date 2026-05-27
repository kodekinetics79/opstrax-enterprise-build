export interface CompliancePack {
  code: string;
  countryCode: string;
  name: string;
  description: string;
  modules: string[];
  requiredDeviceCapabilities: string[];
  dataPrivacyRequirements: string[];
  reportTemplates: string[];
  approvalTrackingFields: string[];
  disclaimer: string;
}
