import { useEffect, useMemo, useRef, useState } from 'react';
import { Link } from 'react-router';
import {
  AlertTriangle,
  ArrowRight,
  CheckCircle2,
  ChevronRight,
  Clock3,
  Download,
  MapPinned,
  Package,
  Pencil,
  RefreshCw,
  Route,
  Sparkles,
  Truck,
} from 'lucide-react';
import { logisticsApi, type LogisticsOrder, type LogisticsOverview, type LogisticsRoute, type LogisticsStop } from '@/services/logisticsApi';
import { notifyApiError } from '@/services/fleetTmsApi';
import { usePermissions } from '@/hooks/usePermission';

type DispatchMode = 'dispatch' | 'orders' | 'routes' | 'delivery';

const MODULES: Record<DispatchMode, {
  label: string;
  title: string;
  subtitle: string;
  accent: string;
  summary: string;
}> = {
  dispatch: {
    label: 'Dispatch Command Center',
    title: 'Logistics command',
    subtitle: 'Order intake, route movement, recovery actions and proof state across the operation.',
    accent: 'from-blue-600 via-sky-500 to-cyan-400',
    summary: '',
  },
  orders: {
    label: 'Jobs & Orders',
    title: 'Orders pipeline',
    subtitle: 'Who ordered, current priority, dispatch state and promised times for every open order.',
    accent: 'from-indigo-600 via-blue-500 to-cyan-400',
    summary: '',
  },
  routes: {
    label: 'Route Planning',
    title: 'Delivery routes',
    subtitle: 'Stop density, load, driver and completion state per active route.',
    accent: 'from-sky-600 via-cyan-500 to-teal-400',
    summary: '',
  },
  delivery: {
    label: 'Last Mile Delivery',
    title: 'Last mile stops',
    subtitle: 'Live delivery state, attempts, reschedules and recipient proof per stop.',
    accent: 'from-cyan-600 via-sky-500 to-blue-400',
    summary: '',
  },
};

const MODE_ORDER: DispatchMode[] = ['dispatch', 'orders', 'routes', 'delivery'];
const PAGE_SIZE = 12;

type Notice = { kind: 'success' | 'error' | 'info'; message: string };

const normalizePermission = (value: string) => value.trim().toLowerCase().replaceAll('.', ':');
const terminalOrder = (status: string) => status === 'Delivered' || status === 'Returned';
const terminalRoute = (status: string) => status === 'Closed' || status === 'Completed';
const terminalStop = (status: string) => status === 'Delivered';

function toLocalInput(value?: string) {
  if (!value) return '';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? '' : new Date(date.getTime() - date.getTimezoneOffset() * 60_000).toISOString().slice(0, 16);
}

function toIsoOrUndefined(value: string) {
  if (!value.trim()) return undefined;
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? undefined : date.toISOString();
}

