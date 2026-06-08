import { NextFunction, Request, Response } from "express";

export function errorHandler(
  error: unknown,
  req: Request,
  res: Response,
  next: NextFunction
) {
  console.error(`[fleet-backend] ${req.method} ${req.path}`, error);

  res.status(500).json({
    success: false,
    message: "Internal server error.",
    errors: ["An unexpected server error occurred."],
  });
}
