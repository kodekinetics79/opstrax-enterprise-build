import React from "react";
import ReactDOM from "react-dom/client";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { BrowserRouter } from "react-router";
import App from "./App";
import { ErrorBoundary } from "@/components/ErrorBoundary";
import { AuthProvider } from "@/hooks/useAuth";
import { I18nProvider } from "@/i18n/I18nProvider";
import "@/styles/index.css";

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: 1,
      refetchOnWindowFocus: false,
      staleTime: 30_000,
    },
  },
});

function render() {
  ReactDOM.createRoot(document.getElementById("root")!).render(
    <React.StrictMode>
      <QueryClientProvider client={queryClient}>
        <I18nProvider>
          <BrowserRouter>
            <ErrorBoundary>
              <AuthProvider>
                <App />
              </AuthProvider>
            </ErrorBoundary>
          </BrowserRouter>
        </I18nProvider>
      </QueryClientProvider>
    </React.StrictMode>,
  );
}

// Dev-only offline mode (VITE_MOCK_DATA=true in .env.local): serve captured live-site
// fixtures instead of a real backend. The dynamic import means this code and its fixture
// data (src/mocks/fixtures.ts) are never bundled into a production build.
if (import.meta.env.DEV && import.meta.env.VITE_MOCK_DATA === "true") {
  import("@/mocks/mockAdapter").then(({ installMockAdapter }) => {
    installMockAdapter();
    render();
  });
} else {
  render();
}
