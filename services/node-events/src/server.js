import "dotenv/config";
import express from "express";
import cors from "cors";
import helmet from "helmet";

const app = express();
const port = Number(process.env.PORT || 8090);
const corsOrigin = process.env.CORS_ORIGIN || "http://localhost:10000";

app.use(helmet({ contentSecurityPolicy: false }));
app.use(cors({ origin: corsOrigin }));
app.use(express.json({ limit: "2mb" }));

const eventTypes = [
  "location.updated",
  "geofence.entered",
  "geofence.exited",
  "job.created",
  "job.assigned",
  "job.status_changed",
  "job.delayed",
  "route.optimized",
  "vehicle.idle",
  "safety.event",
  "dashcam.event",
  "coaching.created",
  "coaching.completed",
  "incident.created",
  "incident.status_changed",
  "evidence.package_created",
  "evidence.package_locked",
  "insurance.report_created",
  "maintenance.due",
  "maintenance.overdue",
  "maintenance.warning",
  "workorder.created",
  "workorder.status_changed",
  "workorder.completed",
  "dvir.submitted",
  "dvir.critical_defect",
  "dvir.mechanic_reviewed",
  "document.expiring",
  "document.expired",
  "eta.sent",
  "proof.completed",
  "dispatch.recommendation",
  "customer.feedback",
  "fuel.transaction_created",
  "fuel.anomaly_detected",
  "idling.threshold_exceeded",
  "expense.created",
  "expense.approved",
  "expense.rejected",
  "contract.expiring",
  "carrier.compliance_risk",
  "margin.risk_detected",
  "cost.leakage_detected",
  "cost.action_created",
];

const locations = ["Manassas, VA", "Woodbridge, VA", "Alexandria, VA", "Dulles, VA", "Fairfax, VA", "Arlington, VA", "Washington DC"];

function createEvent(index = Date.now()) {
  const type = eventTypes[index % eventTypes.length];
  const location = locations[index % locations.length];
  return {
    id: `${Date.now()}-${index}`,
    type,
    title: `${type.replace(".", " ")} near ${location}`,
    vehicleCode: `${["TRK", "VAN", "BOX", "REEFER"][index % 4]}-${101 + (index % 20)}`,
    severity: ["Info", "Warning", "High", "Critical"][index % 4],
    lat: 38.62 + ((index % 20) * 0.012),
    lng: -77.55 + ((index % 20) * 0.015),
    generatedAt: new Date().toISOString(),
  };
}

app.get("/health", (_req, res) => {
  res.json({ status: "healthy", service: "opstrax-node-events", utc: new Date().toISOString() });
});

app.get("/events/stream", (req, res) => {
  res.setHeader("Content-Type", "text/event-stream");
  res.setHeader("Cache-Control", "no-cache");
  res.setHeader("Connection", "keep-alive");
  res.flushHeaders?.();

  let index = 0;
  const send = () => {
    res.write(`data: ${JSON.stringify(createEvent(index++))}\n\n`);
  };
  send();
  const interval = setInterval(send, 3000);
  req.on("close", () => clearInterval(interval));
});

app.get("/demo/live-feed", (_req, res) => {
  res.json(Array.from({ length: 12 }, (_, index) => createEvent(index)));
});

app.post("/telemetry/location", (req, res) => {
  res.status(201).json({ ...createEvent(1), ...req.body, type: "location.updated" });
});

app.post("/telemetry/safety-event", (req, res) => {
  res.status(201).json({ ...createEvent(5), ...req.body, type: "safety.event" });
});

app.post("/ai/generate-brief", (_req, res) => {
  res.status(201).json({
    title: "OpsTrax AI live brief generated",
    summary: "Dispatch risk, idling leakage, maintenance timing, and customer ETA exposure are the top live operating signals.",
    generatedAt: new Date().toISOString(),
  });
});

app.listen(port, () => {
  console.log(`OpsTrax Node Events listening on ${port}`);
});