export function DispatchWorkspacePage({ mode: initialMode = 'dispatch' }: { mode?: DispatchMode }) {
  const rawPermissions = usePermissions();
  const permissions = useMemo(() => rawPermissions.map(normalizePermission), [rawPermissions]);
  const directlyHas = (permission: string) => permissions.includes('*') || permissions.includes(normalizePermission(permission));
  const canCreate = directlyHas('dispatch:create') || directlyHas('dispatch:manage');
  const canUpdate = directlyHas('dispatch:update') || directlyHas('dispatch:manage');
  const canAssign = directlyHas('dispatch:assign') || directlyHas('dispatch:manage');
  const canDeliver = canUpdate || directlyHas('fleet:pod:manage') || directlyHas('shipments:update');
  const [mode, setMode] = useState<DispatchMode>(initialMode);
  const config = MODULES[mode];
  const [overview, setOverview] = useState<LogisticsOverview | null>(null);
  const [orders, setOrders] = useState<LogisticsOrder[]>([]);
  const [routes, setRoutes] = useState<LogisticsRoute[]>([]);
  const [stops, setStops] = useState<LogisticsStop[]>([]);
  const [routeStops, setRouteStops] = useState<LogisticsStop[]>([]);
  const [selectedRouteId, setSelectedRouteId] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [notice, setNotice] = useState<Notice | null>(null);
  const [savingId, setSavingId] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);
  const [editingOrderId, setEditingOrderId] = useState<string | null>(null);
  const [editingRouteId, setEditingRouteId] = useState<string | null>(null);
  const [orderStatus, setOrderStatus] = useState('All');
  const [routeStatus, setRouteStatus] = useState('All');
  const [stopStatus, setStopStatus] = useState('All');
  const [stopSearch, setStopSearch] = useState('');
  const [orderPage, setOrderPage] = useState(1);
  const [stopPage, setStopPage] = useState(1);
  const [orderTotal, setOrderTotal] = useState(0);
  const [stopTotal, setStopTotal] = useState(0);
  const requestSequence = useRef(0);
  const [orderForm, setOrderForm] = useState({
    orderNumber: '',
    customerName: '',
    city: '',
    area: '',
    priority: 'Normal',
    routeCode: '',
    itemCount: '',
    orderValue: '',
    driverName: '',
    vehicleNumber: '',
    dispatchNotes: '',
    promisedAtUtc: '',
  });
  const [routeForm, setRouteForm] = useState({
    routeCode: '',
    hub: '',
    territory: '',
    driverName: '',
    vehicleNumber: '',
    plannedStops: '',
    distanceKm: '',
    plannedForDate: '',
    departureTimeUtc: '',
    notes: '',
  });
  const [lastMileForm, setLastMileForm] = useState({
    recipientName: '',
    exceptionReason: '',
    nextStop: '',
    timeWindow: '',
    nextEtaUtc: '',
  });

  const syncSelectedRoute = (routeList: LogisticsRoute[], stopList: LogisticsStop[]) => {
    if (!routeList.length) {
      setSelectedRouteId(null);
      return;
    }

    setSelectedRouteId((current) => {
      if (current && routeList.some((route) => route.id === current)) return current;
      const routeCodeFromStops = stopList.find((stop) => stop.routeCode)?.routeCode;
      return routeList.find((route) => route.routeCode === routeCodeFromStops)?.id ?? routeList[0].id;
    });
  };

  const loadWorkspace = async (activeMode: DispatchMode = mode) => {
    const sequence = ++requestSequence.current;
    const orderParams = {
      page: activeMode === 'orders' ? orderPage : 1,
      pageSize: PAGE_SIZE,
      status: activeMode === 'orders' && orderStatus !== 'All' ? orderStatus : undefined,
    };
    const stopParams = {
      page: activeMode === 'delivery' ? stopPage : 1,
      pageSize: PAGE_SIZE,
      status: activeMode === 'delivery' && stopStatus !== 'All' ? stopStatus : undefined,
      search: activeMode === 'delivery' ? stopSearch.trim() || undefined : undefined,
    };
    const [ovRes, orderRes, routeRes, stopRes] = await Promise.allSettled([
      logisticsApi.overview(),
      activeMode === 'dispatch' || activeMode === 'orders' ? logisticsApi.orders(orderParams) : Promise.resolve(null),
      activeMode === 'dispatch' || activeMode === 'orders' || activeMode === 'routes' || activeMode === 'delivery'
        ? logisticsApi.routes({ status: activeMode === 'routes' && routeStatus !== 'All' ? routeStatus : undefined })
        : Promise.resolve(null),
      activeMode === 'dispatch' || activeMode === 'delivery' ? logisticsApi.lastMile(stopParams) : Promise.resolve(null),
    ]);
    if (sequence !== requestSequence.current) return;

    const relevantResults = [
      ovRes,
      ...(activeMode === 'dispatch' || activeMode === 'orders' ? [orderRes] : []),
      ...(activeMode === 'dispatch' || activeMode === 'orders' || activeMode === 'routes' || activeMode === 'delivery' ? [routeRes] : []),
      ...(activeMode === 'dispatch' || activeMode === 'delivery' ? [stopRes] : []),
    ];
    const failures = relevantResults.filter((result) => result.status === 'rejected');
    setLoadError(failures.length === relevantResults.length ? 'The logistics workspace could not load. Retry when the API is available.' : null);
    if (failures.length > 0 && failures.length < relevantResults.length)
      setNotice({ kind: 'info', message: 'Some logistics panels could not refresh; available live data is still shown.' });

    if (ovRes.status === 'fulfilled') {
      const ov = ovRes.value;
      setOverview(ov);
      const nextOrders = orderRes.status === 'fulfilled' && orderRes.value ? orderRes.value.items : ov.orderCards;
      const nextRoutes = routeRes.status === 'fulfilled' && routeRes.value ? routeRes.value.items : ov.routeCards;
      const nextStops = stopRes.status === 'fulfilled' && stopRes.value ? stopRes.value.items : ov.liveStops;
      setOrders(nextOrders);
      setRoutes(nextRoutes);
      setStops(nextStops);
      setOrderTotal(orderRes.status === 'fulfilled' && orderRes.value ? orderRes.value.total : nextOrders.length);
      setStopTotal(stopRes.status === 'fulfilled' && stopRes.value ? stopRes.value.total : nextStops.length);
      syncSelectedRoute(nextRoutes, nextStops);
      return;
    }

    setOverview(null);
    setOrders(orderRes.status === 'fulfilled' && orderRes.value ? orderRes.value.items : []);
    setRoutes(routeRes.status === 'fulfilled' && routeRes.value ? routeRes.value.items : []);
    setStops(stopRes.status === 'fulfilled' && stopRes.value ? stopRes.value.items : []);
    setOrderTotal(orderRes.status === 'fulfilled' && orderRes.value ? orderRes.value.total : 0);
    setStopTotal(stopRes.status === 'fulfilled' && stopRes.value ? stopRes.value.total : 0);
    syncSelectedRoute(
      routeRes.status === 'fulfilled' && routeRes.value ? routeRes.value.items : [],
      stopRes.status === 'fulfilled' && stopRes.value ? stopRes.value.items : [],
    );
  };

  const refreshWorkspace = async (activeMode: DispatchMode = mode) => {
    await loadWorkspace(activeMode);
  };

  useEffect(() => {
    setLoading(true);
    const pending = loadWorkspace(mode);
    const sequence = requestSequence.current;
    pending
      .catch((err) => {
        if (sequence === requestSequence.current) setLoadError(notifyApiError(err, 'Unable to load logistics workspace.'));
      })
      .finally(() => {
        if (sequence === requestSequence.current) setLoading(false);
      });
  }, [mode, orderPage, orderStatus, routeStatus, stopPage, stopSearch, stopStatus]);

  useEffect(() => { setOrderPage(1); }, [orderStatus]);
  useEffect(() => { setStopPage(1); }, [stopStatus, stopSearch]);

  useEffect(() => {
    if (!selectedRouteId) {
      setRouteStops([]);
      return;
    }
    let cancelled = false;
    (async () => {
      try {
        const response = await logisticsApi.routeStops(selectedRouteId);
        if (!cancelled) setRouteStops(response.items);
      } catch (err) {
        if (!cancelled) setNotice({ kind: 'error', message: notifyApiError(err, 'Unable to load route stops.') });
      }
    })();
    return () => { cancelled = true; };
  }, [selectedRouteId]);

  const stats = useMemo(() => {
    if (!overview) return [];
    const s = overview.summary;
    return [
      { label: 'Active orders', value: s.activeOrders.toString(), hint: 'Queued + in motion', icon: Package },
      { label: 'Routes active', value: s.activeRoutes.toString(), hint: 'Today on road', icon: Route },
      { label: 'In transit', value: s.inTransit.toString(), hint: 'Needs close follow-up', icon: Truck },
      { label: 'On-time rate', value: `${s.onTimeRate.toFixed(1)}%`, hint: 'Route completion', icon: Clock3 },
    ];
  }, [overview]);

  const valuePillars = useMemo(() => {
    switch (mode) {
      case 'orders':
        return ['Demand intake', 'Priority control', 'Assignment readiness'];
      case 'routes':
        return ['Territory balance', 'Stop density', 'ETA discipline'];
      case 'delivery':
        return ['Recipient proof', 'Attempt recovery', 'Doorstep visibility'];
      default:
        return ['SLA command', 'Exception recovery', 'Unified dispatch truth'];
    }
  }, [mode]);

  const commandSignals = useMemo(() => {
    const topRoute = routes[0];
    const topStop = stops[0];
    const topOrder = orders[0];

    switch (mode) {
      case 'orders':
        return [
          {
            label: 'Intake under control',
            value: `${orders.filter((order) => order.status === 'Queued').length}`,
            note: 'Queued orders still waiting for operational ownership.',
          },
          {
            label: 'High-priority exposure',
            value: `${orders.filter((order) => order.priority === 'High' || order.priority === 'Critical').length}`,
            note: 'Orders likely to become escalation calls if left untouched.',
          },
          {
            label: 'Lead account in motion',
            value: topOrder?.customerName ?? 'Awaiting fresh orders',
            note: 'The account most visible to sales and customer service right now.',
          },
        ];
      case 'routes':
        return [
          {
            label: 'Most loaded route',
            value: topRoute?.routeCode ?? 'No route selected',
            note: topRoute ? `${topRoute.completedStops}/${topRoute.plannedStops} stops completed` : 'Route creation is available from this panel.',
          },
          {
            label: 'Distance commitment',
            value: `${routes.reduce((sum, route) => sum + route.distanceKm, 0).toFixed(0)} km`,
            note: 'Total planned distance currently sitting with the dispatch desk.',
          },
          {
            label: 'Planner pressure',
            value: `${routes.filter((route) => route.status !== 'Completed').length}`,
            note: 'Routes still active and likely to need monitoring or intervention.',
          },
        ];
      case 'delivery':
        return [
          {
            label: 'Proof-ready stops',
            value: `${stops.filter((stop) => stop.proofStatus === 'POD' || stop.proofStatus === 'Verified').length}`,
            note: 'Stops already carrying proof or ready for final verification.',
          },
          {
            label: 'Customer-facing risk',
            value: `${stops.filter((stop) => stop.status === 'Attempted' || stop.status === 'Rescheduled').length}`,
            note: 'Last-mile exceptions likely to generate customer follow-up.',
          },
          {
            label: 'Next stop due',
            value: topStop?.customerName ?? 'Awaiting route execution',
            note: topStop ? `${topStop.routeCode} · ${topStop.timeWindow}` : 'No live stop selected yet.',
          },
        ];
      default:
        return [
          {
            label: 'Orders needing movement',
            value: `${orders.filter((order) => order.status === 'Queued').length}`,
            note: 'Open order load still waiting to enter execution.',
          },
          {
            label: 'Routes under watch',
            value: `${routes.filter((route) => route.status !== 'Completed').length}`,
            note: 'Routes that remain active and commercially visible.',
          },
          {
            label: 'Exception touchpoints',
            value: `${stops.filter((stop) => stop.status === 'Attempted' || stop.status === 'Delayed').length}`,
            note: 'Delivery moments most likely to turn into support conversations.',
          },
        ];
    }
  }, [mode, orders, routes, stops]);

  const actionNarrative = useMemo(() => {
    switch (mode) {
      case 'orders':
        return 'Open orders ranked by urgency and promised time — dispatch from here.';
      case 'routes':
        return 'Active routes with stop load and completion pressure — progress them as stops close.';
      case 'delivery':
        return 'Every stop with its proof, attempt and reschedule state — recover exceptions here.';
      default:
        return 'Orders, routes and stops in one read — urgency, motion and exceptions.';
    }
  }, [mode]);

  const liveRecordRows = useMemo(() => {
    if (mode === 'routes') {
      return routes.slice(0, 5).map((route) => ({
        id: route.id,
        title: route.routeCode,
        subtitle: `${route.hub} · ${route.territory}`,
        status: route.status,
        actionLabel: 'Focus route',
        onClick: () => setSelectedRouteId(route.id),
      }));
    }

    if (mode === 'delivery') {
      return stops.slice(0, 5).map((stop) => ({
        id: stop.id,
        title: stop.customerName,
        subtitle: stop.addressLine,
        status: stop.status,
        actionLabel: 'Focus stop',
        onClick: () => {
          const matchingRoute = routes.find((route) => route.routeCode === stop.routeCode);
          if (matchingRoute) setSelectedRouteId(matchingRoute.id);
        },
      }));
    }

    return orders.slice(0, 5).map((order) => ({
      id: order.id,
      title: order.orderNumber,
      subtitle: `${order.customerName} · ${order.city}`,
      status: order.status,
      actionLabel: '',
      onClick: undefined,
    }));
  }, [mode, orders, routes, stops]);

  const alerts = overview?.alerts ?? [];
  const selectedRoute = routes.find((route) => route.id === selectedRouteId) ?? null;
  const visibleStops = mode === 'routes'
    ? routeStops
    : mode === 'delivery' && routeStops.length
      ? routeStops
      : stops;

  const reportError = (err: unknown, fallback: string) =>
    setNotice({ kind: 'error', message: notifyApiError(err, fallback) });

  const resetOrderForm = () => {
    setEditingOrderId(null);
    setOrderForm({ orderNumber: '', customerName: '', city: '', area: '', priority: 'Normal', routeCode: '', itemCount: '', orderValue: '', driverName: '', vehicleNumber: '', dispatchNotes: '', promisedAtUtc: '' });
  };

  const resetRouteForm = () => {
    setEditingRouteId(null);
    setRouteForm({ routeCode: '', hub: '', territory: '', driverName: '', vehicleNumber: '', plannedStops: '', distanceKm: '', plannedForDate: '', departureTimeUtc: '', notes: '' });
  };

  const beginEditOrder = (order: LogisticsOrder) => {
    setMode('orders');
    setEditingOrderId(order.id);
    setOrderForm({
      orderNumber: order.orderNumber ?? '', customerName: order.customerName ?? '', city: order.city ?? '', area: order.area ?? '',
      priority: order.priority ?? 'Normal', routeCode: order.routeCode ?? '', itemCount: String(order.itemCount ?? ''),
      orderValue: String(order.orderValue ?? ''), driverName: order.driverName ?? '', vehicleNumber: order.vehicleNumber ?? '',
      dispatchNotes: order.dispatchNotes ?? '', promisedAtUtc: toLocalInput(order.promisedAtUtc),
    });
  };

  const beginEditRoute = (route: LogisticsRoute) => {
    setMode('routes');
    setEditingRouteId(route.id);
    setRouteForm({
      routeCode: route.routeCode ?? '', hub: route.hub ?? '', territory: route.territory ?? '', driverName: route.driverName ?? '',
      vehicleNumber: route.vehicleNumber ?? '', plannedStops: String(route.plannedStops ?? ''), distanceKm: String(route.distanceKm ?? ''),
      plannedForDate: route.plannedForDate?.slice(0, 10) ?? '', departureTimeUtc: toLocalInput(route.departureTimeUtc), notes: route.notes ?? '',
    });
  };

  const selectOrderRoute = (routeCode: string) => {
    const route = routes.find((item) => item.routeCode === routeCode);
    setOrderForm((current) => ({
      ...current,
      routeCode,
      driverName: route?.driverName || current.driverName,
      vehicleNumber: route?.vehicleNumber || current.vehicleNumber,
    }));
  };

  const handleDispatch = async (order: LogisticsOrder) => {
    if (!canAssign) return setNotice({ kind: 'error', message: 'dispatch:assign is required to dispatch an order.' });
    if (order.status !== 'Queued') return setNotice({ kind: 'info', message: `Only queued orders can be dispatched from this workspace; ${order.orderNumber} is ${order.status}.` });
    if (!order.routeCode?.trim() || !order.driverName?.trim() || !order.vehicleNumber?.trim()) {
      beginEditOrder(order);
      return setNotice({ kind: 'info', message: 'Complete the route, driver, and vehicle assignment before dispatch.' });
    }
    setSavingId(order.id);
    try {
      await logisticsApi.dispatchOrder(order.id, {
        routeCode: order.routeCode,
        driverName: order.driverName,
        vehicleNumber: order.vehicleNumber,
        notes: order.dispatchNotes,
      });
      await refreshWorkspace();
      setNotice({ kind: 'success', message: `${order.orderNumber} was dispatched.` });
    } catch (err) {
      reportError(err, 'Unable to dispatch this order.');
    } finally {
      setSavingId(null);
    }
  };

  const handleProgress = async (route: LogisticsRoute) => {
    if (!canUpdate) return setNotice({ kind: 'error', message: 'dispatch:update is required to progress a route.' });
    if (!['Ready', 'Active', 'Delayed'].includes(route.status) || terminalRoute(route.status))
      return setNotice({ kind: 'info', message: `${route.routeCode} cannot be progressed from ${route.status}.` });
    if (route.plannedStops < 1 || route.completedStops >= route.plannedStops)
      return setNotice({ kind: 'info', message: `${route.routeCode} has no remaining planned stops to progress.` });
    setSavingId(route.id);
    try {
      await logisticsApi.progressRoute(route.id, {
        completedStopsDelta: 1,
        currentStop: route.currentStop,
        nextStop: route.nextStop,
        notes: 'Route advanced from the command center.',
      });
      await refreshWorkspace();
      setNotice({ kind: 'success', message: `${route.routeCode} advanced by one completed stop.` });
    } catch (err) {
      reportError(err, 'Unable to advance the route.');
    } finally {
      setSavingId(null);
    }
  };

  const handleConfirm = async (stop: LogisticsStop) => {
    if (!canDeliver) return setNotice({ kind: 'error', message: 'Proof or dispatch update permission is required to confirm delivery.' });
    if (terminalStop(stop.status)) return setNotice({ kind: 'info', message: 'This stop is already delivered.' });
    const recipientName = lastMileForm.recipientName.trim();
    if (!recipientName) return setNotice({ kind: 'error', message: 'Enter the actual recipient name before confirming delivery.' });
    setSavingId(stop.id);
    try {
      await logisticsApi.confirmDelivery(stop.id, {
        recipientName,
        proofStatus: 'Captured',
      });
      await refreshWorkspace();
      setNotice({ kind: 'success', message: `Delivery confirmed for ${stop.customerName}; proof and billing linkage were updated.` });
    } catch (err) {
      reportError(err, 'Unable to confirm delivery.');
    } finally {
      setSavingId(null);
    }
  };

  const handleAttempt = async (stop: LogisticsStop) => {
    if (!canUpdate) return setNotice({ kind: 'error', message: 'dispatch:update is required to record an attempt.' });
    if (terminalStop(stop.status)) return setNotice({ kind: 'info', message: 'A delivered stop cannot be attempted.' });
    const reason = lastMileForm.exceptionReason.trim();
    if (!reason) return setNotice({ kind: 'error', message: 'Enter the real delivery-attempt reason.' });
    const nextEtaUtc = toIsoOrUndefined(lastMileForm.nextEtaUtc);
    if (lastMileForm.nextEtaUtc && (!nextEtaUtc || new Date(nextEtaUtc).getTime() <= Date.now()))
      return setNotice({ kind: 'error', message: 'Next ETA must be a valid future time.' });
    setSavingId(stop.id);
    try {
      await logisticsApi.recordAttempt(stop.id, {
        status: 'Attempted',
        proofStatus: 'None',
        exceptionReason: reason,
        nextEtaUtc,
        nextStop: lastMileForm.nextStop.trim() || undefined,
      });
      await refreshWorkspace();
      setNotice({ kind: 'success', message: `Delivery attempt recorded for ${stop.customerName}.` });
    } catch (err) {
      reportError(err, 'Unable to record delivery attempt.');
    } finally {
      setSavingId(null);
    }
  };

  const handleReschedule = async (stop: LogisticsStop) => {
    if (!canUpdate) return setNotice({ kind: 'error', message: 'dispatch:update is required to reschedule a stop.' });
    if (terminalStop(stop.status)) return setNotice({ kind: 'info', message: 'A delivered stop cannot be rescheduled.' });
    const reason = lastMileForm.exceptionReason.trim();
    const nextEtaUtc = toIsoOrUndefined(lastMileForm.nextEtaUtc);
    if (!reason) return setNotice({ kind: 'error', message: 'Enter the customer-confirmed reschedule reason.' });
    if (!nextEtaUtc || new Date(nextEtaUtc).getTime() <= Date.now())
      return setNotice({ kind: 'error', message: 'Choose a valid future ETA before rescheduling.' });
    setSavingId(stop.id);
    try {
      await logisticsApi.rescheduleStop(stop.id, {
        timeWindow: lastMileForm.timeWindow.trim() || undefined,
        reason,
        nextEtaUtc,
      });
      await refreshWorkspace();
      setNotice({ kind: 'success', message: `${stop.customerName} was rescheduled.` });
    } catch (err) {
      reportError(err, 'Unable to reschedule stop.');
    } finally {
      setSavingId(null);
    }
  };

  const handleCreateOrder = async () => {
    if (!canCreate && !editingOrderId) return setNotice({ kind: 'error', message: 'dispatch:create is required to create an order.' });
    if (editingOrderId && !canUpdate) return setNotice({ kind: 'error', message: 'dispatch:update is required to edit an order.' });
    const orderNumber = orderForm.orderNumber.trim();
    const customerName = orderForm.customerName.trim();
    const itemCount = Number(orderForm.itemCount);
    const orderValue = Number(orderForm.orderValue || 0);
    if (!orderNumber || !customerName) return setNotice({ kind: 'error', message: 'Order number and customer name are required.' });
    if (!Number.isInteger(itemCount) || itemCount < 1 || itemCount > 100_000)
      return setNotice({ kind: 'error', message: 'Item count must be a whole number between 1 and 100000.' });
    if (!Number.isFinite(orderValue) || orderValue < 0 || orderValue > 1_000_000_000)
      return setNotice({ kind: 'error', message: 'Order value must be between 0 and 1000000000.' });
    setCreating(true);
    try {
      const body = {
        orderNumber,
        customerName,
        city: orderForm.city,
        area: orderForm.area,
        priority: orderForm.priority,
        routeCode: orderForm.routeCode,
        itemCount,
        orderValue,
        driverName: orderForm.driverName,
        vehicleNumber: orderForm.vehicleNumber,
        dispatchNotes: orderForm.dispatchNotes,
        promisedAtUtc: toIsoOrUndefined(orderForm.promisedAtUtc),
        customerSegment: 'Retail',
        salesChannel: 'Portal',
      };
      if (editingOrderId) await logisticsApi.updateOrder(editingOrderId, body);
      else await logisticsApi.createOrder({ ...body, status: 'Queued' });
      setNotice({ kind: 'success', message: editingOrderId ? `${orderNumber} was updated.` : `${orderNumber} was created in Queued status.` });
      resetOrderForm();
      await refreshWorkspace();
    } catch (err) {
      reportError(err, editingOrderId ? 'Unable to update order.' : 'Unable to create order.');
    } finally {
      setCreating(false);
    }
  };

  const handleCreateRoute = async () => {
    if (!canCreate && !editingRouteId) return setNotice({ kind: 'error', message: 'dispatch:create is required to create a route.' });
    if (editingRouteId && !canUpdate) return setNotice({ kind: 'error', message: 'dispatch:update is required to edit a route.' });
    const routeCode = routeForm.routeCode.trim();
    const plannedStops = Number(routeForm.plannedStops);
    const distanceKm = Number(routeForm.distanceKm || 0);
    const currentRoute = routes.find((route) => route.id === editingRouteId);
    if (!routeCode) return setNotice({ kind: 'error', message: 'Route code is required.' });
    if (!Number.isInteger(plannedStops) || plannedStops < 1 || plannedStops > 100_000)
      return setNotice({ kind: 'error', message: 'Planned stops must be a whole number between 1 and 100000.' });
    if (currentRoute && plannedStops < currentRoute.completedStops)
      return setNotice({ kind: 'error', message: 'Planned stops cannot be lower than already completed stops.' });
    if (!Number.isFinite(distanceKm) || distanceKm < 0)
      return setNotice({ kind: 'error', message: 'Distance cannot be negative.' });
    setCreating(true);
    try {
      const body = {
        routeCode,
        hub: routeForm.hub,
        territory: routeForm.territory,
        driverName: routeForm.driverName,
        vehicleNumber: routeForm.vehicleNumber,
        plannedStops,
        distanceKm,
        departureTimeUtc: toIsoOrUndefined(routeForm.departureTimeUtc),
        plannedForDate: routeForm.plannedForDate || undefined,
        notes: routeForm.notes,
      };
      if (editingRouteId) await logisticsApi.updateRoute(editingRouteId, body);
      else await logisticsApi.createRoute({ ...body, completedStops: 0, completionPercent: 0, status: 'Planned', currentStop: '', nextStop: '' });
      setNotice({ kind: 'success', message: editingRouteId ? `${routeCode} was updated.` : `${routeCode} was created with ${plannedStops} planned stop${plannedStops === 1 ? '' : 's'}.` });
      resetRouteForm();
      await refreshWorkspace();
    } catch (err) {
      reportError(err, editingRouteId ? 'Unable to update route.' : 'Unable to create route.');
    } finally {
      setCreating(false);
    }
  };

  const handleExport = async () => {
    try {
      await logisticsApi.exportLastMile({
        status: stopStatus === 'All' ? undefined : stopStatus,
        search: stopSearch.trim() || undefined,
      });
      setNotice({ kind: 'success', message: 'Filtered last-mile CSV export started.' });
    } catch (err) {
      reportError(err, 'Unable to export last-mile records.');
    }
  };

  return (
    <>
      <div className="fleet-console relative min-h-[100svh] overflow-hidden text-slate-900">

        <div className="relative z-10 mx-auto flex min-h-[100svh] w-full max-w-[1600px] flex-col px-4 py-3 sm:px-6 lg:px-8 lg:py-4">
          <div className="mb-4 flex items-center justify-between gap-4">
            <div className="flex items-center gap-3 rounded-[22px] border border-white/80 bg-white/74 px-4 py-3 shadow-[0_18px_40px_rgba(37,99,235,0.08)] backdrop-blur-xl dark:border-white/[0.08] dark:bg-white/[0.04]">
              <div className="rounded-[18px] bg-[radial-gradient(circle_at_top_left,rgba(255,255,255,0.45),transparent_45%),linear-gradient(160deg,rgba(29,78,216,0.98),rgba(8,47,122,0.98))] p-1.5 shadow-[0_12px_24px_rgba(37,99,235,0.22)]">
                <span className="text-sm font-black tracking-tight text-white">OpsTrax</span>
              </div>
              <div className="h-9 w-px bg-slate-300/60" />
              <div>
                <p className="text-[10px] font-bold tracking-[0.28em] uppercase text-blue-500/70">Dispatch & Delivery</p>
                <p className="text-[11px] text-slate-500">{config.label}</p>
              </div>
            </div>

            <nav aria-label="Logistics workspace views" className="hidden items-center gap-2 rounded-full border border-white/70 bg-white/72 p-1 shadow-[0_14px_30px_rgba(37,99,235,0.05)] backdrop-blur-xl md:flex dark:border-white/[0.08] dark:bg-white/[0.04]">
              {MODE_ORDER.map((item) => {
                const active = item === mode;
                return (
                  <button
                    key={item}
                    type="button"
                    aria-pressed={active}
                    onClick={() => setMode(item)}
                    className={`rounded-full px-4 py-2 text-[11px] font-semibold uppercase tracking-[0.18em] transition ${
                      active
                        ? 'bg-slate-950 text-white shadow-lg dark:bg-white dark:text-slate-950'
                        : 'text-slate-500 hover:bg-slate-100 dark:text-slate-400 dark:hover:bg-white/[0.05]'
                    }`}
                  >
                    {MODULES[item].label}
                  </button>
                );
              })}
            </nav>
          </div>

          <label className="mb-4 md:hidden">
            <span className="sr-only">Workspace view</span>
            <select
              aria-label="Workspace view"
              value={mode}
              onChange={(event) => setMode(event.target.value as DispatchMode)}
              className="w-full rounded-2xl border border-white/80 bg-white/90 px-4 py-3 text-sm font-semibold text-slate-800 shadow-sm dark:border-white/10 dark:bg-slate-900 dark:text-white"
            >
              {MODE_ORDER.map((item) => <option key={item} value={item}>{MODULES[item].label}</option>)}
            </select>
          </label>

          {notice && (
            <div
              role={notice.kind === 'error' ? 'alert' : 'status'}
              aria-live={notice.kind === 'error' ? 'assertive' : 'polite'}
              className={`mb-4 flex items-center justify-between gap-3 rounded-2xl border px-4 py-3 text-sm ${notice.kind === 'error' ? 'border-red-200 bg-red-50 text-red-800' : notice.kind === 'success' ? 'border-emerald-200 bg-emerald-50 text-emerald-800' : 'border-blue-200 bg-blue-50 text-blue-800'}`}
            >
              <span>{notice.message}</span>
              <button type="button" aria-label="Dismiss notification" onClick={() => setNotice(null)} className="font-bold">×</button>
            </div>
          )}
          {loadError && (
            <div role="alert" className="mb-4 flex flex-wrap items-center justify-between gap-3 rounded-2xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-800">
              <span>{loadError}</span>
              <button type="button" onClick={() => { setLoading(true); refreshWorkspace().finally(() => setLoading(false)); }} className="inline-flex items-center gap-2 rounded-xl bg-red-700 px-3 py-2 font-semibold text-white">
                <RefreshCw className="h-4 w-4" /> Retry
              </button>
            </div>
          )}

          <section className="grid flex-1 gap-4 lg:grid-cols-[1.05fr_0.95fr]">
            <div className="rounded-[32px] border border-white/75 bg-[linear-gradient(160deg,rgba(251,253,255,0.92),rgba(239,245,255,0.74))] p-5 shadow-[0_24px_80px_rgba(37,99,235,0.12)] backdrop-blur-3xl dark:border-white/[0.08] dark:bg-[linear-gradient(160deg,rgba(11,18,34,0.96),rgba(7,12,24,0.92))]">
              <div className="flex flex-wrap items-center gap-2">
                <span className={`inline-flex items-center gap-2 rounded-full border border-blue-300/30 bg-white/78 px-3 py-1.5 text-[10px] font-semibold uppercase tracking-[0.24em] text-blue-600 shadow-sm backdrop-blur`}>
                  <span className="live-dot h-1.5 w-1.5" />
                  Logistics
                </span>
                <span className={`inline-flex items-center gap-2 rounded-full bg-gradient-to-r ${config.accent} px-3 py-1.5 text-[10px] font-semibold uppercase tracking-[0.24em] text-white shadow-sm`}>
                  <Sparkles className="h-3.5 w-3.5" />
                  {config.label}
                </span>
              </div>

              <div className="mt-5 max-w-3xl">
                <h1 className="text-[40px] font-black leading-[1.02] tracking-tight text-slate-950 xl:text-[54px] dark:text-white">
                  {config.title}
                </h1>
                <p className="mt-4 max-w-2xl text-[15px] leading-relaxed text-slate-600 dark:text-slate-400">
                  {config.subtitle}
                </p>
                <p className="mt-3 max-w-2xl text-[13px] leading-relaxed text-slate-500 dark:text-slate-500">
                  {config.summary}
                </p>
                <div className="mt-4 flex flex-wrap gap-2">
                  {valuePillars.map((pillar) => (
                    <span key={pillar} className="rounded-full border border-white/80 bg-white/78 px-3 py-1.5 text-[10px] font-semibold uppercase tracking-[0.18em] text-slate-600 shadow-sm backdrop-blur dark:border-white/10 dark:bg-white/[0.04] dark:text-slate-300">
                      {pillar}
                    </span>
                  ))}
                </div>

                <div className="mt-4 flex flex-wrap items-end gap-3 rounded-2xl border border-slate-200/70 bg-white/65 p-3 dark:border-white/10 dark:bg-white/[0.03]">
                  {mode === 'orders' && (
                    <label className="min-w-48 text-xs font-semibold text-slate-600 dark:text-slate-300">
                      Order status
                      <select aria-label="Order status filter" value={orderStatus} onChange={(event) => setOrderStatus(event.target.value)} className="mt-1 block w-full rounded-xl border border-slate-200 bg-white px-3 py-2 text-sm dark:border-white/10 dark:bg-slate-900">
                        {['All', 'Queued', 'Dispatched', 'InTransit', 'Exception', 'Delivered', 'Returned'].map((status) => <option key={status}>{status}</option>)}
                      </select>
                    </label>
                  )}
                  {mode === 'routes' && (
                    <label className="min-w-48 text-xs font-semibold text-slate-600 dark:text-slate-300">
                      Route status
                      <select aria-label="Route status filter" value={routeStatus} onChange={(event) => setRouteStatus(event.target.value)} className="mt-1 block w-full rounded-xl border border-slate-200 bg-white px-3 py-2 text-sm dark:border-white/10 dark:bg-slate-900">
                        {['All', 'Planned', 'Ready', 'Active', 'Delayed', 'Closed', 'Completed'].map((status) => <option key={status}>{status}</option>)}
                      </select>
                    </label>
                  )}
                  {mode === 'delivery' && (
                    <>
                      <label className="min-w-48 flex-1 text-xs font-semibold text-slate-600 dark:text-slate-300">
                        Search stops
                        <input aria-label="Search last-mile stops" value={stopSearch} onChange={(event) => setStopSearch(event.target.value)} placeholder="Order, customer, route, city" className="mt-1 block w-full rounded-xl border border-slate-200 bg-white px-3 py-2 text-sm dark:border-white/10 dark:bg-slate-900" />
                      </label>
                      <label className="min-w-44 text-xs font-semibold text-slate-600 dark:text-slate-300">
                        Stop status
                        <select aria-label="Last-mile status filter" value={stopStatus} onChange={(event) => setStopStatus(event.target.value)} className="mt-1 block w-full rounded-xl border border-slate-200 bg-white px-3 py-2 text-sm dark:border-white/10 dark:bg-slate-900">
                          {['All', 'OutForDelivery', 'Attempted', 'Failed', 'Rescheduled', 'Delivered'].map((status) => <option key={status}>{status}</option>)}
                        </select>
                      </label>
                      <button type="button" onClick={handleExport} className="inline-flex items-center gap-2 rounded-xl bg-slate-900 px-4 py-2.5 text-sm font-semibold text-white dark:bg-white dark:text-slate-900"><Download className="h-4 w-4" /> Export CSV</button>
                    </>
                  )}
                </div>
              </div>

              <div className="mt-6 grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
                {loading ? (
                  Array.from({ length: 4 }).map((_, index) => (
                    <div
                      key={index}
                      className="rounded-[24px] border border-white/80 bg-white/78 p-4 shadow-[0_12px_26px_rgba(37,99,235,0.05)] backdrop-blur-xl dark:border-white/[0.06] dark:bg-white/[0.04]"
                    >
                      <div className="space-y-3">
                        <div className="h-3 w-20 animate-pulse rounded bg-slate-200/80 dark:bg-white/10" />
                        <div className="h-9 w-16 animate-pulse rounded bg-slate-200/80 dark:bg-white/10" />
                        <div className="h-3 w-28 animate-pulse rounded bg-slate-200/80 dark:bg-white/10" />
                      </div>
                    </div>
                  ))
                ) : (
                  stats.map((stat) => (
                    <div
                      key={stat.label}
                      className="rounded-[24px] border border-white/80 bg-white/78 p-4 shadow-[0_12px_26px_rgba(37,99,235,0.05)] backdrop-blur-xl dark:border-white/[0.06] dark:bg-white/[0.04]"
                    >
                      <div className="flex items-center justify-between">
                        <p className="text-[10px] font-semibold uppercase tracking-[0.2em] text-slate-400">{stat.label}</p>
                        <stat.icon className="h-4 w-4 text-blue-500/70" />
                      </div>
                      <p className="mt-2 text-[28px] font-black tracking-tight text-slate-950 dark:text-white">{stat.value}</p>
                      <p className="mt-1 text-[12px] text-slate-500 dark:text-slate-500">{stat.hint}</p>
                    </div>
                  ))
                )}
              </div>

              <div className="mt-4 grid gap-4 xl:grid-cols-[1.25fr_0.75fr]">
                <div className="rounded-[28px] border border-white/80 bg-white/74 p-4 shadow-[0_18px_40px_rgba(37,99,235,0.06)] backdrop-blur-xl dark:border-white/[0.06] dark:bg-white/[0.04]">
                  <div className="mb-4 grid gap-3 lg:grid-cols-3">
                    {commandSignals.map((signal) => (
                      <div key={signal.label} className="rounded-[22px] border border-slate-200/70 bg-[linear-gradient(180deg,rgba(255,255,255,0.92),rgba(244,248,255,0.84))] p-3.5 dark:border-white/10 dark:bg-white/[0.03]">
                        <p className="text-[10px] font-semibold uppercase tracking-[0.2em] text-slate-400">{signal.label}</p>
                        <p className="mt-2 text-[24px] font-black tracking-tight text-slate-950 dark:text-white">{signal.value}</p>
                        <p className="mt-1 text-[11px] leading-relaxed text-slate-500 dark:text-slate-400">{signal.note}</p>
                      </div>
                    ))}
                  </div>

                  <div className="mb-3 flex items-center justify-between gap-3">
                    <div>
                      <p className="text-[10px] font-semibold uppercase tracking-[0.22em] text-slate-400">Operational board</p>
                      <p className="mt-1 text-[18px] font-black tracking-tight text-slate-950 dark:text-white">
                        {mode === 'routes' ? 'Route load and completion' : mode === 'delivery' ? 'Last mile progress' : 'Orders, routes, and exceptions'}
                      </p>
                    </div>

                  </div>

                  <div className="space-y-3">
                    {mode === 'routes' ? (
                      routes.slice(0, 4).map((route) => (
                        <ActionRouteCard key={route.id} route={route} canUpdate={canUpdate} saving={savingId === route.id} onAdvance={() => handleProgress(route)} onInspect={() => setSelectedRouteId(route.id)} onEdit={() => beginEditRoute(route)} />
                      ))
                    ) : mode === 'delivery' ? (
                      stops.slice(0, 6).map((stop) => (
                        <ActionStopCard key={stop.id} stop={stop} canUpdate={canUpdate} canDeliver={canDeliver} saving={savingId === stop.id} onConfirm={() => handleConfirm(stop)} onAttempt={() => handleAttempt(stop)} onReschedule={() => handleReschedule(stop)} />
                      ))
                    ) : (
                      orders.slice(0, 6).map((order) => (
                        <ActionOrderCard key={order.id} order={order} canAssign={canAssign} canUpdate={canUpdate} saving={savingId === order.id} onDispatch={() => handleDispatch(order)} onEdit={() => beginEditOrder(order)} />
                      ))
                    )}
                    {!loading && ((mode === 'routes' && routes.length === 0) || (mode === 'delivery' && stops.length === 0) || ((mode === 'dispatch' || mode === 'orders') && orders.length === 0)) && (
                      <div className="rounded-2xl border border-dashed border-slate-300 p-6 text-center text-sm text-slate-500 dark:border-white/15 dark:text-slate-400">No records match this view and its current filters.</div>
                    )}
                  </div>
                  {(mode === 'orders' || mode === 'delivery') && (
                    <div className="mt-4 flex items-center justify-between gap-3 text-xs text-slate-500">
                      <span>{mode === 'orders' ? orderTotal : stopTotal} total record{(mode === 'orders' ? orderTotal : stopTotal) === 1 ? '' : 's'}</span>
                      <div className="flex items-center gap-2">
                        <button type="button" disabled={(mode === 'orders' ? orderPage : stopPage) <= 1 || loading} onClick={() => mode === 'orders' ? setOrderPage((page) => Math.max(1, page - 1)) : setStopPage((page) => Math.max(1, page - 1))} className="rounded-xl border border-slate-200 bg-white px-3 py-2 font-semibold disabled:opacity-40 dark:border-white/10 dark:bg-white/[0.04]">Previous</button>
                        <span aria-live="polite">Page {mode === 'orders' ? orderPage : stopPage}</span>
                        <button type="button" disabled={loading || (mode === 'orders' ? orderPage * PAGE_SIZE >= orderTotal : stopPage * PAGE_SIZE >= stopTotal)} onClick={() => mode === 'orders' ? setOrderPage((page) => page + 1) : setStopPage((page) => page + 1)} className="rounded-xl border border-slate-200 bg-white px-3 py-2 font-semibold disabled:opacity-40 dark:border-white/10 dark:bg-white/[0.04]">Next</button>
                      </div>
                    </div>
                  )}
                </div>

                <div className="rounded-[28px] border border-white/80 bg-white/74 p-4 shadow-[0_18px_40px_rgba(37,99,235,0.06)] backdrop-blur-xl dark:border-white/[0.06] dark:bg-white/[0.04]">
                  <p className="text-[10px] font-semibold uppercase tracking-[0.22em] text-slate-400">Operational cues</p>
                  <div className="mt-3 rounded-[22px] border border-slate-200/70 bg-[linear-gradient(135deg,rgba(19,30,56,0.96),rgba(31,87,184,0.86))] p-4 text-white shadow-[0_18px_40px_rgba(37,99,235,0.18)]">
                    <p className="text-[10px] font-semibold uppercase tracking-[0.2em] text-white/60">Executive readout</p>
                    <p className="mt-2 text-[16px] font-black leading-snug">{mode === 'delivery' ? 'Protect the customer moment.' : mode === 'routes' ? 'Shape the day before it shapes you.' : 'Keep service promises commercially safe.'}</p>
                    <p className="mt-2 text-[12px] leading-relaxed text-white/78">{actionNarrative}</p>
                  </div>
                  <div className="mt-3 space-y-3">
                    {alerts.length ? alerts.slice(0, 4).map((alert) => (
                      <div key={alert.orderNumber} className="rounded-2xl border border-amber-200/60 bg-amber-50/70 p-3 dark:border-amber-400/10 dark:bg-amber-400/8">
                        <div className="flex items-center justify-between gap-3">
                          <p className="text-[12px] font-bold text-amber-800 dark:text-amber-200">{alert.orderNumber}</p>
                          <span className="rounded-full bg-white/70 px-2 py-1 text-[9px] font-semibold uppercase tracking-[0.18em] text-amber-700 dark:bg-white/10 dark:text-amber-300">
                            {alert.status}
                          </span>
                        </div>
                        <p className="mt-1 text-[12px] font-medium text-slate-700 dark:text-slate-300">{alert.customerName}</p>
                        <p className="mt-1 text-[11px] leading-relaxed text-slate-500 dark:text-slate-400">
                          {alert.exceptionReason || 'Keep this delivery visible and move fast on recovery.'}
                        </p>
                      </div>
                    )) : (
                      <div className="rounded-2xl border border-slate-200/70 bg-slate-50/70 p-4 text-sm text-slate-500 dark:border-white/10 dark:bg-white/[0.03] dark:text-slate-400">
                        No urgent exceptions. The board is clean.
                      </div>
                    )}
                  </div>

                  <div className="mt-4 rounded-2xl border border-slate-200/70 bg-[linear-gradient(180deg,rgba(255,255,255,0.88),rgba(245,249,255,0.78))] p-4 dark:border-white/10 dark:bg-[linear-gradient(180deg,rgba(255,255,255,0.05),rgba(255,255,255,0.02))]">
                    <div className="flex items-center justify-between">
                      <p className="text-[10px] font-semibold uppercase tracking-[0.22em] text-slate-400">Recent records</p>
                      <ArrowRight className="h-4 w-4 text-blue-500/70" />
                    </div>
                    <div className="mt-3 space-y-2">
                      {liveRecordRows.map((item) => (
                        <button
                          key={item.id}
                          type="button"
                          onClick={item.onClick}
                          disabled={!item.onClick}
                          className={`ops-live-row ${item.onClick ? 'ops-live-row-action' : 'ops-live-row-static'}`}
                        >
                          <div className="min-w-0">
                            <p className="truncate text-[12px] font-semibold text-slate-800 dark:text-slate-200">{item.title}</p>
                            <p className="truncate text-[10px] text-slate-400 dark:text-slate-500">
                              {item.subtitle}
                            </p>
                          </div>
                          <span className="rounded-full bg-slate-100 px-2 py-1 text-[9px] font-semibold uppercase tracking-[0.18em] text-slate-500 dark:bg-white/[0.05] dark:text-slate-300">
                            {item.status}
                          </span>
                          {item.onClick ? (
                            <span className="inline-flex items-center gap-1 text-[10px] font-semibold uppercase tracking-[0.18em] text-blue-500/80">
                              {item.actionLabel}
                              <ChevronRight className="h-4 w-4 shrink-0 text-slate-300" />
                            </span>
                          ) : (
                            <span className="text-[10px] font-semibold uppercase tracking-[0.18em] text-slate-300">—</span>
                          )}
                        </button>
                      ))}
                    </div>
                  </div>
                </div>
              </div>
            </div>

            <div className="rounded-[32px] border border-white/75 bg-[linear-gradient(180deg,rgba(12,18,34,0.96),rgba(7,12,24,0.92))] p-5 shadow-[0_24px_80px_rgba(0,0,0,0.32)] backdrop-blur-3xl">
              <div className="flex items-center justify-between gap-3">
                <div className="rounded-full border border-white/10 bg-white/5 px-3 py-1.5 text-[10px] font-semibold uppercase tracking-[0.22em] text-white/70">
                  {config.label}
                </div>

              </div>

              <div className="mt-5">
                <h2 className="text-[28px] font-black leading-tight text-white xl:text-[34px]">
                  Route intelligence and intake
                </h2>
                <p className="mt-3 max-w-xl text-[14px] leading-relaxed text-slate-300/80">
                  Route status, order intake and last-mile visibility for the current tenant.
                </p>
              </div>

              <div className="mt-6 grid gap-3">
                <div className="rounded-[24px] border border-white/10 bg-white/[0.04] p-4">
                  <div className="flex items-center justify-between">
                    <p className="text-[10px] font-semibold uppercase tracking-[0.22em] text-white/50">Route intelligence</p>
                    <MapPinned className="h-4 w-4 text-cyan-300/80" />
                  </div>
                  <div className="mt-3 space-y-3">
                    {routes.slice(0, 3).map((route) => (
                      <div key={route.id} className="rounded-2xl border border-white/10 bg-white/[0.03] p-3">
                        <div className="flex items-center justify-between gap-3">
                          <div>
                            <p className="text-[12px] font-bold text-white">{route.routeCode}</p>
                            <p className="text-[11px] text-slate-400">{route.driverName} · {route.vehicleNumber}</p>
                          </div>
                          <span className="text-[10px] font-bold text-cyan-300">{route.completionPercent.toFixed(1)}%</span>
                        </div>
                        <div className="mt-2 h-1.5 rounded-full bg-white/10">
                          <div className="h-1.5 rounded-full bg-gradient-to-r from-cyan-400 via-sky-400 to-blue-500" style={{ width: `${Math.min(100, route.completionPercent)}%` }} />
                        </div>
                      </div>
                    ))}
                  </div>
                </div>

                <div className="rounded-[24px] border border-white/10 bg-white/[0.04] p-4">
                  <div className="flex items-center justify-between">
                    <p className="text-[10px] font-semibold uppercase tracking-[0.22em] text-white/50">
                      {mode === 'orders' ? 'Create job or order' : mode === 'routes' ? 'Create route' : mode === 'delivery' ? 'Delivery exception notes' : 'Create dispatch order'}
                    </p>
                    <Package className="h-4 w-4 text-sky-300/80" />
                  </div>
                  <div className="mt-3 space-y-3">
                    {(mode === 'dispatch' || mode === 'orders') && (canCreate || (editingOrderId && canUpdate) ? (
                      <>
                        <DarkField label="Order number" value={orderForm.orderNumber} onChange={(value) => setOrderForm((current) => ({ ...current, orderNumber: value }))} required disabled={Boolean(editingOrderId)} />
                        <DarkField label="Customer name" value={orderForm.customerName} onChange={(value) => setOrderForm((current) => ({ ...current, customerName: value }))} required />
                        <div className="grid grid-cols-2 gap-2">
                          <DarkField label="City" value={orderForm.city} onChange={(value) => setOrderForm((current) => ({ ...current, city: value }))} />
                          <DarkField label="Area" value={orderForm.area} onChange={(value) => setOrderForm((current) => ({ ...current, area: value }))} />
                        </div>
                        <div className="grid grid-cols-2 gap-2">
                          <DarkField label="Item count" type="number" min="1" max="100000" value={orderForm.itemCount} onChange={(value) => setOrderForm((current) => ({ ...current, itemCount: value }))} required />
                          <DarkField label="Order value" type="number" min="0" step="0.01" value={orderForm.orderValue} onChange={(value) => setOrderForm((current) => ({ ...current, orderValue: value }))} />
                        </div>
                        <label className="block text-[11px] font-semibold text-white/70">Priority
                          <select value={orderForm.priority} onChange={(event) => setOrderForm((current) => ({ ...current, priority: event.target.value }))} className="mt-1 w-full rounded-2xl border border-white/10 bg-slate-900 px-3 py-2.5 text-sm text-white">
                            {['Low', 'Normal', 'High', 'Critical'].map((priority) => <option key={priority}>{priority}</option>)}
                          </select>
                        </label>
                        <label className="block text-[11px] font-semibold text-white/70">Assigned route
                          <select value={orderForm.routeCode} onChange={(event) => selectOrderRoute(event.target.value)} className="mt-1 w-full rounded-2xl border border-white/10 bg-slate-900 px-3 py-2.5 text-sm text-white">
                            <option value="">Unassigned</option>
                            {routes.filter((route) => !terminalRoute(route.status)).map((route) => <option key={route.id} value={route.routeCode}>{route.routeCode} · {route.status}</option>)}
                          </select>
                        </label>
                        <div className="grid grid-cols-2 gap-2">
                          <DarkField label="Driver name" value={orderForm.driverName} onChange={(value) => setOrderForm((current) => ({ ...current, driverName: value }))} />
                          <DarkField label="Vehicle number" value={orderForm.vehicleNumber} onChange={(value) => setOrderForm((current) => ({ ...current, vehicleNumber: value }))} />
                        </div>
                        <DarkField label="Promised time" type="datetime-local" value={orderForm.promisedAtUtc} onChange={(value) => setOrderForm((current) => ({ ...current, promisedAtUtc: value }))} />
                        <label className="block text-[11px] font-semibold text-white/70">Dispatch notes
                          <textarea value={orderForm.dispatchNotes} onChange={(event) => setOrderForm((current) => ({ ...current, dispatchNotes: event.target.value }))} rows={2} className="mt-1 w-full rounded-2xl border border-white/10 bg-white/[0.05] px-3 py-2.5 text-sm text-white outline-none" />
                        </label>
                        <div className="flex gap-2">
                          {editingOrderId && <button type="button" onClick={resetOrderForm} className="rounded-2xl border border-white/15 px-4 py-3 text-xs font-bold text-white">Cancel</button>}
                          <button type="button" onClick={handleCreateOrder} disabled={creating} className="inline-flex flex-1 items-center justify-center rounded-2xl bg-gradient-to-r from-blue-600 via-sky-500 to-cyan-400 px-4 py-3 text-[12px] font-bold text-white disabled:opacity-60">
                            {creating ? 'Saving...' : editingOrderId ? 'Save order' : 'Create order'}
                          </button>
                        </div>
                      </>
                    ) : <ReadOnlyMessage />)}
                    {mode === 'routes' && (canCreate || (editingRouteId && canUpdate) ? (
                      <>
                        <DarkField label="Route code" value={routeForm.routeCode} onChange={(value) => setRouteForm((current) => ({ ...current, routeCode: value }))} required disabled={Boolean(editingRouteId)} />
                        <div className="grid grid-cols-2 gap-2">
                          <DarkField label="Hub" value={routeForm.hub} onChange={(value) => setRouteForm((current) => ({ ...current, hub: value }))} />
                          <DarkField label="Territory" value={routeForm.territory} onChange={(value) => setRouteForm((current) => ({ ...current, territory: value }))} />
                        </div>
                        <div className="grid grid-cols-2 gap-2">
                          <DarkField label="Driver name" value={routeForm.driverName} onChange={(value) => setRouteForm((current) => ({ ...current, driverName: value }))} />
                          <DarkField label="Vehicle number" value={routeForm.vehicleNumber} onChange={(value) => setRouteForm((current) => ({ ...current, vehicleNumber: value }))} />
                        </div>
                        <div className="grid grid-cols-2 gap-2">
                          <DarkField label="Planned stops" type="number" min="1" max="100000" value={routeForm.plannedStops} onChange={(value) => setRouteForm((current) => ({ ...current, plannedStops: value }))} required />
                          <DarkField label="Distance (km)" type="number" min="0" step="0.1" value={routeForm.distanceKm} onChange={(value) => setRouteForm((current) => ({ ...current, distanceKm: value }))} />
                        </div>
                        <div className="grid grid-cols-2 gap-2">
                          <DarkField label="Planned date" type="date" value={routeForm.plannedForDate} onChange={(value) => setRouteForm((current) => ({ ...current, plannedForDate: value }))} />
                          <DarkField label="Departure time" type="datetime-local" value={routeForm.departureTimeUtc} onChange={(value) => setRouteForm((current) => ({ ...current, departureTimeUtc: value }))} />
                        </div>
                        <label className="block text-[11px] font-semibold text-white/70">Route notes
                          <textarea value={routeForm.notes} onChange={(event) => setRouteForm((current) => ({ ...current, notes: event.target.value }))} rows={2} className="mt-1 w-full rounded-2xl border border-white/10 bg-white/[0.05] px-3 py-2.5 text-sm text-white outline-none" />
                        </label>
                        <div className="flex gap-2">
                          {editingRouteId && <button type="button" onClick={resetRouteForm} className="rounded-2xl border border-white/15 px-4 py-3 text-xs font-bold text-white">Cancel</button>}
                          <button type="button" onClick={handleCreateRoute} disabled={creating} className="inline-flex flex-1 items-center justify-center rounded-2xl bg-gradient-to-r from-sky-600 via-cyan-500 to-teal-400 px-4 py-3 text-[12px] font-bold text-white disabled:opacity-60">
                            {creating ? 'Saving...' : editingRouteId ? 'Save route' : 'Create route'}
                          </button>
                        </div>
                      </>
                    ) : <ReadOnlyMessage />)}
                    {mode === 'delivery' && (canUpdate || canDeliver ? (
                      <>
                        <p className="text-[11px] leading-relaxed text-white/55">Enter real delivery evidence before selecting an action. A recipient is required for Deliver; a reason is required for Attempt; a reason and future ETA are required for Reschedule.</p>
                        <DarkField label="Actual recipient name" value={lastMileForm.recipientName} onChange={(value) => setLastMileForm((current) => ({ ...current, recipientName: value }))} />
                        <label className="block text-[11px] font-semibold text-white/70">Attempt or reschedule reason
                          <textarea value={lastMileForm.exceptionReason} onChange={(event) => setLastMileForm((current) => ({ ...current, exceptionReason: event.target.value }))} rows={3} className="mt-1 w-full rounded-2xl border border-white/10 bg-white/[0.05] px-3 py-2.5 text-sm text-white outline-none" />
                        </label>
                        <DarkField label="Next ETA" type="datetime-local" value={lastMileForm.nextEtaUtc} onChange={(value) => setLastMileForm((current) => ({ ...current, nextEtaUtc: value }))} />
                        <div className="grid grid-cols-2 gap-2">
                          <DarkField label="Time window" value={lastMileForm.timeWindow} onChange={(value) => setLastMileForm((current) => ({ ...current, timeWindow: value }))} />
                          <DarkField label="Next stop" value={lastMileForm.nextStop} onChange={(value) => setLastMileForm((current) => ({ ...current, nextStop: value }))} />
                        </div>
                      </>
                    ) : <ReadOnlyMessage />)}
                  </div>
                </div>

                <div className="rounded-[24px] border border-white/10 bg-white/[0.04] p-4">
                  <div className="flex items-center justify-between">
                    <p className="text-[10px] font-semibold uppercase tracking-[0.22em] text-white/50">
                      {mode === 'routes' ? 'Stops on selected route' : 'Last-mile visibility'}
                    </p>
                    <Truck className="h-4 w-4 text-sky-300/80" />
                  </div>
                  <div className="mt-3 space-y-2">
                    {mode === 'delivery' && routes.length > 0 && (
                      <div className="mb-3 flex flex-wrap gap-2">
                        {routes.slice(0, 4).map((route) => {
                          const active = route.id === selectedRouteId;
                          return (
                            <button
                              key={route.id}
                              type="button"
                              onClick={() => setSelectedRouteId(route.id)}
                              className={`rounded-full px-3 py-1.5 text-[10px] font-semibold uppercase tracking-[0.18em] transition ${
                                active
                                  ? 'bg-cyan-400 text-slate-950 shadow-[0_10px_24px_rgba(34,211,238,0.18)]'
                                  : 'border border-white/10 bg-white/[0.04] text-white/65 hover:bg-white/[0.08]'
                              }`}
                            >
                              {route.routeCode}
                            </button>
                          );
                        })}
                      </div>
                    )}
                    {mode === 'delivery' && selectedRoute && (
                      <p className="mb-3 text-[10px] uppercase tracking-[0.18em] text-white/45">
                        Showing stops for {selectedRoute.routeCode} · {selectedRoute.driverName || 'Assigned driver pending'}
                      </p>
                    )}
                    {visibleStops.slice(0, 4).map((stop) => (
                      <div key={stop.id} className="rounded-2xl border border-white/10 bg-white/[0.03] px-3 py-2.5">
                        <div className="flex items-center justify-between gap-3">
                          <div className="min-w-0">
                            <p className="truncate text-[12px] font-bold text-white">{stop.customerName}</p>
                            <p className="truncate text-[10px] text-slate-400">{stop.addressLine}</p>
                          </div>
                          <span className="rounded-full border border-white/10 px-2 py-1 text-[9px] font-semibold uppercase tracking-[0.18em] text-white/65">
                            {stop.status}
                          </span>
                        </div>
                      </div>
                    ))}
                  </div>
                </div>
              </div>

              <div className="mt-6 rounded-[26px] border border-white/10 bg-white/[0.04] p-4">
                <p className="text-[11px] font-semibold uppercase tracking-[0.22em] text-white/70">Related modules</p>
                <div className="mt-3 flex flex-wrap gap-2">
                  <Link to="/dispatch" className="rounded-full border border-white/15 px-3 py-1.5 text-[11px] font-semibold text-white/80 transition hover:bg-white/10">Dispatch board</Link>
                  <Link to="/route-plans" className="rounded-full border border-white/15 px-3 py-1.5 text-[11px] font-semibold text-white/80 transition hover:bg-white/10">Route plans</Link>
                  <Link to="/last-mile-delivery" className="rounded-full border border-white/15 px-3 py-1.5 text-[11px] font-semibold text-white/80 transition hover:bg-white/10">Last mile</Link>
                  <Link to="/jobs" className="rounded-full border border-white/15 px-3 py-1.5 text-[11px] font-semibold text-white/80 transition hover:bg-white/10">Jobs board</Link>
                  <Link to="/trips" className="rounded-full border border-white/15 px-3 py-1.5 text-[11px] font-semibold text-white/80 transition hover:bg-white/10">Trips</Link>
                  <Link to="/operations/proof-center" className="rounded-full border border-white/15 px-3 py-1.5 text-[11px] font-semibold text-white/80 transition hover:bg-white/10">Proof center</Link>
                  <Link to="/finance/billing" className="rounded-full border border-white/15 px-3 py-1.5 text-[11px] font-semibold text-white/80 transition hover:bg-white/10">Billing</Link>
                </div>
              </div>
            </div>
          </section>
        </div>
      </div>
    </>
  );
}

