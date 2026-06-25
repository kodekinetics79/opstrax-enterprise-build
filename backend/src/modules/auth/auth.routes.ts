import express from "express";
import { z } from "zod";
import {
  authenticateWithPassword,
  changePassword,
  getSessionByToken,
  logoutSession,
  refreshSession,
} from "./auth.service";
import { fail, ok } from "../../lib/httpEnvelope";

const router = express.Router();

const loginSchema = z.object({
  email: z.string().email(),
  password: z.string().min(1),
});

const changePasswordSchema = z.object({
  currentPassword: z.string().min(1),
  newPassword: z.string().min(8),
});

function readBearerToken(req: express.Request) {
  const header = req.get("authorization") || "";
  if (!header.toLowerCase().startsWith("bearer ")) return null;
  const token = header.slice(7).trim();
  return token || null;
}

async function requireSession(req: express.Request, res: express.Response) {
  const token = readBearerToken(req);
  if (!token) {
    res.status(401).json(fail("Unauthorized", ["Missing bearer token"]));
    return null;
  }

  const session = await getSessionByToken(token);
  if (!session) {
    res.status(401).json(fail("Unauthorized", ["Session expired or invalid"]));
    return null;
  }

  return { token, ...session };
}

router.post("/login", async (req, res, next) => {
  try {
    const parsed = loginSchema.safeParse(req.body);
    if (!parsed.success) {
      return res.status(400).json(fail("Invalid login request", parsed.error.issues.map((issue) => issue.message)));
    }

    const session = await authenticateWithPassword(parsed.data.email, parsed.data.password);
    if (!session) {
      return res.status(401).json(fail("Invalid credentials", ["Email or password was incorrect"]));
    }

    return res.status(200).json(ok(session, "Login successful"));
  } catch (error) {
    next(error);
  }
});

router.get("/me", async (req, res, next) => {
  try {
    const session = await requireSession(req, res);
    if (!session) return;

    return res.status(200).json(
      ok(
        {
          token: session.token,
          csrfToken: session.session.csrfToken ?? "",
          user: {
            id: session.user.id,
            email: session.user.email,
            name: session.user.fullName,
            fullName: session.user.fullName,
            companyId: session.user.companyId,
            companyCode: session.user.companyCode,
          },
          role: session.user.roleName,
          company: {
            id: session.user.companyId,
            companyId: session.user.companyId,
            code: session.user.companyCode,
            name: session.user.companyName,
          },
          permissions: session.user.permissions,
        },
        "Session loaded"
      )
    );
  } catch (error) {
    next(error);
  }
});

router.post("/refresh", async (req, res, next) => {
  try {
    const session = await requireSession(req, res);
    if (!session) return;

    const refreshed = await refreshSession(session.token);
    if (!refreshed) {
      return res.status(401).json(fail("Unauthorized", ["Session expired or invalid"]));
    }

    return res.status(200).json(ok(refreshed, "Session refreshed"));
  } catch (error) {
    next(error);
  }
});

router.post("/logout", async (req, res, next) => {
  try {
    const session = await requireSession(req, res);
    if (!session) return;

    await logoutSession(session.token);
    return res.status(200).json(ok({ loggedOut: true }, "Logout successful"));
  } catch (error) {
    next(error);
  }
});

router.post("/change-password", async (req, res, next) => {
  try {
    const session = await requireSession(req, res);
    if (!session) return;

    const parsed = changePasswordSchema.safeParse(req.body);
    if (!parsed.success) {
      return res.status(400).json(fail("Invalid password change request", parsed.error.issues.map((issue) => issue.message)));
    }

    const changed = await changePassword(session.token, parsed.data.currentPassword, parsed.data.newPassword);
    if (!changed) {
      return res.status(400).json(fail("Password update failed", ["Current password was incorrect"]));
    }

    return res.status(200).json(ok({ changed: true }, "Password updated"));
  } catch (error) {
    next(error);
  }
});

export default router;
