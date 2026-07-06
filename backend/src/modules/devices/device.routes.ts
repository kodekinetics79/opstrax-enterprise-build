import express from "express";
import { z } from "zod";
import { deviceTypes } from "./device.registry";
import {
  requirePermission,
  resolveTenantId,
  tenantMatchesAuthenticatedUser,
} from "../../middleware/sessionAuth";

const router = express.Router();

const deviceTypeCodes = [
  "obd_ii",
  "j1939_can",
  "gps_tracker",
  "dashcam",
  "temperature_sensor",
  "fuel_sensor",
  "ble_rfid_driver_id",
  "tire_pressure_sensor",
] as const;

export const registerDeviceSchema = z
  .object({
    tenantId: z.union([z.string().trim().min(1).max(64), z.number().int().positive()]).optional(),
    vehicleId: z.string().trim().min(1).max(128),
    deviceType: z.enum(deviceTypeCodes),
    manufacturer: z.string().trim().min(1).max(120),
    model: z.string().trim().min(1).max(120),
    imei: z.string().trim().regex(/^[0-9]{14,16}$/).optional(),
    simNumber: z.string().trim().min(5).max(32).regex(/^[+0-9A-Za-z-]+$/).optional(),
    approvalCountry: z.string().trim().min(2).max(3).regex(/^[A-Za-z]+$/),
  })
  .strict();

router.get("/types", (req, res) => {
  res.status(200).json({
    success: true,
    data: deviceTypes,
  });
});

router.post("/register", async (req, res, next) => {
  try {
    const auth = await requirePermission(req, res, "telemetry.devices.manage");
    if (!auth) return;
    const tenantId = resolveTenantId(req, auth);
    const parsed = registerDeviceSchema.safeParse(req.body);

    if (!parsed.success) {
      return res.status(400).json({
        success: false,
        message: "Invalid device registration request.",
        errors: parsed.error.issues.map((issue) => issue.message),
      });
    }

    if (!tenantMatchesAuthenticatedUser(parsed.data.tenantId, tenantId)) {
      return res.status(403).json({
        success: false,
        message: "Forbidden",
        errors: ["Device tenant must match the authenticated tenant"],
      });
    }

    const {
      vehicleId,
      deviceType,
      manufacturer,
      model,
      imei,
      simNumber,
      approvalCountry,
    } = parsed.data;

    return res.status(201).json({
      success: true,
      message: "Device registered successfully.",
      data: {
        id: `device-${Date.now()}`,
        tenantId: String(tenantId),
        vehicleId,
        deviceType,
        manufacturer,
        model,
        imei,
        simNumber,
        approvalCountry: approvalCountry.toUpperCase(),
        status: "active",
        approvalStatus: "to_be_verified",
        createdAt: new Date().toISOString(),
      },
    });
  } catch (error) {
    next(error);
  }
});

export default router;
