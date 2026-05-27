import { CompliancePack } from "./compliance.types";

export const compliancePacks: CompliancePack[] = [
  {
    code: "USA_FMCSA_ELD",
    countryCode: "US",
    name: "USA FMCSA ELD/HOS Compliance Pack",
    description:
      "Supports USA-style ELD/HOS workflows, driver logs, DVIR, and DOT-ready reporting.",
    modules: ["eld_hos", "dvir", "driver_management"],
    requiredDeviceCapabilities: [
      "engine_hours",
      "odometer",
      "gps_location",
      "ignition_status",
      "driver_id",
    ],
    dataPrivacyRequirements: [
      "role_based_access",
      "audit_logs",
      "driver_data_retention_policy",
    ],
    reportTemplates: ["hos_report", "dvir_report", "driver_log_export"],
    approvalTrackingFields: ["device_model", "device_provider", "eld_status"],
    disclaimer:
      "This pack supports USA-style compliance workflows. Final ELD use requires appropriate device/provider compliance validation.",
  },
  {
    code: "CANADA_ELD",
    countryCode: "CA",
    name: "Canada ELD Compliance Pack",
    description:
      "Supports Canadian ELD/HOS workflows, inspection exports, and bilingual configuration.",
    modules: ["eld_hos", "dvir", "driver_management"],
    requiredDeviceCapabilities: [
      "engine_hours",
      "odometer",
      "gps_location",
      "ignition_status",
      "driver_id",
    ],
    dataPrivacyRequirements: [
      "role_based_access",
      "audit_logs",
      "driver_data_retention_policy",
      "bilingual_notice_support",
    ],
    reportTemplates: ["canadian_hos_report", "dvir_report"],
    approvalTrackingFields: [
      "device_model",
      "device_provider",
      "third_party_certification_status",
    ],
    disclaimer:
      "This pack supports Canadian compliance workflows. Production use should be aligned with Canada's third-party-certified ELD requirements.",
  },
  {
    code: "KSA_TGA_WASL_READY",
    countryCode: "SA",
    name: "Saudi Arabia TGA/WASL-Ready Compliance Pack",
    description:
      "Supports Saudi fleet readiness, WASL-ready reporting structure, CST device approval tracking, and PDPL privacy controls.",
    modules: [
      "wasl_ready_reporting",
      "pdpl_privacy_controls",
      "cst_device_approval_tracker",
      "driver_management",
    ],
    requiredDeviceCapabilities: [
      "gps_location",
      "speed",
      "ignition_status",
      "driver_id",
      "device_imei",
      "sim_number",
    ],
    dataPrivacyRequirements: [
      "driver_consent",
      "location_data_retention_policy",
      "role_based_access",
      "audit_logs",
      "cross_border_transfer_controls",
    ],
    reportTemplates: [
      "wasl_ready_event_export",
      "device_approval_status_report",
      "pdpl_audit_report",
    ],
    approvalTrackingFields: [
      "device_model",
      "imei",
      "sim_number",
      "cst_approval_status",
      "wasl_integration_status",
    ],
    disclaimer:
      "This module is designed for Saudi readiness and integration support. Formal approval or WASL integration must be completed through the appropriate approved local channel.",
  },
  {
    code: "UAE_TRANSPORT_READY",
    countryCode: "AE",
    name: "UAE Transport-Ready Compliance Pack",
    description:
      "Supports UAE-ready fleet operations, Arabic/English configuration, privacy controls, and authority-specific customization.",
    modules: ["pdpl_privacy_controls", "driver_management"],
    requiredDeviceCapabilities: [
      "gps_location",
      "speed",
      "ignition_status",
      "driver_id",
    ],
    dataPrivacyRequirements: [
      "driver_consent",
      "role_based_access",
      "audit_logs",
      "data_retention_policy",
    ],
    reportTemplates: ["uae_fleet_activity_report", "privacy_audit_report"],
    approvalTrackingFields: [
      "device_model",
      "device_provider",
      "local_approval_status",
    ],
    disclaimer:
      "This pack is a UAE-ready configuration foundation. Emirate-specific authority integrations should be configured during implementation.",
  },
  {
    code: "CUSTOM_COUNTRY_RULES",
    countryCode: "CUSTOM",
    name: "Custom Country Rules Pack",
    description:
      "Supports private enterprise or country-specific custom compliance requirements.",
    modules: ["fleet_dashboard", "live_map", "driver_management"],
    requiredDeviceCapabilities: ["gps_location"],
    dataPrivacyRequirements: ["role_based_access", "audit_logs"],
    reportTemplates: ["custom_fleet_report"],
    approvalTrackingFields: ["custom_approval_status"],
    disclaimer:
      "This pack is configurable and should be finalized based on the customer’s local legal and operational requirements.",
  },
];
