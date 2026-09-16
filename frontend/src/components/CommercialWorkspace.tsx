import type { ReactNode } from "react";
import { Link, useLocation, useNavigate } from "react-router";
import {
  Banknote,
  BriefcaseBusiness,
  Building2,
  ChevronRight,
  CircleDollarSign,
  FileCheck2,
  FileText,
  Info,
  ReceiptText,
  Truck,
  UsersRound,
  type LucideIcon,
} from "lucide-react";
import { PageHeader } from "@/components/ui";
import { useHasPermission } from "@/hooks/usePermission";
import "./commercial-workspace.css";

export type RevenueStage = "accounts" | "leads" | "opportunities" | "quotations" | "contracts" | "delivery" | "invoices" | "payments";

type RevenueStep = {
  key: RevenueStage;
  label: string;
  route: string;
  permission: string;
  icon: LucideIcon;
};

const REVENUE_STEPS: RevenueStep[] = [
  { key: "accounts", label: "Accounts", route: "/customers", permission: "customers:view", icon: Building2 },
  { key: "leads", label: "Leads", route: "/leads", permission: "customers:view", icon: UsersRound },
  { key: "opportunities", label: "Deals", route: "/opportunities", permission: "customers:view", icon: BriefcaseBusiness },
  { key: "quotations", label: "Quotes", route: "/quotations", permission: "customers:view", icon: FileText },
  { key: "contracts", label: "Contracts", route: "/contracts", permission: "customers:view", icon: FileCheck2 },
  { key: "delivery", label: "Delivery", route: "/jobs", permission: "shipments:view", icon: Truck },
  { key: "invoices", label: "Invoices", route: "/invoices", permission: "finance:view", icon: ReceiptText },
  { key: "payments", label: "Payments", route: "/payments", permission: "finance:view", icon: Banknote },
];

const FINANCE_SECTIONS = [
  { key: "/invoices", label: "Invoices", permission: "finance:view" },
  { key: "/ar-aging", label: "AR aging", permission: "finance:view" },
  { key: "/payments", label: "Payments", permission: "finance:view" },
  { key: "/profitability", label: "Profitability", permission: "finance:view" },
  { key: "/finance/billing", label: "Consolidation", permission: "billing:read" },
  { key: "/finance/tax-config", label: "Tax", permission: "tax:read" },
  { key: "/finance/revenue-recognition", label: "Recognition", permission: "revrec:read" },
  { key: "/finance/settlements", label: "Driver pay", permission: "settlement:read" },
  { key: "/expenses", label: "Expenses", permission: "finance:view" },
  { key: "/fuel-idling", label: "Fuel", permission: "fuel:view" },
] as const;

export function RevenueWorkspaceHeader({
  title,
  description,
  activeStage,
  actions,
  eyebrow = "Revenue workspace",
}: {
  title: string;
  description: string;
  activeStage?: RevenueStage;
  actions?: ReactNode;
  eyebrow?: string;
}) {
  const hasPermission = useHasPermission();
  const visibleSteps = REVENUE_STEPS.filter((step) => hasPermission(step.permission));

  return (
    <PageHeader
      title={title}
      eyebrow={eyebrow}
      description={description}
      actions={actions}
      compact
      footer={
        <nav className="revenue-spine" aria-label="Customer revenue lifecycle">
          <span className="revenue-spine__label"><CircleDollarSign aria-hidden /> Revenue flow</span>
          <div className="revenue-spine__rail">
            {visibleSteps.map((step, index) => {
              const Icon = step.icon;
              const active = step.key === activeStage;
              return (
                <span className="revenue-spine__item" key={step.key}>
                  <Link className="revenue-step" data-active={active || undefined} aria-current={active ? "page" : undefined} to={step.route}>
                    <Icon aria-hidden />
                    <span>{step.label}</span>
                  </Link>
                  {index < visibleSteps.length - 1 ? <ChevronRight className="revenue-spine__arrow" aria-hidden /> : null}
                </span>
              );
            })}
          </div>
        </nav>
      }
    />
  );
}

export type CommercialMetric = {
  label: string;
  value: ReactNode;
  detail?: ReactNode;
  tone?: "neutral" | "good" | "warn" | "bad" | "info";
  active?: boolean;
  onClick?: () => void;
};

export function CommercialMetricRail({ metrics, label = "Workspace summary" }: { metrics: CommercialMetric[]; label?: string }) {
  if (metrics.length === 0) return null;

  return (
    <section className="commercial-metrics" aria-label={label}>
      {metrics.map((metric) => {
        const content = <>
          <span className="commercial-metric__label">{metric.label}</span>
          <strong className="commercial-metric__value">{metric.value}</strong>
          {metric.detail != null ? <span className="commercial-metric__detail">{metric.detail}</span> : null}
        </>;
        return metric.onClick ? (
          <button key={metric.label} type="button" className="commercial-metric" data-tone={metric.tone ?? "neutral"} data-active={metric.active || undefined} onClick={metric.onClick}>
            {content}
          </button>
        ) : (
          <div key={metric.label} className="commercial-metric" data-tone={metric.tone ?? "neutral"}>
            {content}
          </div>
        );
      })}
    </section>
  );
}

export function CommercialToolbar({ filters, search, meta }: { filters: ReactNode; search?: ReactNode; meta?: ReactNode }) {
  return (
    <div className="commercial-toolbar">
      <div className="commercial-toolbar__filters">{filters}</div>
      {meta ? <div className="commercial-toolbar__meta">{meta}</div> : null}
      {search ? <div className="commercial-toolbar__search">{search}</div> : null}
    </div>
  );
}

export function CommercialDisclosure({ title = "Data and workflow notes", children }: { title?: string; children: ReactNode }) {
  return (
    <details className="commercial-disclosure">
      <summary><Info aria-hidden /> {title}</summary>
      <div className="commercial-disclosure__body">{children}</div>
    </details>
  );
}

export function CommercialTabs({
  label,
  items,
  active,
  onSelect,
}: {
  label: string;
  items: Array<{ key: string; label: string; detail?: string }>;
  active: string;
  onSelect: (key: string) => void;
}) {
  return (
    <nav className="commercial-tabs" aria-label={label}>
      {items.map((item) => (
        <button key={item.key} type="button" className="commercial-tab" data-active={active === item.key || undefined} aria-current={active === item.key ? "page" : undefined} onClick={() => onSelect(item.key)}>
          <span>{item.label}</span>
          {item.detail ? <small>{item.detail}</small> : null}
        </button>
      ))}
    </nav>
  );
}

export function FinanceWorkspaceTabs() {
  const { pathname } = useLocation();
  const navigate = useNavigate();
  const hasPermission = useHasPermission();
  const items = FINANCE_SECTIONS.filter((item) => hasPermission(item.permission));

  return (
    <CommercialTabs
      label="Finance workspace sections"
      items={items}
      active={pathname}
      onSelect={(route) => navigate(route)}
    />
  );
}
