import express from "express";
import { TelemetryEvent } from "./telemetry.types";

const router = express.Router();

const telemetryEvents: TelemetryEvent[] = [];

router.post("/ingest", (req, res) => {
  const event: TelemetryEvent = {
    ...req.body,
    timestamp: req.body.timestamp || new Date().toISOString(),
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

  res.status(201).json({
    success: true,
    message: "Telemetry event received.",
    data: {
      event,
      generatedAlerts,
    },
  });
});

router.get("/vehicle/:vehicleId", (req, res) => {
  const events = telemetryEvents.filter(
    (event) => event.vehicleId === req.params.vehicleId
  );

  res.status(200).json({
    success: true,
    data: events,
  });
});

export default router;
