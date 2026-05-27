import express from "express";
import cors from "cors";
import helmet from "helmet";
import morgan from "morgan";

import healthRoutes from "./modules/health/health.routes";
import tenantConfigRoutes from "./modules/tenant-config/tenantConfig.routes";
import complianceRoutes from "./modules/compliance/compliance.routes";
import deviceRoutes from "./modules/devices/device.routes";
import industryRoutes from "./modules/industry/industry.routes";
import telemetryRoutes from "./modules/telemetry/telemetry.routes";

import { errorHandler } from "./middleware/errorHandler";

export const app = express();

app.use(helmet());
app.use(
  cors({
    origin: process.env.FRONTEND_URL || "*",
    credentials: true,
  })
);
app.use(express.json({ limit: "5mb" }));
app.use(morgan("dev"));

app.use("/api/health", healthRoutes);
app.use("/api/tenant", tenantConfigRoutes);
app.use("/api/compliance", complianceRoutes);
app.use("/api/devices", deviceRoutes);
app.use("/api/industry-modules", industryRoutes);
app.use("/api/telemetry", telemetryRoutes);

app.use(errorHandler);
