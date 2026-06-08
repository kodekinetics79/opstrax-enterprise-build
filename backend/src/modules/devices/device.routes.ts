import express from "express";
import { deviceTypes } from "./device.registry";

const router = express.Router();

router.get("/types", (req, res) => {
  res.status(200).json({
    success: true,
    data: deviceTypes,
  });
});

router.post("/register", (req, res) => {
  const {
    tenantId,
    vehicleId,
    deviceType,
    manufacturer,
    model,
    imei,
    simNumber,
    approvalCountry,
  } = req.body;

  res.status(201).json({
    success: true,
    message: "Device registered successfully.",
    data: {
      id: `device-${Date.now()}`,
      tenantId,
      vehicleId,
      deviceType,
      manufacturer,
      model,
      imei,
      simNumber,
      approvalCountry,
      status: "active",
      approvalStatus: "to_be_verified",
      createdAt: new Date().toISOString(),
    },
  });
});

export default router;
