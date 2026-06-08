import express from "express";

const router = express.Router();

router.get("/", (req, res) => {
  res.status(200).json({
    success: true,
    data: {
      service: "fleet-backend",
      status: "healthy",
      timestamp: new Date().toISOString(),
    },
    message: "Health check passed",
    errors: [],
  });
});

export default router;