function ActionOrderCard({ order, onDispatch, onEdit, saving, canAssign, canUpdate }: { order: LogisticsOrder; onDispatch: () => void; onEdit: () => void; saving: boolean; canAssign: boolean; canUpdate: boolean }) {
  const dispatchable = order.status === 'Queued' && Boolean(order.routeCode?.trim() && order.driverName?.trim() && order.vehicleNumber?.trim());
  return (
    <div className="rounded-[24px] border border-slate-200/70 bg-[linear-gradient(180deg,rgba(255,255,255,0.92),rgba(245,248,255,0.78))] p-4 shadow-[0_10px_24px_rgba(37,99,235,0.05)] dark:border-white/10 dark:bg-white/[0.04]">
      <div className="flex items-start justify-between gap-3">
        <div>
          <p className="text-[12px] font-black tracking-tight text-slate-950 dark:text-white">{order.orderNumber}</p>
          <p className="text-[11px] text-slate-500 dark:text-slate-400">{order.customerName} · {order.city}</p>
        </div>
        <span className="rounded-full border border-slate-200/70 bg-white px-2.5 py-1 text-[9px] font-semibold uppercase tracking-[0.18em] text-slate-500 dark:border-white/10 dark:bg-white/[0.05] dark:text-slate-300">
          {order.status}
        </span>
      </div>
      <div className="mt-3 flex flex-wrap items-center gap-2 text-[10px] font-semibold uppercase tracking-[0.18em] text-slate-400">
        <span>{order.routeCode}</span>
        <span>•</span>
        <span>{order.priority}</span>
        <span>•</span>
        <span>{order.driverName}</span>
      </div>
      <p className="mt-3 text-[11px] leading-relaxed text-slate-500 dark:text-slate-400">
        {order.priority === 'High' || order.priority === 'Critical'
          ? 'Priority order with visible service impact if dispatch slips.'
          : 'Ready for operational assignment and route ownership.'}
      </p>
      {(canAssign || canUpdate) && !terminalOrder(order.status) && (
        <div className={`mt-4 grid gap-2 ${canAssign && canUpdate ? 'grid-cols-2' : 'grid-cols-1'}`}>
          {canUpdate && <button type="button" onClick={onEdit} disabled={saving} className="inline-flex items-center justify-center gap-2 rounded-2xl border border-slate-200 bg-white px-4 py-3 text-xs font-bold text-slate-700 disabled:opacity-50 dark:border-white/10 dark:bg-white/[0.05] dark:text-white"><Pencil className="h-3.5 w-3.5" /> Edit</button>}
          {canAssign && <button type="button" onClick={dispatchable ? onDispatch : onEdit} disabled={saving || order.status !== 'Queued'} title={!dispatchable && order.status === 'Queued' ? 'Complete route, driver, and vehicle assignment first' : undefined} className="inline-flex items-center justify-center gap-2 rounded-2xl bg-gradient-to-r from-blue-600 via-sky-500 to-cyan-400 px-4 py-3 text-xs font-bold text-white disabled:cursor-not-allowed disabled:opacity-50">
            {saving ? 'Dispatching...' : dispatchable ? 'Dispatch' : 'Complete assignment'} <ArrowRight className="h-4 w-4" />
          </button>}
        </div>
      )}
    </div>
  );
}

