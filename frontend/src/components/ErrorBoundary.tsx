import { Component, type ErrorInfo, type ReactNode } from "react";
import { AlertTriangle, RefreshCw } from "lucide-react";
import {
  moduleLoadRecoveryKey,
  shouldReloadForDynamicImportFailure,
} from "@/utils/moduleLoadRecovery";

type Props = {
  children: ReactNode;
};

type State = {
  hasError: boolean;
};

export class ErrorBoundary extends Component<Props, State> {
  state: State = { hasError: false };

  static getDerivedStateFromError(): State {
    return { hasError: true };
  }

  override componentDidCatch(error: Error, info: ErrorInfo) {
    console.error("Frontend error boundary caught an error", error, info);

    // A tab left open across an atomic deployment can still execute the old
    // entry bundle and request a hashed lazy chunk that no longer exists on the
    // stable alias. Reload exactly once per deployment window so the browser
    // obtains the current entry manifest, while preserving this boundary if the
    // reload cannot recover.
    try {
      const recoveryKey = moduleLoadRecoveryKey(__OPSTRAX_FRONTEND_SHA__);
      const storedAttempt = window.sessionStorage.getItem(recoveryKey);

      if (shouldReloadForDynamicImportFailure(error, storedAttempt === "attempted")) {
        window.sessionStorage.setItem(recoveryKey, "attempted");
        window.location.reload();
      }
    } catch {
      // Storage can be unavailable in hardened browser contexts. Do not reload
      // without the loop guard; leave the explicit recovery action visible.
    }
  }

  override render() {
    if (this.state.hasError) {
      return (
        <div className="grid min-h-screen place-items-center bg-slate-950 p-6 text-slate-100">
          <div className="panel max-w-lg p-8 text-center">
            <div className="mx-auto flex h-14 w-14 items-center justify-center rounded-2xl border border-red-400/20 bg-red-400/10 text-red-300">
              <AlertTriangle className="h-7 w-7" />
            </div>
            <h1 className="mt-5 text-2xl font-bold tracking-tight text-white">Something went wrong</h1>
            <p className="mt-3 text-sm leading-6 text-slate-400">
              The app hit an unexpected error. Refresh the page or return to the dashboard to continue.
            </p>
            <button
              className="btn-primary mt-6"
              type="button"
              onClick={() => window.location.reload()}
            >
              <RefreshCw className="h-4 w-4" />
              Refresh app
            </button>
          </div>
        </div>
      );
    }

    return this.props.children;
  }
}
