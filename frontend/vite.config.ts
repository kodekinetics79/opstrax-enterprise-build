import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";
import { defineConfig, loadEnv } from "vite";

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), "");
  const frontendSha =
    env.VITE_DEPLOYMENT_SHA ||
    env.VERCEL_GIT_COMMIT_SHA ||
    env.GITHUB_SHA ||
    env.RENDER_GIT_COMMIT ||
    "unknown";
  const frontendEnvironment =
    env.VITE_APP_ENVIRONMENT ||
    env.VERCEL_ENV ||
    mode ||
    "unknown";
  const apiBaseUrl =
    env.VITE_API_BASE_URL ||
    env.VITE_DOTNET_API_URL ||
    env.VITE_PLATFORM_API_BASE_URL ||
    "";

  return {
    define: {
      __OPSTRAX_FRONTEND_SHA__: JSON.stringify(frontendSha),
      __OPSTRAX_FRONTEND_ENVIRONMENT__: JSON.stringify(frontendEnvironment),
      __OPSTRAX_API_BASE_URL__: JSON.stringify(apiBaseUrl),
    },
    plugins: [react(), tailwindcss()],
    resolve: {
      alias: {
        "@": "/src",
      },
    },
    build: {
      rollupOptions: {
        output: {
          // force deterministic chunk splitting so cache busts correctly
          chunkFileNames: "assets/[name]-[hash].js",
          // Keep the long-lived application runtime out of the entry chunk. These
          // packages change less often than OpsTrax screens, so this also prevents
          // an ordinary application release from invalidating the framework cache.
          manualChunks(id) {
            if (id.includes("/node_modules/react/") || id.includes("/node_modules/react-dom/") || id.includes("/node_modules/scheduler/")) {
              return "vendor-react";
            }
            if (id.includes("/node_modules/react-router/")) {
              return "vendor-router";
            }
            if (id.includes("/node_modules/@tanstack/")) {
              return "vendor-query";
            }
            if (id.includes("/node_modules/axios/")) {
              return "vendor-http";
            }
          }
        },
      },
    },
  };
});
