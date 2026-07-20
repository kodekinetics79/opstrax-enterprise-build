import { apiClient, unwrap } from "@/services/apiClient";
import type { AnyRecord } from "@/types";

export const operationsProofApi = {
  executionSummary: (jobId: number | string) =>
    unwrap<AnyRecord>(apiClient.get(`/api/operations/jobs/${jobId}/execution-summary`)),

  smartAssignmentRecommendations: (jobId: number | string) =>
    unwrap<AnyRecord[]>(apiClient.get(`/api/jobs/${jobId}/smart-assign/recommendations`)),
  recommendSmartAssignment: (jobId: number | string, payload: AnyRecord) =>
    unwrap<AnyRecord>(apiClient.post(`/api/jobs/${jobId}/smart-assign/recommend`, payload)),
  acceptSmartAssignment: (recommendationId: number | string, payload: AnyRecord = {}) =>
    unwrap<AnyRecord>(apiClient.post(`/api/smart-assign/recommendations/${recommendationId}/accept`, payload)),
  rejectSmartAssignment: (recommendationId: number | string, payload: AnyRecord = {}) =>
    unwrap<AnyRecord>(apiClient.post(`/api/smart-assign/recommendations/${recommendationId}/reject`, payload)),

  siteAccess: (jobId: number | string) =>
    unwrap<AnyRecord[]>(apiClient.get(`/api/jobs/${jobId}/site-access`)),
  createSiteAccess: (jobId: number | string, payload: AnyRecord) =>
    unwrap<AnyRecord>(apiClient.post(`/api/jobs/${jobId}/site-access`, payload)),
  updateSiteAccess: (id: number | string, payload: AnyRecord) =>
    unwrap<AnyRecord>(apiClient.patch(`/api/site-access/${id}`, payload)),

  accessDocuments: (jobId: number | string) =>
    unwrap<AnyRecord[]>(apiClient.get(`/api/jobs/${jobId}/access-documents`)),
  createAccessDocument: (jobId: number | string, payload: AnyRecord) =>
    unwrap<AnyRecord>(apiClient.post(`/api/jobs/${jobId}/access-documents`, payload)),
  updateAccessDocumentStatus: (id: number | string, payload: AnyRecord) =>
    unwrap<AnyRecord>(apiClient.patch(`/api/access-documents/${id}/status`, payload)),

  pickupAuthorizations: (jobId: number | string) =>
    unwrap<AnyRecord[]>(apiClient.get(`/api/jobs/${jobId}/pickup-authorizations`)),
  createPickupAuthorization: (jobId: number | string, payload: AnyRecord) =>
    unwrap<AnyRecord>(apiClient.post(`/api/jobs/${jobId}/pickup-authorizations`, payload)),
  updatePickupAuthorization: (id: number | string, payload: AnyRecord) =>
    unwrap<AnyRecord>(apiClient.patch(`/api/pickup-authorizations/${id}`, payload)),

  warehouseHandovers: (jobId: number | string) =>
    unwrap<AnyRecord[]>(apiClient.get(`/api/jobs/${jobId}/warehouse-handovers`)),
  createWarehouseHandover: (jobId: number | string, payload: AnyRecord) =>
    unwrap<AnyRecord>(apiClient.post(`/api/jobs/${jobId}/warehouse-handovers`, payload)),
  updateWarehouseHandover: (id: number | string, payload: AnyRecord) =>
    unwrap<AnyRecord>(apiClient.patch(`/api/warehouse-handovers/${id}`, payload)),

  proofPackages: (jobId: number | string) =>
    unwrap<AnyRecord[]>(apiClient.get(`/api/jobs/${jobId}/proof-packages`)),
  createProofPackage: (jobId: number | string, payload: AnyRecord) =>
    unwrap<AnyRecord>(apiClient.post(`/api/jobs/${jobId}/proof-packages`, payload)),
  proofPackage: (id: number | string) =>
    unwrap<AnyRecord>(apiClient.get(`/api/proof-packages/${id}`)),
  updateProofPackage: (id: number | string, payload: AnyRecord) =>
    unwrap<AnyRecord>(apiClient.patch(`/api/proof-packages/${id}`, payload)),
  submitProofPackage: (id: number | string, payload: AnyRecord = {}) =>
    unwrap<AnyRecord>(apiClient.post(`/api/proof-packages/${id}/submit`, payload)),
  validateProofPackage: (id: number | string, payload: AnyRecord = {}) =>
    unwrap<AnyRecord>(apiClient.post(`/api/proof-packages/${id}/validate`, payload)),

  proofArtifacts: (proofPackageId: number | string) =>
    unwrap<AnyRecord[]>(apiClient.get(`/api/proof-packages/${proofPackageId}/artifacts`)),
  createProofArtifact: (proofPackageId: number | string, payload: AnyRecord) =>
    unwrap<AnyRecord>(apiClient.post(`/api/proof-packages/${proofPackageId}/artifacts`, payload)),
  billingConfidence: (proofPackageId: number | string) =>
    unwrap<AnyRecord>(apiClient.get(`/api/proof-packages/${proofPackageId}/billing-confidence`)),
};
