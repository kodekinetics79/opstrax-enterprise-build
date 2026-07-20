import express from "express";
import { z } from "zod";
import { buildTenantRuntimeConfig } from "./tenantConfig.service";
import {
  requirePermission,
  resolveTenantId,
  tenantMatchesAuthenticatedUser,
} from "../../middleware/sessionAuth";

const router = express.Router();

export const configureTenantSchema = z
  .object({
    tenantId: z.union([z.string().trim().min(1).max(64), z.number().int().positive()]).optional(),
    primaryCountry: z.enum(["US", "CA", "SA", "AE", "CUSTOM"]),
    operatingCountries: z
      .array(z.enum(["US", "CA", "SA", "AE", "CUSTOM"]))
      .optional(),
    industries: z.array(
      z.enum([
        "logistics",
        "cold_chain",
        "school_transport",
        "construction",
        "oil_gas",
        "rental_fleet",
        "delivery_fleet",
      ])
    ),
    enabledDeviceTypes: z.array(
      z.enum([
        "obd_ii",
        "j1939_can",
        "gps_tracker",
        "dashcam",
        "temperature_sensor",
        "fuel_sensor",
        "ble_rfid_driver_id",
        "tire_pressure_sensor",
      ])
    ),
  })
  .strict();

router.post("/configure", async (req, res, next) => {
  try {
    const auth = await requirePermission(req, res, "settings:manage");
    if (!auth) return;
    const tenantId = resolveTenantId(req, auth);
    const parsed = configureTenantSchema.safeParse(req.body);

    if (!parsed.success) {
      return res.status(400).json({
        success: false,
        message: "Invalid tenant configuration request.",
        errors: parsed.error.flatten(),
      });
    }

    if (!tenantMatchesAuthenticatedUser(parsed.data.tenantId, tenantId)) {
      return res.status(403).json({
        success: false,
        message: "Forbidden",
        errors: ["Configuration tenant must match the authenticated tenant"],
      });
    }

    const runtimeConfig = buildTenantRuntimeConfig({
      ...parsed.data,
      tenantId: String(tenantId),
    });

    return res.status(200).json({
      success: true,
      data: runtimeConfig,
    });
  } catch (error) {
    next(error);
  }
});

export default router;
