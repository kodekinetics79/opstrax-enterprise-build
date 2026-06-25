import { query, queryOne } from "../../lib/db";
import { integrationCatalog } from "./integrations.registry";
import type {
  IntegrationActivity,
  IntegrationActivityStatus,
  IntegrationCategory,
  IntegrationRecord,
  IntegrationsPayload,
  IntegrationsSummary,
} from "./integrations.types";

type IntegrationDbRow = {
  id: string | number;
  companyId: string | number;
  providerName: string;
  category: IntegrationCategory;
  status: string;
  integrationKey: string | null;
  description: string | null;
  logo: string | null;
  syncLabel: string | null;
  lastSyncAt: string | null;
  relatedSystemsJson: unknown;
  connectedToJson: unknown;
  managedBy: string | null;
  scope: string | null;
  configJson: unknown;
  updatedAt: string | null;
};

type AuditRow = {
  id: string | number;
  actionName: string;
  entityName: string | null;
  entityId: string | number | null;
  detailsJson: unknown;
  createdAt: string;
};

function clone<T>(value: T): T {
  return structuredClone(value);
}

function nowIso() {
  return new Date().toISOString();
}

function parseJsonArray(value: unknown) {
  if (Array.isArray(value)) return value;
  if (typeof value === "string") {
    try {
      const parsed = JSON.parse(value);
      return Array.isArray(parsed) ? parsed : [];
    } catch {
      return [];
    }
  }
  return [];
}

function parseJsonObject(value: unknown) {
  if (value && typeof value === "object" && !Array.isArray(value)) return value as Record<string, unknown>;
  if (typeof value === "string") {
    try {
      const parsed = JSON.parse(value);
      return parsed && typeof parsed === "object" && !Array.isArray(parsed) ? (parsed as Record<string, unknown>) : {};
    } catch {
      return {};
    }
  }
  return {};
}

