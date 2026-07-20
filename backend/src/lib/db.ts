import { Pool, type PoolConfig, type QueryResultRow } from "pg";
import { getEnv } from "./env";

type ConnectionConfig = PoolConfig & {
  connectionString?: string;
};

function sslConfigRequested() {
  const allowUnverified =
    process.env.NODE_ENV === "development" &&
    process.env.PGSSL_REJECT_UNAUTHORIZED?.toLowerCase() === "false";
  return { rejectUnauthorized: !allowUnverified };
}

export function parseDotNetConnectionString(input: string): ConnectionConfig {
  const config: ConnectionConfig = {};
  for (const segment of input.split(";")) {
    const trimmed = segment.trim();
    if (!trimmed) continue;
    const index = trimmed.indexOf("=");
    if (index <= 0) continue;
    const key = trimmed.slice(0, index).trim().toLowerCase();
    const value = trimmed.slice(index + 1).trim();

    if (key === "host") config.host = value;
    if (key === "port") config.port = Number(value);
    if (key === "database") config.database = value;
    if (key === "username" || key === "user id" || key === "user") config.user = value;
    if (key === "password") config.password = value;
    if (key === "ssl mode" && value.toLowerCase() === "require") {
      config.ssl = sslConfigRequested();
    }
  }
  return config;
}

export function buildPoolConfig(): ConnectionConfig {
  const env = getEnv();
  const raw = env.DATABASE_URL || env.PG_CONNECTION;
  if (raw) {
    if (/^postgres(ql)?:\/\//i.test(raw)) {
      return {
        connectionString: raw,
        ssl: /[?&]sslmode=require(?:&|$)/i.test(raw) ? sslConfigRequested() : undefined,
      };
    }
    return parseDotNetConnectionString(raw);
  }

  return {
    host: process.env.PGHOST,
    port: process.env.PGPORT ? Number(process.env.PGPORT) : undefined,
    database: process.env.PGDATABASE,
    user: process.env.PGUSER,
    password: process.env.PGPASSWORD,
    ssl: process.env.PGSSLMODE?.toLowerCase() === "require" ? sslConfigRequested() : undefined,
  };
}

export const pool = new Pool({
  ...buildPoolConfig(),
  application_name: "opstrax-backend",
  max: 10,
});

export async function query<T extends QueryResultRow = QueryResultRow>(
  text: string,
  params: unknown[] = []
): Promise<T[]> {
  const result = await pool.query<T>(text, params);
  return result.rows;
}

export async function queryOne<T extends QueryResultRow = QueryResultRow>(
  text: string,
  params: unknown[] = []
): Promise<T | null> {
  const rows = await query<T>(text, params);
  return rows[0] ?? null;
}

export async function pingDatabase() {
  const startedAt = Date.now();
  const row = await queryOne<{ now: string }>("SELECT NOW()::text AS now");
  return {
    ok: Boolean(row),
    latencyMs: Date.now() - startedAt,
    timestamp: row?.now ?? new Date().toISOString(),
  };
}

export async function ensureBackendColumns() {
  await query(`
    ALTER TABLE integrations
      ADD COLUMN IF NOT EXISTS integration_key VARCHAR(120),
      ADD COLUMN IF NOT EXISTS description TEXT,
      ADD COLUMN IF NOT EXISTS logo VARCHAR(80),
      ADD COLUMN IF NOT EXISTS sync_label VARCHAR(80),
      ADD COLUMN IF NOT EXISTS last_sync_at TIMESTAMPTZ,
      ADD COLUMN IF NOT EXISTS related_systems_json JSONB,
      ADD COLUMN IF NOT EXISTS connected_to_json JSONB,
      ADD COLUMN IF NOT EXISTS managed_by VARCHAR(120),
      ADD COLUMN IF NOT EXISTS scope VARCHAR(30) NOT NULL DEFAULT 'tenant',
      ADD COLUMN IF NOT EXISTS config_json JSONB NOT NULL DEFAULT '{}'::jsonb,
      ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW();
  `);

  await query(`
    ALTER TABLE user_sessions
      ADD COLUMN IF NOT EXISTS refresh_token VARCHAR(128),
      ADD COLUMN IF NOT EXISTS csrf_token VARCHAR(128);
  `);

  await query(`
    CREATE UNIQUE INDEX IF NOT EXISTS ix_user_sessions_refresh_token
    ON user_sessions (refresh_token)
    WHERE refresh_token IS NOT NULL;
  `);
}
