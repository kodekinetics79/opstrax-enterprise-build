import express from "express";
import { z } from "zod";
import { buildTenantRuntimeConfig } from "./tenantConfig.service";

const router = express.Router();

const configureTenantSchema = z.object({
  tenantId: z.string().min(1),
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
});

router.post("/configure", (req, res) => {
  const parsed = configureTenantSchema.safeParse(req.body);

  if (!parsed.success) {
    return res.status(400).json({
      success: false,
      message: "Invalid tenant configuration request.",
      errors: parsed.error.flatten(),
    });
  }

  const runtimeConfig = buildTenantRuntimeConfig(parsed.data);

  return res.status(200).json({
    success: true,
    data: runtimeConfig,
  });
});

export default router;
