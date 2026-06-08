import express from "express";
import cors from "cors";
import helmet from "helmet";
import morgan from "morgan";
import type { Request, Response, NextFunction } from "express";

import healthRoutes from "./modules/health/health.routes";
import tenantConfigRoutes from "./modules/tenant-config/tenantConfig.routes";
import complianceRoutes from "./modules/compliance/compliance.routes";
import deviceRoutes from "./modules/devices/device.routes";
import industryRoutes from "./modules/industry/industry.routes";
import telemetryRoutes from "./modules/telemetry/telemetry.routes";

import { errorHandler } from "./middleware/errorHandler";

export const app = express();
const requestWindows = new Map<string, { windowStart: number; count: number }>();
const RATE_LIMIT_WINDOW_MS = Number(process.env.RATE_LIMIT_WINDOW_MS || 60_000);
const RATE_LIMIT_MAX_REQUESTS = Number(process.env.RATE_LIMIT_MAX_REQUESTS || 240);
const allowedOrigins = (process.env.FRONTEND_URL || "http://localhost:10000")
  .split(",")
  .map((origin) => origin.trim())
  .filter(Boolean);

app.use(helmet());
app.use(
  cors({
    origin: allowedOrigins,
    credentials: true,
  })
);
app.use(express.json({ limit: "5mb" }));
app.use(morgan("dev"));
app.use((req: Request, res: Response, next: NextFunction) => {
  res.setHeader("X-Content-Type-Options", "nosniff");
  res.setHeader("X-Frame-Options", "DENY");
  res.setHeader("Referrer-Policy", "strict-origin-when-cross-origin");
  res.setHeader("Permissions-Policy", "camera=(), microphone=(), geolocation=()");
  next();
});

app.use((req: Request, res: Response, next: NextFunction) => {
  if (!req.path.startsWith("/api")) return next();

  if (
    req.path === "/api/health" ||
    req.path === "/api/ready" ||
    req.path === "/api/auth/login" ||
    (req.method === "GET" && req.path.startsWith("/api/customer-eta/track/"))
  ) {
    return next();
  }

  const key = req.ip || req.socket.remoteAddress || "unknown";
  const now = Date.now();
  const window = requestWindows.get(key);
  if (!window || now - window.windowStart > RATE_LIMIT_WINDOW_MS) {
    requestWindows.set(key, { windowStart: now, count: 1 });
    return next();
  }

  window.count += 1;
  if (window.count > RATE_LIMIT_MAX_REQUESTS) {
    return res.status(429).json({
      success: false,
      message: "Too many requests",
      errors: ["Rate limit exceeded"],
    });
  }

  return next();
});

app.use("/api/health", healthRoutes);
app.get("/api/ready", async (_req, res) => {
  res.status(200).json({
    success: true,
    data: {
      service: "fleet-backend",
      status: "ready",
      dependencies: {
        database: "not-configured",
      },
      timestamp: new Date().toISOString(),
    },
    message: "Ready",
    errors: [],
  });
});
app.use("/api/tenant", tenantConfigRoutes);
app.use("/api/compliance", complianceRoutes);
app.use("/api/devices", deviceRoutes);
app.use("/api/industry-modules", industryRoutes);
app.use("/api/telemetry", telemetryRoutes);

app.use(errorHandler);