function ActionRouteCard({ route, onAdvance, onInspect, onEdit, saving, canUpdate }: { route: LogisticsRoute; onAdvance: () => void; onInspect: () => void; onEdit: () => void; saving: boolean; canUpdate: boolean }) {
  const canProgress = canUpdate && ['Ready', 'Active', 'Delayed'].includes(route.status) && route.plannedStops > route.completedStops;
  return (
    <div className="rounded-[24px] border border-slate-200/70 bg-[linear-gradient(180deg,rgba(255,255,255,0.92),rgba(245,248,255,0.78))] p-4 shadow-[0_10px_24px_rgba(37,99,235,0.05)] dark:border-white/10 dark:bg-white/[0.04]">
      <div className="flex items-start justify-between gap-3">
        <div>
          <p className="text-[12px] font-black tracking-tight text-slate-950 dark:text-white">{route.routeCode}</p>
          <p className="text-[11px] text-slate-500 dark:text-slate-400">{route.hub} · {route.territory}</p>
        </div>
        <span className="rounded-full border border-slate-200/70 bg-white px-2.5 py-1 text-[9px] font-semibold uppercase tracking-[0.18em] text-slate-500 dark:border-white/10 dark:bg-white/[0.05] dark:text-slate-300">
          {route.status}
        </span>
      </div>
      <div className="mt-3 grid grid-cols-3 gap-2 text-[10px] text-slate-400">
        <span>{route.completedStops}/{route.plannedStops} stops</span>
        <span>{route.distanceKm.toFixed(1)} km</span>
        <span>{route.driverName}</span>
      </div>
      <p className="mt-3 text-[11px] leading-relaxed text-slate-500 dark:text-slate-400">
        {route.nextStop
          ? `Next operational handoff is ${route.nextStop}.`
          : 'Planner view is ready for next-stop progression and route recovery.'}
      </p>
      <div className={`mt-4 grid gap-2 ${canUpdate ? 'grid-cols-3' : 'grid-cols-1'}`}>
        <button
          type="button"
          onClick={onInspect}
          className="inline-flex w-full items-center justify-center gap-2 rounded-2xl border border-slate-200/70 bg-white px-4 py-3 text-[12px] font-bold text-slate-700 transition hover:border-sky-300 hover:text-sky-700 dark:border-white/10 dark:bg-white/[0.05] dark:text-white"
        >
          Inspect stops
        </button>
        {canUpdate && !terminalRoute(route.status) && <button type="button" onClick={onEdit} disabled={saving} className="inline-flex items-center justify-center gap-2 rounded-2xl border border-slate-200 bg-white px-3 py-3 text-[11px] font-bold text-slate-700 disabled:opacity-50 dark:border-white/10 dark:bg-white/[0.05] dark:text-white"><Pencil className="h-3.5 w-3.5" /> Edit</button>}
        {canUpdate && <button
          type="button"
          onClick={onAdvance}
          disabled={saving || !canProgress}
          className="inline-flex w-full items-center justify-center gap-2 rounded-2xl bg-gradient-to-r from-sky-600 via-cyan-500 to-teal-400 px-4 py-3 text-[12px] font-bold text-white shadow-[0_14px_30px_rgba(47,107,255,0.26)] transition hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-60"
        >
          {saving ? 'Advancing...' : 'Advance route'}
          <ArrowRight className="h-4 w-4" />
        </button>}
      </div>
    </div>
  );
}

