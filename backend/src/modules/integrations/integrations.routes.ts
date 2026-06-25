import express, { type Request, type Response } from "express";
import { z } from "zod";
import {
  configureIntegration,
  connectIntegration,
  disconnectIntegration,
  getIntegrationDetail,
  getIntegrationsPayload,
  syncIntegration,
} from "./integrations.store";
import { requirePermission, resolveTenantId } from "../../middleware/sessionAuth";

const router = express.Router();

function parseIntegrationId(value: string) {
  const id = Number.parseInt(value, 10);
  if (!Number.isFinite(id) || id <= 0) throw new Error("Invalid integration id");
  return id;
}

const configSchema = z.record(z.union([z.string(), z.number(), z.boolean(), z.null()])).default({});

router.get("/", async (req, res, next) => {
  try {
    const auth = await requirePermission(req, res, "integrations:view");
    if (!auth) return;
    const tenantId = resolveTenantId(req, auth);
    const payload = await getIntegrationsPayload(tenantId);

    res.status(200).json({
      success: true,
      message: "Integrations loaded",
      errors: [],
      data: payload,
    });
  } catch (error) {
    next(error);
  }
});

router.get("/activity", async (req, res, next) => {
  try {
    const auth = await requirePermission(req, res, "integrations:view");
    if (!auth) return;
    const tenantId = resolveTenantId(req, auth);
    const payload = await getIntegrationsPayload(tenantId);

    res.status(200).json({
      success: true,
      message: "Integration activity loaded",
      errors: [],
      data: payload.activity,
    });
  } catch (error) {
    next(error);
  }
});

router.get("/:id", async (req, res, next) => {
  try {
    const auth = await requirePermission(req, res, "integrations:view");
    if (!auth) return;
    const tenantId = resolveTenantId(req, auth);
    const id = parseIntegrationId(req.params.id);
    const detail = await getIntegrationDetail(tenantId, id);

    res.status(200).json({
      success: true,
      message: "Integration loaded",
      errors: [],
      data: detail,
    });
  } catch (error) {
    const message = error instanceof Error ? error.message : "Unable to load integration";
    res.status(404).json({
      success: false,
      message,
      errors: [message],
    });
  }
});

router.post("/:id/connect", async (req, res, next) => {
  try {
    const auth = await requirePermission(req, res, "integrations:manage");
    if (!auth) return;
    const tenantId = resolveTenantId(req, auth);
    const id = parseIntegrationId(req.params.id);
    const result = await connectIntegration(tenantId, id);

    res.status(200).json({
      success: true,
      message: "Integration connected",
      errors: [],
      data: result,
    });
  } catch (error) {
    const message = error instanceof Error ? error.message : "Unable to connect integration";
    res.status(400).json({
      success: false,
      message,
      errors: [message],
    });
  }
});

router.post("/:id/configure", async (req, res, next) => {
  try {
    const auth = await requirePermission(req, res, "integrations:manage");
    if (!auth) return;
    const tenantId = resolveTenantId(req, auth);
    const id = parseIntegrationId(req.params.id);
    const parsed = configSchema.safeParse(req.body ?? {});

    if (!parsed.success) {
      return res.status(400).json({
        success: false,
        message: "Invalid integration configuration",
        errors: parsed.error.issues.map((issue) => issue.message),
      });
    }

    const result = await configureIntegration(tenantId, id, parsed.data);

    res.status(200).json({
      success: true,
      message: "Integration configured",
      errors: [],
      data: result,
    });
  } catch (error) {
    const message = error instanceof Error ? error.message : "Unable to configure integration";
    res.status(400).json({
      success: false,
      message,
      errors: [message],
    });
  }
});

router.post("/:id/sync", async (req, res, next) => {
  try {
    const auth = await requirePermission(req, res, "integrations:manage");
    if (!auth) return;
    const tenantId = resolveTenantId(req, auth);
    const id = parseIntegrationId(req.params.id);
    const result = await syncIntegration(tenantId, id);

    res.status(200).json({
      success: true,
      message: "Integration sync completed",
      errors: [],
      data: result,
    });
  } catch (error) {
    const message = error instanceof Error ? error.message : "Unable to sync integration";
    res.status(400).json({
      success: false,
      message,
      errors: [message],
    });
  }
});

router.delete("/:id", async (req, res, next) => {
  try {
    const auth = await requirePermission(req, res, "integrations:manage");
    if (!auth) return;
    const tenantId = resolveTenantId(req, auth);
    const id = parseIntegrationId(req.params.id);
    const result = await disconnectIntegration(tenantId, id);

    res.status(200).json({
      success: true,
      message: "Integration disconnected",
      errors: [],
      data: result,
    });
  } catch (error) {
    const message = error instanceof Error ? error.message : "Unable to disconnect integration";
    res.status(400).json({
      success: false,
      message,
      errors: [message],
    });
  }
});

export default router;
