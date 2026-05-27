export interface DeviceTypeDefinition {
  code: string;
  name: string;
  category: string;
  description: string;
  capabilities: string[];
}

export interface DeviceModel {
  manufacturer: string;
  model: string;
  deviceType: string;
  supportedProtocols: string[];
  supportedCapabilities: string[];
  countryApprovalStatus: Record<string, string>;
}
