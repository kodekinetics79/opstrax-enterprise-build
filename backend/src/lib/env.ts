import { z } from "zod";

const envSchema = z.object({
  PORT: z.coerce.number().int().positive().default(11000),
  FRONTEND_URL: z.string().default("http://localhost:10000"),
  RATE_LIMIT_WINDOW_MS: z.coerce.number().int().positive().default(60_000),
  RATE_LIMIT_MAX_REQUESTS: z.coerce.number().int().positive().default(240),
  PG_CONNECTION: z.string().optional(),
  DATABASE_URL: z.string().optional(),
});

export type BackendEnv = z.infer<typeof envSchema>;

export function getEnv(): BackendEnv {
  return envSchema.parse(process.env);
}

