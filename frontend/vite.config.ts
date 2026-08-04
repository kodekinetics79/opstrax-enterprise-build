import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";
import { defineConfig } from "vite";

export default defineConfig({
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
        },
      },
    },
  },
});
