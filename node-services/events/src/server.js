import 'dotenv/config';
import express from 'express';
import cors from 'cors';
import helmet from 'helmet';
import { WebSocketServer } from 'ws';
import { nanoid } from 'nanoid';
import { pool, getTenantId, getVehicleAndDriver } from './db.js';

const app = express();
const port = Number(process.env.PORT || 8090);
const corsOrigin = process.env.CORS_ORIGIN || 'http://localhost:10000';

app.use(helmet());
app.use(cors({ origin: corsOrigin }));
app.use(express.json({ limit: '2mb' }));

const server = app.listen(port, () => {
  console.log(`OpsTrax Node Event Service listening on ${port}`);
});

const wss = new WebSocketServer({ server, path: '/ws' });

function broadcast(payload) {
  const message = JSON.stringify(payload);
  for (const client of wss.clients) {
    if (client.readyState === 1) client.send(message);
  }
}

app.get('/health', (_req, res) => {
  res.json({ status: 'healthy', service: 'opstrax-node-events', utc: new Date().toISOString() });
});

app.post('/telemetry/location', async (req, res, next) => {
  try {
    const {
      tenantCode = 'KK-DEMO',
      vehicleCode,
      driverCode,
      lat,
      lng,
      speedMph = 0,
      heading = 0,
      eventType = 'LOCATION',
    } = req.body;

    if (!vehicleCode || lat === undefined || lng === undefined) {
      return res.status(400).json({ error: 'vehicleCode, lat, and lng are required.' });
    }

    const tenantId = await getTenantId(tenantCode);
    const { vehicleId, driverId } = await getVehicleAndDriver({ tenantId, vehicleCode, driverCode });

    const [result] = await pool.execute(
      `INSERT INTO location_events
        (tenant_id, vehicle_id, driver_id, vehicle_code, driver_code, lat, lng, speed_mph, heading, event_type)
       VALUES
        (:tenantId, :vehicleId, :driverId, :vehicleCode, :driverCode, :lat, :lng, :speedMph, :heading, :eventType)`,
      { tenantId, vehicleId, driverId, vehicleCode, driverCode: driverCode ?? null, lat, lng, speedMph, heading, eventType }
    );

    const event = {
      id: result.insertId,
      eventId: nanoid(),
      tenantCode,
      vehicleCode,
      driverCode,
      lat,
      lng,
      speedMph,
      heading,
      eventType,
      eventTime: new Date().toISOString(),
    };

    broadcast({ type: 'LOCATION_EVENT', data: event });
    res.status(201).json(event);
  } catch (error) {
    next(error);
  }
});

app.post('/events/safety', async (req, res, next) => {
  try {
    const {
      tenantCode = 'KK-DEMO',
      vehicleCode,
      driverCode,
      eventType,
      severity = 'Low',
      description = '',
    } = req.body;

    if (!eventType) return res.status(400).json({ error: 'eventType is required.' });

    const tenantId = await getTenantId(tenantCode);
    const { vehicleId, driverId } = await getVehicleAndDriver({ tenantId, vehicleCode, driverCode });

    const [result] = await pool.execute(
      `INSERT INTO safety_events
        (tenant_id, vehicle_id, driver_id, event_type, severity, description)
       VALUES
        (:tenantId, :vehicleId, :driverId, :eventType, :severity, :description)`,
      { tenantId, vehicleId, driverId, eventType, severity, description }
    );

    const payload = { id: result.insertId, tenantCode, vehicleCode, driverCode, eventType, severity, description };
    broadcast({ type: 'SAFETY_EVENT', data: payload });
    res.status(201).json(payload);
  } catch (error) {
    next(error);
  }
});

app.post('/ai/generate-daily-brief', async (req, res, next) => {
  try {
    const { tenantCode = 'KK-DEMO' } = req.body;
    const tenantId = await getTenantId(tenantCode);

    const [[vehicleStats]] = await pool.execute(
      `SELECT COUNT(*) activeVehicles FROM vehicles WHERE tenant_id = :tenantId AND status <> 'Inactive'`,
      { tenantId }
    );
    const [[jobStats]] = await pool.execute(
      `SELECT COUNT(*) atRiskJobs FROM jobs WHERE tenant_id = :tenantId AND status IN ('At Risk', 'Delayed')`,
      { tenantId }
    );
    const [[maintenanceStats]] = await pool.execute(
      `SELECT COUNT(*) openWorkOrders FROM maintenance_work_orders WHERE tenant_id = :tenantId AND status <> 'Closed'`,
      { tenantId }
    );

    const title = 'AI daily fleet brief generated';
    const body = `Today, ${vehicleStats.activeVehicles} vehicles are active, ${jobStats.atRiskJobs} jobs require attention, and ${maintenanceStats.openWorkOrders} maintenance work orders remain open. Review dispatch exceptions and prioritize high-risk routes first.`;

    const [result] = await pool.execute(
      `INSERT INTO ai_insights (tenant_id, insight_type, title, body, severity)
       VALUES (:tenantId, 'Daily Brief', :title, :body, 'Info')`,
      { tenantId, title, body }
    );

    const payload = { id: result.insertId, title, body, severity: 'Info' };
    broadcast({ type: 'AI_INSIGHT', data: payload });
    res.status(201).json(payload);
  } catch (error) {
    next(error);
  }
});

app.use((error, _req, res, _next) => {
  console.error(error);
  res.status(500).json({ error: 'Internal service error', detail: error.message });
});