function ActionStopCard({ stop, onConfirm, onAttempt, onReschedule, saving, canUpdate, canDeliver }: { stop: LogisticsStop; onConfirm: () => void; onAttempt: () => void; onReschedule: () => void; saving: boolean; canUpdate: boolean; canDeliver: boolean }) {
  const terminal = terminalStop(stop.status);
  return (
    <div className="rounded-[24px] border border-slate-200/70 bg-[linear-gradient(180deg,rgba(255,255,255,0.92),rgba(245,248,255,0.78))] p-4 shadow-[0_10px_24px_rgba(37,99,235,0.05)] dark:border-white/10 dark:bg-white/[0.04]">
      <div className="flex items-start justify-between gap-3">
        <div>
          <p className="text-[12px] font-black tracking-tight text-slate-950 dark:text-white">{stop.customerName}</p>
          <p className="text-[11px] text-slate-500 dark:text-slate-400">{stop.addressLine}</p>
        </div>
        <span className="rounded-full border border-slate-200/70 bg-white px-2.5 py-1 text-[9px] font-semibold uppercase tracking-[0.18em] text-slate-500 dark:border-white/10 dark:bg-white/[0.05] dark:text-slate-300">
          {stop.status}
        </span>
      </div>
      <div className="mt-3 flex flex-wrap gap-2 text-[10px] font-semibold uppercase tracking-[0.18em] text-slate-400">
        <span>{stop.routeCode}</span>
        <span>•</span>
        <span>{stop.timeWindow}</span>
        <span>•</span>
        <span>{stop.proofStatus}</span>
      </div>
      <p className="mt-3 text-[11px] leading-relaxed text-slate-500 dark:text-slate-400">
        {stop.status === 'Attempted' || stop.status === 'Rescheduled'
          ? 'This stop has an open exception — record an attempt, deliver or reschedule.'
          : 'Use proof, attempt, or reschedule actions to keep customer visibility current.'}
      </p>
      {!terminal && (canUpdate || canDeliver) && <div className={`mt-4 grid gap-2 ${canUpdate && canDeliver ? 'grid-cols-3' : canUpdate ? 'grid-cols-2' : 'grid-cols-1'}`}>
        {canUpdate && <button
          type="button"
          onClick={onAttempt}
          disabled={saving}
          className="inline-flex w-full items-center justify-center rounded-2xl border border-amber-200/70 bg-amber-50 px-3 py-3 text-[11px] font-bold text-amber-700 transition hover:border-amber-300 disabled:opacity-60"
        >
          Attempt
        </button>}
        {canUpdate && <button
          type="button"
          onClick={onReschedule}
          disabled={saving}
          className="inline-flex w-full items-center justify-center rounded-2xl border border-slate-200/70 bg-white px-3 py-3 text-[11px] font-bold text-slate-700 transition hover:border-sky-300 disabled:opacity-60 dark:border-white/10 dark:bg-white/[0.05] dark:text-white"
        >
          Reschedule
        </button>}
        {canDeliver && <button
          type="button"
          onClick={onConfirm}
          disabled={saving}
          className="inline-flex w-full items-center justify-center gap-2 rounded-2xl bg-gradient-to-r from-emerald-600 via-teal-500 to-cyan-400 px-3 py-3 text-[11px] font-bold text-white shadow-[0_14px_30px_rgba(47,107,255,0.22)] transition hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-60"
        >
          {saving ? 'Saving...' : 'Deliver'}
        </button>}
      </div>}
    </div>
  );
}

function DarkField({ label, value, onChange, type = 'text', min, max, step, required = false, disabled = false }: { label: string; value: string; onChange: (value: string) => void; type?: string; min?: string; max?: string; step?: string; required?: boolean; disabled?: boolean }) {
  return (
    <label className="block text-[11px] font-semibold text-white/70">
      {label}{required ? ' *' : ''}
      <input type={type} min={min} max={max} step={step} required={required} disabled={disabled} value={value} onChange={(event) => onChange(event.target.value)} className="mt-1 w-full rounded-2xl border border-white/10 bg-white/[0.05] px-3 py-2.5 text-sm text-white outline-none disabled:cursor-not-allowed disabled:opacity-55" />
    </label>
  );
}

function ReadOnlyMessage() {
  return <p className="rounded-2xl border border-white/10 bg-white/[0.04] p-3 text-xs leading-relaxed text-white/65">This is a read-only view. Ask an administrator for the required dispatch create or update permission to change records.</p>;
}
