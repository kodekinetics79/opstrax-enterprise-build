import express from "express";
import { pingDatabase } from "../../lib/db";
import { ok, fail } from "../../lib/httpEnvelope";

const router = express.Router();

router.get("/", async (_req, res) => {
  try {
    const db = await pingDatabase();
    res.status(db.ok ? 200 : 503).json(
      ok(
        {
          service: "fleet-backend",
          status: db.ok ? "healthy" : "degraded",
          database: {
            status: db.ok ? "connected" : "unavailable",
            latencyMs: db.latencyMs,
            timestamp: db.timestamp,
          },
          timestamp: new Date().toISOString(),
        },
        db.ok ? "Health check passed" : "Database health check failed"
      )
    );
  } catch (error) {
    res.status(503).json(fail("Health check failed", [error instanceof Error ? error.message : "Database not reachable"]));
  }
});

export default router;
