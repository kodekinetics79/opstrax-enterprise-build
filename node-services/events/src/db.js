import mysql from 'mysql2/promise';

export const pool = mysql.createPool({
  host: process.env.MYSQL_HOST || 'localhost',
  port: Number(process.env.MYSQL_PORT || 3306),
  database: process.env.MYSQL_DATABASE || 'opstrax',
  user: process.env.MYSQL_USER || 'opstrax_user',
  password: process.env.MYSQL_PASSWORD || 'opstrax_password',
  waitForConnections: true,
  connectionLimit: 10,
  namedPlaceholders: true,
});

export async function getTenantId(tenantCode = 'KK-DEMO') {
  const [rows] = await pool.execute('SELECT id FROM tenants WHERE tenant_code = :tenantCode LIMIT 1', { tenantCode });
  if (!rows.length) throw new Error(`Tenant not found: ${tenantCode}`);
  return rows[0].id;
}

export async function getVehicleAndDriver({ tenantId, vehicleCode, driverCode }) {
  const [vehicles] = await pool.execute(
    'SELECT id FROM vehicles WHERE tenant_id = :tenantId AND vehicle_code = :vehicleCode LIMIT 1',
    { tenantId, vehicleCode }
  );
  const [drivers] = await pool.execute(
    'SELECT id FROM drivers WHERE tenant_id = :tenantId AND driver_code = :driverCode LIMIT 1',
    { tenantId, driverCode }
  );

  return {
    vehicleId: vehicles[0]?.id ?? null,
    driverId: drivers[0]?.id ?? null,
  };
}
