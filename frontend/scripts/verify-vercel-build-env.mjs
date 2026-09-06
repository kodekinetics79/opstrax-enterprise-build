// Keep the portable `npm run vercel-build` entrypoint while applying the stronger
// production-origin validation added by the API host guard.
await import("./check-api-base-url.mjs");
