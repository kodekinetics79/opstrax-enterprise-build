import { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Bot, Plus, RadioTower, ShieldCheck, Target } from "lucide-react";
import { modules, moduleIcons } from "@/modules/moduleConfig";
import { AiInsightCard, DataTable, DetailDrawer, FilterBar, KpiCard, LoadingState, PageHeader } from "@/components/ui";
import { modulesApi } from "@/services/modulesApi";
import type { AnyRecord } from "@/types";

export function ModulePage({ moduleKey }: { moduleKey: string }) {
  const [selected, setSelected] = useState<AnyRecord | null>(null);
  const module = modules.find((item) => item.key === moduleKey)!;
  const Icon = moduleIcons[moduleKey] || RadioTower;
  const query = useQuery({ queryKey: ["module", moduleKey], queryFn: () => modulesApi.get(moduleKey) });
  const columns = useMemo(() => ["title", "status", "ownerName", "locationName", "riskLevel", "amount", "dueAt"], []);
  if (query.isLoading) return <LoadingState />;
  const records = query.data?.records || [];

  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow={module.group}
        title={module.title}
        description={module.description}
        actions={<><button className="btn-primary"><Plus className="h-4 w-4" /> Create</button><button className="btn-ghost">Review Queue</button></>}
      />
      <div className="grid gap-4 md:grid-cols-4">
        <KpiCard label="Records" value={records.length} icon={<Icon />} status="Active" />
        <KpiCard label="Open / Active" value={String(query.data?.summary?.active ?? records.filter((x) => String(x.status).match(/open|active|progress/i)).length)} icon={<ShieldCheck />} status="Healthy" />
        <KpiCard label="Risk Items" value={String(query.data?.summary?.riskItems ?? records.filter((x) => String(x.riskLevel).match(/high|critical/i)).length)} icon={<Target />} status="Review" />
        <KpiCard label="AI Insights" value={query.data?.insights?.length || 0} icon={<Bot />} status="Recommended" />
      </div>
      <FilterBar />
      <div className="grid gap-6 xl:grid-cols-[1fr_360px]">
        <DataTable rows={records} columns={columns} onSelect={setSelected} />
        <div className="space-y-4">
          {(query.data?.insights || []).slice(0, 3).map((insight) => <AiInsightCard key={String(insight.id)} insight={insight} />)}
          {!query.data?.insights?.length ? <AiInsightCard insight={{ title: `${module.title} intelligence`, body: "OpsTrax AI will surface operational recommendations as seeded data and live events flow through this module." }} /> : null}
        </div>
      </div>
      <DetailDrawer record={selected} onClose={() => setSelected(null)} />
    </div>
  );
}
