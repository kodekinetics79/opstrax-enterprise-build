import type { Dispatch, SetStateAction } from 'react';
import { Package, RefreshCw, X, type LucideIcon } from 'lucide-react';
import type { LogisticsOrder, LogisticsOverview, LogisticsRoute } from '@/services/logisticsApi';
import { KpiCard, PageHeader } from '@/components/ui';
import { DispatchOrderRow, LightField } from './DispatchWorkspaceComponents';
import { MODE_ORDER, MODULES, type DispatchMode } from './dispatchWorkspaceModel';

type Notice = { kind: 'success' | 'error' | 'info'; message: string };
type OrderForm = {
  orderNumber: string; customerName: string; city: string; area: string; priority: string; routeCode: string;
  itemCount: string; orderValue: string; driverName: string; vehicleNumber: string; dispatchNotes: string; promisedAtUtc: string;
};
type SummaryStat = { label: string; value: string; hint: string; icon: LucideIcon };
const terminalRoute = (status: string) => status === 'Closed' || status === 'Completed';

export interface DispatchOverviewPanelProps {
  config: (typeof MODULES)[DispatchMode];
  canCreate: boolean;
  canAssign: boolean;
  canUpdate: boolean;
  beginCreateOrder: () => void;
  loading: boolean;
  setLoading: Dispatch<SetStateAction<boolean>>;
  refreshWorkspace: () => Promise<void>;
  setOrderIntakeOpen: Dispatch<SetStateAction<boolean>>;
  setMode: Dispatch<SetStateAction<DispatchMode>>;
  mode: DispatchMode;
  notice: Notice | null;
  setNotice: Dispatch<SetStateAction<Notice | null>>;
  loadError: string | null;
  stats: SummaryStat[];
  alerts: LogisticsOverview['alerts'];
  orders: LogisticsOrder[];
  orderTotal: number;
  savingId: string | null;
  handleDispatch: (order: LogisticsOrder) => Promise<void>;
  beginEditOrder: (order: LogisticsOrder) => void;
  orderIntakeOpen: boolean;
  editingOrderId: string | null;
  orderForm: OrderForm;
  setOrderForm: Dispatch<SetStateAction<OrderForm>>;
  routes: LogisticsRoute[];
  selectOrderRoute: (routeCode: string) => void;
  resetOrderForm: () => void;
  creating: boolean;
  handleCreateOrder: () => Promise<void>;
}

