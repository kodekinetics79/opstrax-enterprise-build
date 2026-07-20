import { createContext, useCallback, useContext, useEffect, useMemo, useState } from "react";
import * as SecureStore from "expo-secure-store";
import { SECURE_WORKSPACE_JOB_KEY } from "@/config";

type WorkflowContextValue = {
  selectedJobId: number | null;
  selectedJobInput: string;
  setSelectedJobInput: (value: string) => void;
  applySelectedJob: () => void;
  setSelectedJobId: (value: number | null) => void;
  refreshKey: number;
  bumpRefreshKey: () => void;
};

const WorkflowContext = createContext<WorkflowContextValue | null>(null);

export function WorkflowProvider({ children }: { children: React.ReactNode }) {
  const [selectedJobId, setSelectedJobIdState] = useState<number | null>(null);
  const [selectedJobInput, setSelectedJobInput] = useState("");
  const [refreshKey, setRefreshKey] = useState(0);

  useEffect(() => {
    void (async () => {
      const stored = await SecureStore.getItemAsync(SECURE_WORKSPACE_JOB_KEY);
      if (stored) {
        const parsed = Number.parseInt(stored, 10);
        if (Number.isFinite(parsed) && parsed > 0) {
          setSelectedJobIdState(parsed);
          setSelectedJobInput(String(parsed));
        }
      }
    })();
  }, []);

  const setSelectedJobId = useCallback((value: number | null) => {
    setSelectedJobIdState(value);
    setSelectedJobInput(value ? String(value) : "");
    void SecureStore.setItemAsync(SECURE_WORKSPACE_JOB_KEY, value ? String(value) : "");
  }, []);

  const applySelectedJob = useCallback(() => {
    const parsed = Number.parseInt(selectedJobInput, 10);
    setSelectedJobId(Number.isFinite(parsed) && parsed > 0 ? parsed : null);
  }, [selectedJobInput, setSelectedJobId]);

  const bumpRefreshKey = useCallback(() => setRefreshKey((value) => value + 1), []);

  const value = useMemo(
    () => ({
      selectedJobId,
      selectedJobInput,
      setSelectedJobInput,
      applySelectedJob,
      setSelectedJobId,
      refreshKey,
      bumpRefreshKey,
    }),
    [selectedJobId, selectedJobInput, refreshKey, applySelectedJob, setSelectedJobId, bumpRefreshKey],
  );

  return <WorkflowContext.Provider value={value}>{children}</WorkflowContext.Provider>;
}

export function useWorkflow() {
  const context = useContext(WorkflowContext);
  if (!context) throw new Error("useWorkflow must be used inside WorkflowProvider");
  return context;
}
