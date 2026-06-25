import dotenv from "dotenv";
import path from "path";

dotenv.config({ path: path.resolve(process.cwd(), "..", ".env") });
dotenv.config();

async function start() {
  const { ensureBackendColumns, pingDatabase } = await import("./lib/db");
  await ensureBackendColumns().catch((error) => {
    console.error("[fleet-backend] schema bootstrap failed", error);
  });

  const { app } = await import("./app");
  const PORT = Number(process.env.PORT || 11000);

  try {
    const db = await pingDatabase();
    console.log(`[fleet-backend] database ${db.ok ? "connected" : "unavailable"} (${db.latencyMs}ms)`);
  } catch (error) {
    console.error("[fleet-backend] database ping failed", error);
  }

  app.listen(PORT, () => {
    console.log(`Fleet backend running on http://localhost:${PORT}`);
  });
}

void start();
