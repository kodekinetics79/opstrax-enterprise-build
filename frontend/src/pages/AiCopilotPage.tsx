import { useState } from "react";
import { useMutation, useQuery } from "@tanstack/react-query";
import { Bot, Send, Sparkles } from "lucide-react";
import { AiInsightCard, LoadingState, PageHeader, StatusBadge } from "@/components/ui";
import { aiApi } from "@/services/aiApi";
import type { AnyRecord } from "@/types";

const categories = ["Cost Leakage", "Dispatch Risk", "Maintenance Planning", "Driver Coaching", "Safety Review", "Compliance Audit", "Customer SLA", "Executive Summary"];

export function AiCopilotPage() {
  const [category, setCategory] = useState(categories[0]);
  const [prompt, setPrompt] = useState("Where should operations focus in the next 4 hours?");
  const insights = useQuery({ queryKey: ["ai-insights"], queryFn: aiApi.insights });
  const ask = useMutation({ mutationFn: () => aiApi.ask(prompt, category) });
  const response = ask.data as AnyRecord | undefined;

  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow="OpsTrax AI Copilot"
        title="Operational intelligence workspace"
        description="Ask about dispatch risk, cost leakage, maintenance planning, driver coaching, safety review, compliance audit, customer SLA and executive summaries. No external AI key required."
        actions={<button className="btn-primary" onClick={() => ask.mutate()}><Send className="h-4 w-4" /> Ask OpsTrax AI</button>}
      />
      <div className="grid gap-6 xl:grid-cols-[420px_1fr]">
        <aside className="space-y-4">
          <div className="panel p-5">
            <h2 className="section-title">Prompt Categories</h2>
            <div className="mt-4 grid gap-2">
              {categories.map((item) => <button key={item} onClick={() => setCategory(item)} className={`rounded-xl px-4 py-3 text-left text-sm transition ${category === item ? "bg-violet-400/20 text-white ring-1 ring-violet-300/30" : "bg-white/[0.04] text-slate-300 hover:bg-white/[0.07]"}`}>{item}</button>)}
            </div>
          </div>
          <div className="panel p-5">
            <h2 className="section-title">Evidence Cards</h2>
            <div className="mt-4 space-y-3">
              {insights.isLoading ? <LoadingState /> : (insights.data || []).slice(0, 5).map((item) => <AiInsightCard key={String(item.id)} insight={item} />)}
            </div>
          </div>
        </aside>
        <section className="panel p-5">
          <div className="rounded-2xl border border-violet-400/20 bg-violet-500/10 p-5">
            <div className="flex items-center gap-2 text-violet-200"><Bot className="h-5 w-5" /><span className="font-bold">OpsTrax AI</span><StatusBadge status="Local Stub" /></div>
            <textarea className="field mt-5 min-h-36 resize-none" value={prompt} onChange={(event) => setPrompt(event.target.value)} />
            <div className="mt-4 flex flex-wrap gap-2">{["Create dispatch review", "Send ETA updates", "Schedule maintenance", "Generate executive brief"].map((chip) => <span key={chip} className="badge"><Sparkles className="h-3.5 w-3.5" /> {chip}</span>)}</div>
          </div>
          {response ? (
            <div className="mt-6 space-y-5">
              <div><h2 className="section-title">Summary</h2><p className="mt-3 leading-7 text-slate-300">{String(response.summary)}</p></div>
              <div className="grid gap-4 md:grid-cols-2">{((response.evidence as AnyRecord[]) || []).map((item, i) => <AiInsightCard key={i} insight={item} />)}</div>
              <div><h2 className="section-title">Recommended Actions</h2><div className="mt-3 grid gap-3">{((response.suggestedNextSteps as string[]) || []).map((step) => <div key={step} className="rounded-xl border border-white/10 bg-white/[0.04] p-3 text-sm text-slate-200">{step}</div>)}</div></div>
            </div>
          ) : (
            <div className="mt-10 text-center text-slate-500">Ask OpsTrax AI to generate an evidence-backed operational plan.</div>
          )}
        </section>
      </div>
    </div>
  );
}
