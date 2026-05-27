import express from "express";
import { compliancePacks } from "./compliance.registry";

const router = express.Router();

router.get("/packs", (req, res) => {
  res.status(200).json({
    success: true,
    data: compliancePacks,
  });
});

router.get("/packs/:countryCode", (req, res) => {
  const countryCode = req.params.countryCode.toUpperCase();
  const packs = compliancePacks.filter((pack) => pack.countryCode === countryCode);

  res.status(200).json({
    success: true,
    data: packs,
  });
});

export default router;