function formatSyncLabel(lastSyncAt: string | null) {
  if (!lastSyncAt) return "—";
  const diffMs = Date.now() - new Date(lastSyncAt).getTime();
  if (!Number.isFinite(diffMs) || diffMs < 0) return "Just now";
  const minutes = Math.floor(diffMs / 60000);
  if (minutes < 1) return "Just now";
  if (minutes < 60) return `${minutes} min ago`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours}h ago`;
  const days = Math.floor(hours / 24);
  return `${days}d ago`;
}

function summarize(records: IntegrationRecord[]): IntegrationsSummary {
  return {
    total: records.length,
    connected: records.filter((record) => record.status === "Connected").length,
    pending: records.filter((record) => record.status === "Pending").length,
    errors: records.filter((record) => record.status === "Error").length,
    categories: new Set(records.map((record) => record.category)).size,
    lastUpdated: records.reduce((latest, record) => {
      if (!record.lastSyncAt) return latest;
      return !latest || record.lastSyncAt > latest ? record.lastSyncAt : latest;
    }, "") || nowIso(),
  };
}

async function ensureTenantIntegrations(tenantId: number) {
  for (const catalogItem of integrationCatalog) {
    await query(
      `
      INSERT INTO integrations (
        company_id,
        provider_name,
        category,
        status,
        integration_key,
        description,
        logo,
        sync_label,
        last_sync_at,
        related_systems_json,
        connected_to_json,
        managed_by,
        scope,
        config_json
      )
      SELECT
        $1::bigint,
        $2::varchar,
        $3::varchar,
        $4::varchar,
        $5::varchar,
        $6::text,
        $7::varchar,
        $8::varchar,
        $9::timestamptz,
        $10::jsonb,
        $11::jsonb,
        $12::varchar,
        $13::varchar,
        $14::jsonb
      WHERE NOT EXISTS (
        SELECT 1 FROM integrations
        WHERE company_id = $1::bigint AND integration_key = $5::varchar
      )
      `,
      [
        tenantId,
        catalogItem.name,
        catalogItem.category,
        catalogItem.status,
        catalogItem.key,
        catalogItem.description,
        catalogItem.logo,
        catalogItem.sync,
        catalogItem.lastSyncAt,
        JSON.stringify(catalogItem.relatedSystems),
        JSON.stringify(catalogItem.connectedTo),
        catalogItem.managedBy,
        catalogItem.scope,
        JSON.stringify(catalogItem.config || {}),
      ]
    );
  }
}

async function loadRows(tenantId: number) {
  await ensureTenantIntegrations(tenantId);

  const rows = await query<IntegrationDbRow>(
    `
    SELECT
      id,
      company_id AS "companyId",
      provider_name AS "providerName",
      category,
      status,
      integration_key AS "integrationKey",
      description,
      logo,
      sync_label AS "syncLabel",
      last_sync_at AS "lastSyncAt",
      related_systems_json AS "relatedSystemsJson",
      connected_to_json AS "connectedToJson",
      managed_by AS "managedBy",
      scope,
      config_json AS "configJson",
      updated_at AS "updatedAt"
    FROM integrations
    WHERE company_id = $1
    ORDER BY category, provider_name
    `,
    [tenantId]
  );

  return rows;
}

function mergeRecord(row: IntegrationDbRow): IntegrationRecord {
  const catalogItem = integrationCatalog.find((item) => item.key === row.integrationKey || item.name === row.providerName);
  const relatedSystems = parseJsonArray(row.relatedSystemsJson);
  const connectedTo = parseJsonArray(row.connectedToJson);
  const config = parseJsonObject(row.configJson);

  return {
    id: Number(row.id),
    key: row.integrationKey ?? catalogItem?.key ?? row.providerName.toLowerCase().replace(/\s+/g, "-"),
    name: row.providerName,
    category: row.category,
    description: row.description ?? catalogItem?.description ?? "",
    logo: row.logo ?? catalogItem?.logo ?? row.providerName.slice(0, 3).toUpperCase(),
    status: row.status as IntegrationRecord["status"],
    sync: row.syncLabel ?? catalogItem?.sync ?? formatSyncLabel(row.lastSyncAt),
    lastSyncAt: row.lastSyncAt ?? catalogItem?.lastSyncAt ?? null,
    relatedSystems: relatedSystems.length ? relatedSystems.map(String) : catalogItem?.relatedSystems ?? [],
    connectedTo: connectedTo.length ? connectedTo.map(String) : catalogItem?.connectedTo ?? [],
    managedBy: row.managedBy ?? catalogItem?.managedBy ?? "Operations",
    scope: (row.scope as IntegrationRecord["scope"]) ?? catalogItem?.scope ?? "tenant",
    tenantId: Number(row.companyId),
    config: (Object.keys(config).length ? config : catalogItem?.config ?? {}) as IntegrationRecord["config"],
  };
}

async function loadActivity(tenantId: number): Promise<IntegrationActivity[]> {
  const rows = await query<AuditRow>(
    `
    SELECT
      id,
      action_name AS "actionName",
      entity_name AS "entityName",
      entity_id AS "entityId",
      details_json AS "detailsJson",
      created_at AS "createdAt"
    FROM audit_logs
    WHERE company_id = $1
      AND (
        entity_name = 'integration'
        OR action_name LIKE 'integration.%'
      )
    ORDER BY created_at DESC
    LIMIT 50
    `,
    [tenantId]
  );

  return rows.map((row, index) => {
    const details = parseJsonObject(row.detailsJson);
    const event = String(details.event ?? details.message ?? row.actionName.replace(/^integration\./, "").replace(/\./g, " "));
    const integrationName = String(details.integrationName ?? details.providerName ?? details.name ?? "Integration");
    const records = Number(details.records ?? 0);
    const status =
      String(details.status ?? "").toLowerCase() === "success"
        ? "Success"
        : String(details.status ?? "").toLowerCase() === "error"
        ? "Error"
        : "Pending";

    return {
      id: Number(row.id) || index + 1,
      integrationId: Number(row.entityId ?? 0) || index + 1,
      integration: integrationName,
      event,
      ts: row.createdAt,
      status: status as IntegrationActivityStatus,
      records,
      details: String(details.details ?? details.description ?? ""),
    };
  });
}

async function addActivity(
  tenantId: number,
  integration: IntegrationRecord,
  actionName: string,
  status: "Success" | "Pending" | "Error",
  records: number,
  details: string,
  event: string
) {
  await query(
    `
    INSERT INTO audit_logs (
      company_id,
      action_name,
      entity_name,
      entity_id,
      actor_name,
      details_json
    ) VALUES ($1, $2, 'integration', $3, $4, $5)
    `,
    [
      tenantId,
      actionName,
      integration.id,
      integration.managedBy,
      JSON.stringify({
        integrationId: integration.id,
        integrationName: integration.name,
        providerName: integration.name,
        status,
        records,
        details,
        event,
        description: integration.description,
      }),
    ]
  );
}

export async function getIntegrationsPayload(tenantId: number): Promise<IntegrationsPayload> {
  const rows = await loadRows(tenantId);
  const records = rows.map(mergeRecord);
  const activity = await loadActivity(tenantId);
  return {
    moduleKey: "integrations",
    tenantId,
    summary: summarize(records),
    records,
    activity,
  };
}

export async function getIntegrationDetail(tenantId: number, id: number) {
  const rows = await loadRows(tenantId);
  const record = rows.map(mergeRecord).find((item) => item.id === id);
  if (!record) throw new Error("Integration not found");
  const activity = (await loadActivity(tenantId)).filter((item) => item.integrationId === id);
  return {
    record,
    activity,
  };
}

async function updateIntegration(
  tenantId: number,
  id: number,
  patch: Partial<Pick<IntegrationRecord, "status" | "sync" | "lastSyncAt" | "config">>
) {
  const record = await queryOne<IntegrationDbRow>(
    `
    SELECT
      id,
      company_id AS "companyId",
      provider_name AS "providerName",
      category,
      status,
      integration_key AS "integrationKey",
      description,
      logo,
      sync_label AS "syncLabel",
      last_sync_at AS "lastSyncAt",
      related_systems_json AS "relatedSystemsJson",
      connected_to_json AS "connectedToJson",
      managed_by AS "managedBy",
      scope,
      config_json AS "configJson",
      updated_at AS "updatedAt"
    FROM integrations
    WHERE company_id = $1 AND id = $2
    LIMIT 1
    `,
    [tenantId, id]
  );

  if (!record) {
    throw new Error("Integration not found");
  }

  const nextConfig = patch.config ?? parseJsonObject(record.configJson);
  const nextStatus = patch.status ?? (record.status as IntegrationRecord["status"]);
  const nextSync = patch.sync ?? record.syncLabel ?? formatSyncLabel(patch.lastSyncAt ?? record.lastSyncAt);
  const nextLastSyncAt = patch.lastSyncAt ?? record.lastSyncAt;

  await query(
    `
    UPDATE integrations
    SET status = $1,
        sync_label = $2,
        last_sync_at = $3,
        config_json = $4,
        updated_at = NOW()
    WHERE company_id = $5 AND id = $6
    `,
    [nextStatus, nextSync, nextLastSyncAt, JSON.stringify(nextConfig), tenantId, id]
  );

  const merged = mergeRecord({
    ...record,
    status: nextStatus,
    syncLabel: nextSync,
    lastSyncAt: nextLastSyncAt ?? null,
    configJson: nextConfig,
  });

  return merged;
}

export async function connectIntegration(tenantId: number, id: number) {
  const record = await updateIntegration(tenantId, id, {
    status: "Connected",
    sync: "Just now",
    lastSyncAt: nowIso(),
  });

  await addActivity(
    tenantId,
    record,
    "integration.connect",
    "Success",
    1,
    `Tenant ${tenantId} connector ${record.key} moved to Connected.`,
    `${record.name} connection confirmed and tenant sync is live.`
  );

  return {
    record,
    activity: await loadActivity(tenantId),
  };
}

export async function disconnectIntegration(tenantId: number, id: number) {
  const record = await updateIntegration(tenantId, id, {
    status: "Disconnected",
    sync: "—",
  });

  await addActivity(
    tenantId,
    record,
    "integration.disconnect",
    "Pending",
    0,
    `Connector ${record.key} disconnected for tenant ${tenantId}.`,
    `${record.name} disconnected from tenant operations.`
  );

  return {
    record,
    activity: await loadActivity(tenantId),
  };
}

export async function configureIntegration(
  tenantId: number,
  id: number,
  config: Record<string, string | number | boolean | null>
) {
  const current = await queryOne<IntegrationDbRow>(
    `
    SELECT
      id,
      company_id AS "companyId",
      provider_name AS "providerName",
      category,
      status,
      integration_key AS "integrationKey",
      description,
      logo,
      sync_label AS "syncLabel",
      last_sync_at AS "lastSyncAt",
      related_systems_json AS "relatedSystemsJson",
      connected_to_json AS "connectedToJson",
      managed_by AS "managedBy",
      scope,
      config_json AS "configJson",
      updated_at AS "updatedAt"
    FROM integrations
    WHERE company_id = $1 AND id = $2
    LIMIT 1
    `,
    [tenantId, id]
  );

  if (!current) {
    throw new Error("Integration not found");
  }

  const merged = mergeRecord({
    ...current,
    configJson: {
      ...parseJsonObject(current.configJson),
      ...config,
    },
    status: current.status as IntegrationRecord["status"],
  });

  await query(
    `
    UPDATE integrations
    SET config_json = $1,
        status = CASE WHEN status = 'Disconnected' THEN 'Pending' ELSE status END,
        updated_at = NOW()
    WHERE company_id = $2 AND id = $3
    `,
    [JSON.stringify(merged.config), tenantId, id]
  );

  await addActivity(
    tenantId,
    merged,
    "integration.configure",
    "Success",
    0,
    `Updated config keys: ${Object.keys(config).join(", ") || "none"}.`,
    `${merged.name} configuration updated.`
  );

  return {
    record: merged,
    activity: await loadActivity(tenantId),
  };
}

export async function syncIntegration(tenantId: number, id: number) {
  const record = await updateIntegration(tenantId, id, {
    status: "Connected",
    sync: "Just now",
    lastSyncAt: nowIso(),
  });

  await addActivity(
    tenantId,
    record,
    "integration.sync",
    "Success",
    1,
    `Connector ${record.key} sync triggered manually.`,
    `${record.name} sync completed successfully.`
  );

  return {
    record,
    activity: await loadActivity(tenantId),
  };
}

export async function listIntegrationCategories(tenantId: number): Promise<IntegrationCategory[]> {
  const payload = await getIntegrationsPayload(tenantId);
  return Array.from(new Set(payload.records.map((record) => record.category)));
}
