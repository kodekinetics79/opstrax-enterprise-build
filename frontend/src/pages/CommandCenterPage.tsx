import { useQuery } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { Activity, AlertTriangle, CheckCircle2, DollarSign, RadioTower, Truck } from "lucide-react";
import { Area, AreaChart, Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";
import { ActionQueue, AiInsightCard, KpiCard, LoadingState, PageHeader, Timeline } from "@/components/ui";
import { commandCenterApi } from "@/services/commandCenterApi";
import type { AnyRecord } from "@/types";

export function CommandCenterPage() {
  const { data, isLoading } = useQuery({ queryKey: ["command-center"], queryFn: commandCenterApi.summary, refetchInterval: 15000 });
  if (isLoading || !data) return <LoadingState />;
  const kpis = (data.kpis as AnyRecord[]) || [];
  const chartData = ((data.charts as AnyRecord)?.weeklyJobs as number[] || []).map((value, index) => ({ day: `D${index + 1}`, value }));
  const costData = ((data.charts as AnyRecord)?.costLeakage as number[] || []).map((value, index) => ({ day: `W${index + 1}`, value }));

  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow="OpsTrax Command Center"
        title="Enterprise operations cockpit"
        description="Executive operating picture for fleet readiness, dispatch risk, safety, maintenance, compliance and cost leakage across Northern Virginia/DC operations."
        actions={<><button className="btn-primary">Acknowledge Risks</button><button className="btn-ghost">Generate Brief</button></>}
      />

      <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
        {(kpis.length ? kpis : []).slice(0, 8).map((kpi, i) => (
          <KpiCard key={String(kpi.id || i)} label={String(kpi.label)} value={String(kpi.valueText || kpi.value || "--")} trend={String(kpi.trendValue || "Live")} status={String(kpi.status || "Healthy")} icon={[<Truck />, <RadioTower />, <AlertTriangle />, <CheckCircle2 />][i % 4]} />
        ))}
      </div>

      <div className="grid gap-6 xl:grid-cols-[1.2fr_.8fr]">
        <div className="panel p-5">
          <h2 className="section-title">Live Operations Map Preview</h2>
          <div className="map-surface mt-4 h-[360px]">
            {((data.mapPreview as AnyRecord[]) || []).slice(0, 12).map((pin, index) => (
              <div
                key={String(pin.id)}
                className="map-pin"
                style={{ left: `${12 + (index * 7) % 76}%`, top: `${18 + (index * 11) % 62}%` }}
                title={String(pin.vehicleCode)}
              >
                <span />
              </div>
            ))}
          </div>
        </div>
        <div className="space-y-6">
          <AiInsightCard insight={{ title: "AI Brief", body: String(data.aiBrief) }} />
          <ActionQueue actions={(data.priorityActions as AnyRecord[]) || []} />
        </div>
      </div>

      <div className="grid gap-6 xl:grid-cols-3">
        <ChartPanel title="Weekly Operations Throughput">
          <ResponsiveContainer width="100%" height={220}>
            <AreaChart data={chartData}><defs><linearGradient id="ops" x1="0" y1="0" x2="0" y2="1"><stop offset="5%" stopColor="#2dd4bf" stopOpacity={0.5}/><stop offset="95%" stopColor="#2dd4bf" stopOpacity={0}/></linearGradient></defs><CartesianGrid stroke="#1e293b" /><XAxis dataKey="day" stroke="#64748b" /><YAxis stroke="#64748b" /><Tooltip contentStyle={{ background: "#020617", border: "1px solid #334155" }} /><Area type="monotone" dataKey="value" stroke="#2dd4bf" fill="url(#ops)" /></AreaChart>
          </ResponsiveContainer>
        </ChartPanel>
        <ChartPanel title="Cost Leakage Radar">
          <ResponsiveContainer width="100%" height={220}>
            <BarChart data={costData}><CartesianGrid stroke="#1e293b" /><XAxis dataKey="day" stroke="#64748b" /><YAxis stroke="#64748b" /><Tooltip contentStyle={{ background: "#020617", border: "1px solid #334155" }} /><Bar dataKey="value" fill="#f59e0b" radius={[6, 6, 0, 0]} /></BarChart>
          </ResponsiveContainer>
        </ChartPanel>
        <Timeline items={(data.timeline as AnyRecord[]) || []} />
      </div>
    </div>
  );
}

function ChartPanel({ title, children }: { title: string; children: ReactNode }) {
  return <div className="panel p-5"><h2 className="section-title">{title}</h2><div className="mt-4">{children}</div></div>;
}
