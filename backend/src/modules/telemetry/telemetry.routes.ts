import express from "express";
import { z } from "zod";
import type { TelemetryEvent } from "./telemetry.types";
import {
  requirePermission,
  resolveTenantId,
  tenantMatchesAuthenticatedUser,
} from "../../middleware/sessionAuth";

const router = express.Router();

const telemetryEvents: TelemetryEvent[] = [];

const finiteNumber = z.number().finite();
const optionalTenantId = z
  .union([z.string().trim().min(1).max(64), z.number().int().positive()])
  .optional();

export const telemetryEventSchema = z
  .object({
    tenantId: optionalTenantId,
    vehicleId: z.string().trim().min(1).max(128),
    driverId: z.string().trim().min(1).max(128).optional(),
    deviceId: z.string().trim().min(1).max(128),
    deviceType: z.string().trim().min(1).max(80),
    providerName: z.string().trim().min(1).max(120),
    timestamp: z.string().datetime({ offset: true }).optional(),
    countryCode: z.string().trim().regex(/^[A-Za-z]{2,3}$/),
    location: z
      .object({
        latitude: finiteNumber.min(-90).max(90),
        longitude: finiteNumber.min(-180).max(180),
        speed: finiteNumber.min(0).max(500).optional(),
        heading: finiteNumber.min(0).max(360).optional(),
      })
      .strict()
      .optional(),
    engine: z
      .object({
        ignitionStatus: z.boolean().optional(),
        rpm: finiteNumber.min(0).max(20_000).optional(),
        odometer: finiteNumber.min(0).optional(),
        engineHours: finiteNumber.min(0).optional(),
        fuelLevel: finiteNumber.min(0).max(100).optional(),
        diagnosticCodes: z.array(z.string().trim().min(1).max(64)).max(100).optional(),
      })
      .strict()
      .optional(),
    safety: z
      .object({
        harshBrake: z.boolean().optional(),
        harshAcceleration: z.boolean().optional(),
        collisionDetected: z.boolean().optional(),
        driverDistraction: z.boolean().optional(),
        seatbeltStatus: z.boolean().optional(),
      })
      .strict()
      .optional(),
    coldChain: z
      .object({
        temperature: finiteNumber.min(-100).max(200).optional(),
        humidity: finiteNumber.min(0).max(100).optional(),
        doorOpen: z.boolean().optional(),
        reeferStatus: z.string().trim().min(1).max(80).optional(),
      })
      .strict()
      .optional(),
    tires: z
      .object({
        pressure: finiteNumber.min(0).max(300).optional(),
        temperature: finiteNumber.min(-100).max(300).optional(),
        tirePosition: z.string().trim().min(1).max(40).optional(),
      })
      .strict()
      .optional(),
    rawPayload: z.record(z.unknown()).optional(),
  })
  .strict();

router.post("/ingest", async (req, res, next) => {
  try {
    const auth = await requirePermission(req, res, "telemetry.devices.manage");
    if (!auth) return;
    const tenantId = resolveTenantId(req, auth);
    const parsed = telemetryEventSchema.safeParse(req.body);

    if (!parsed.success) {
      return res.status(400).json({
        success: false,
        message: "Invalid telemetry event.",
        errors: parsed.error.issues.map((issue) => issue.message),
      });
    }

    if (!tenantMatchesAuthenticatedUser(parsed.data.tenantId, tenantId)) {
      return res.status(403).json({
        success: false,
        message: "Forbidden",
        errors: ["Telemetry tenant must match the authenticated tenant"],
      });
    }

    const event: TelemetryEvent = {
      ...parsed.data,
      tenantId: String(tenantId),
      countryCode: parsed.data.countryCode.toUpperCase(),
      timestamp: parsed.data.timestamp || new Date().toISOString(),
    };

    telemetryEvents.push(event);

    const generatedAlerts = [];

    if (
      event.coldChain?.temperature !== undefined &&
      (event.coldChain.temperature < 2 || event.coldChain.temperature > 8)
    ) {
      generatedAlerts.push({
        type: "COLD_CHAIN_TEMP_OUT_OF_RANGE",
        severity: "critical",
        message: "Temperature is outside the configured cold-chain range.",
      });
    }

    if (event.safety?.harshBrake) {
      generatedAlerts.push({
        type: "HARSH_BRAKING",
        severity: "warning",
        message: "Harsh braking event detected.",
      });
    }

    return res.status(201).json({
      success: true,
      message: "Telemetry event received.",
      data: {
        event,
        generatedAlerts,
      },
    });
  } catch (error) {
    next(error);
  }
});

router.get("/vehicle/:vehicleId", async (req, res, next) => {
  try {
    const auth = await requirePermission(req, res, "telemetry.live_state.read");
    if (!auth) return;
    const tenantId = resolveTenantId(req, auth);
    const vehicleId = z.string().trim().min(1).max(128).safeParse(req.params.vehicleId);
    if (!vehicleId.success) {
      return res.status(400).json({
        success: false,
        message: "Invalid vehicle id.",
        errors: vehicleId.error.issues.map((issue) => issue.message),
      });
    }

    const events = telemetryEvents.filter(
      (event) => event.tenantId === String(tenantId) && event.vehicleId === vehicleId.data
    );

    return res.status(200).json({
      success: true,
      data: events,
    });
  } catch (error) {
    next(error);
  }
});

export default router;
