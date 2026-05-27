import express from "express";
import { industryModuleDefinitions } from "./industry.registry";

const router = express.Router();

router.get("/", (req, res) => {
  res.status(200).json({
    success: true,
    data: industryModuleDefinitions,
  });
});

export default router;