export function DispatchOverviewPanel(props: DispatchOverviewPanelProps) {
  const {
    config, canCreate, canAssign, canUpdate, beginCreateOrder, loading, setLoading, refreshWorkspace,
    setOrderIntakeOpen, setMode, mode, notice, setNotice, loadError, stats, alerts, orders, orderTotal,
    savingId, handleDispatch, beginEditOrder, orderIntakeOpen, editingOrderId, orderForm, setOrderForm, routes,
    selectOrderRoute, resetOrderForm, creating, handleCreateOrder,
  } = props;
    return (
      <div className="fleet-console page-stack min-w-0 text-slate-900">
        <PageHeader
          eyebrow="Dispatch & Delivery"
          title={config.title}
          description={config.subtitle}
          actions={
            <>
              {canCreate && (
                <button type="button" className="btn-primary" onClick={beginCreateOrder}>
                  <Package className="h-4 w-4" /> Create order
                </button>
              )}
              <button
                type="button"
                className="btn-ghost"
                disabled={loading}
                onClick={() => {
                  setLoading(true);
                  refreshWorkspace().finally(() => setLoading(false));
                }}
              >
                <RefreshCw className={`h-4 w-4 ${loading ? 'animate-spin' : ''}`} /> Refresh
              </button>
            </>
          }
          footer={
            <nav aria-label="Logistics workspace views" className="flex min-w-0 max-w-full gap-1 overflow-x-auto">
              {MODE_ORDER.map((item) => {
                const active = item === mode;
                return (
                  <button
                    key={item}
                    type="button"
                    aria-pressed={active}
                    onClick={() => {
                      setOrderIntakeOpen(false);
                      setMode(item);
                    }}
                    className={`${active ? 'btn-primary' : 'btn-ghost'} btn-compact shrink-0`}
                  >
                    {MODULES[item].label}
                  </button>
                );
              })}
            </nav>
          }
        />

        {notice && (
          <div
            role={notice.kind === 'error' ? 'alert' : 'status'}
            aria-live={notice.kind === 'error' ? 'assertive' : 'polite'}
            className={`flex items-center justify-between gap-3 rounded-xl border px-3 py-2 text-sm ${notice.kind === 'error' ? 'border-red-200 bg-red-50 text-red-800' : notice.kind === 'success' ? 'border-emerald-200 bg-emerald-50 text-emerald-800' : 'border-blue-200 bg-blue-50 text-blue-800'}`}
          >
            <span>{notice.message}</span>
            <button type="button" aria-label="Dismiss notification" onClick={() => setNotice(null)} className="icon-btn"><X className="h-4 w-4" /></button>
          </div>
        )}

        {loadError && (
          <div role="alert" className="flex flex-wrap items-center justify-between gap-3 rounded-xl border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-800">
            <span>{loadError}</span>
            <button type="button" onClick={() => { setLoading(true); refreshWorkspace().finally(() => setLoading(false)); }} className="btn-danger btn-compact">
              <RefreshCw className="h-4 w-4" /> Retry
            </button>
          </div>
        )}

        <section className="panel grid grid-cols-2 overflow-hidden divide-x divide-y divide-slate-100 lg:grid-cols-4 lg:divide-y-0" aria-label="Dispatch summary">
          {loading
            ? Array.from({ length: 4 }).map((_, index) => <div key={index} className="h-[72px] animate-pulse bg-slate-50" />)
            : stats.map((stat) => (
                <KpiCard key={stat.label} compact label={stat.label} value={stat.value} status={stat.hint} icon={<stat.icon className="h-4 w-4" />} />
              ))}
        </section>

        {alerts.length > 0 && (
          <section className="panel flex min-w-0 flex-col gap-2 px-3 py-2 lg:flex-row lg:items-center" aria-labelledby="dispatch-alerts-title">
            <div className="shrink-0">
              <p id="dispatch-alerts-title" className="text-xs font-bold text-amber-800">Pending alerts</p>
              <p className="text-[11px] text-slate-500">{alerts.length} need review</p>
            </div>
            <div className="flex min-w-0 flex-1 gap-2 overflow-x-auto">
              {alerts.slice(0, 4).map((alert) => (
                <div key={`${alert.orderNumber}-${alert.status}`} className="min-w-[190px] flex-1 rounded-lg border border-amber-200 bg-amber-50 px-3 py-2">
                  <div className="flex items-center justify-between gap-2">
                    <span className="truncate text-xs font-bold text-amber-900">{alert.orderNumber}</span>
                    <span className="shrink-0 text-[10px] font-bold uppercase text-amber-700">{alert.status}</span>
                  </div>
                  <p className="truncate text-[11px] text-amber-800/80">{alert.customerName} · {alert.exceptionReason || 'Recovery follow-up required'}</p>
                </div>
              ))}
            </div>
          </section>
        )}

        <section className="panel min-w-0 overflow-hidden" aria-labelledby="dispatch-orders-title">
          <div className="flex flex-wrap items-center justify-between gap-2 border-b border-slate-100 px-3 py-2">
            <div>
              <h2 id="dispatch-orders-title" className="text-sm font-bold text-slate-950">Orders requiring action</h2>
              <p className="text-xs text-slate-500">Assignment, route ownership, and dispatch state in one queue.</p>
            </div>
            <div className="flex items-center gap-2 text-xs text-slate-500">
              <span>{orders.length} shown · {orderTotal} total</span>
              <button type="button" className="btn-ghost btn-compact" onClick={() => setMode('orders')}>Open full pipeline</button>
            </div>
          </div>

          {loading ? (
            <div className="divide-y divide-slate-100">
              {Array.from({ length: 5 }).map((_, index) => <div key={index} className="h-16 animate-pulse bg-slate-50/70" />)}
            </div>
          ) : orders.length ? (
            <div className="divide-y divide-slate-100">
              {orders.map((order) => (
                <DispatchOrderRow
                  key={order.id}
                  order={order}
                  canAssign={canAssign}
                  canUpdate={canUpdate}
                  saving={savingId === order.id}
                  onDispatch={() => handleDispatch(order)}
                  onEdit={() => beginEditOrder(order)}
                />
              ))}
            </div>
          ) : (
            <p className="px-4 py-8 text-center text-sm text-slate-500">No orders are waiting in the current dispatch queue.</p>
          )}
        </section>

        {orderIntakeOpen && (canCreate || (Boolean(editingOrderId) && canUpdate)) && (
          <section className="panel p-4" aria-labelledby="dispatch-order-form-title">
            <div className="mb-3 flex items-start justify-between gap-3">
              <div>
                <h2 id="dispatch-order-form-title" className="text-sm font-bold text-slate-950">{editingOrderId ? 'Edit dispatch order' : 'Create dispatch order'}</h2>
                <p className="text-xs text-slate-500">Required order details are validated before the record enters the queue.</p>
              </div>
              <button type="button" className="btn-ghost btn-compact" onClick={resetOrderForm}>Close</button>
            </div>
            <form className="grid gap-3 md:grid-cols-2 xl:grid-cols-4" onSubmit={(event) => { event.preventDefault(); void handleCreateOrder(); }}>
              <LightField label="Order number" value={orderForm.orderNumber} onChange={(value) => setOrderForm((current) => ({ ...current, orderNumber: value }))} required disabled={Boolean(editingOrderId)} />
              <LightField label="Customer name" value={orderForm.customerName} onChange={(value) => setOrderForm((current) => ({ ...current, customerName: value }))} required />
              <LightField label="City" value={orderForm.city} onChange={(value) => setOrderForm((current) => ({ ...current, city: value }))} />
              <LightField label="Area" value={orderForm.area} onChange={(value) => setOrderForm((current) => ({ ...current, area: value }))} />
              <LightField label="Item count" type="number" min="1" max="100000" value={orderForm.itemCount} onChange={(value) => setOrderForm((current) => ({ ...current, itemCount: value }))} required />
              <LightField label="Order value" type="number" min="0" step="0.01" value={orderForm.orderValue} onChange={(value) => setOrderForm((current) => ({ ...current, orderValue: value }))} />
              <label className="form-field flex flex-col text-xs font-semibold text-slate-700">Priority
                <select value={orderForm.priority} onChange={(event) => setOrderForm((current) => ({ ...current, priority: event.target.value }))} className="field">
                  {['Low', 'Normal', 'High', 'Critical'].map((priority) => <option key={priority}>{priority}</option>)}
                </select>
              </label>
              <label className="form-field flex flex-col text-xs font-semibold text-slate-700">Assigned route
                <select value={orderForm.routeCode} onChange={(event) => selectOrderRoute(event.target.value)} className="field">
                  <option value="">Unassigned</option>
                  {routes.filter((route) => !terminalRoute(route.status)).map((route) => <option key={route.id} value={route.routeCode}>{route.routeCode} · {route.status}</option>)}
                </select>
              </label>
              <LightField label="Driver name" value={orderForm.driverName} onChange={(value) => setOrderForm((current) => ({ ...current, driverName: value }))} />
              <LightField label="Vehicle number" value={orderForm.vehicleNumber} onChange={(value) => setOrderForm((current) => ({ ...current, vehicleNumber: value }))} />
              <LightField label="Promised time" type="datetime-local" value={orderForm.promisedAtUtc} onChange={(value) => setOrderForm((current) => ({ ...current, promisedAtUtc: value }))} />
              <label className="form-field flex flex-col text-xs font-semibold text-slate-700 xl:col-span-1">Dispatch notes
                <textarea value={orderForm.dispatchNotes} onChange={(event) => setOrderForm((current) => ({ ...current, dispatchNotes: event.target.value }))} rows={2} className="field min-h-[64px] resize-y" />
              </label>
              <div className="flex flex-wrap justify-end gap-2 md:col-span-2 xl:col-span-4">
                <button type="button" onClick={resetOrderForm} className="btn-ghost">Cancel</button>
                <button type="submit" disabled={creating} className="btn-primary">
                  {creating ? 'Saving…' : editingOrderId ? 'Save order' : 'Create order'}
                </button>
              </div>
            </form>
          </section>
        )}
      </div>
    );
}
