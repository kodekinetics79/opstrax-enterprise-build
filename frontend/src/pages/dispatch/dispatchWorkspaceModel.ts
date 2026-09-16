export type DispatchMode = 'dispatch' | 'orders' | 'routes' | 'delivery';

export const MODULES: Record<DispatchMode, {
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

export const MODE_ORDER: DispatchMode[] = ['dispatch', 'orders', 'routes', 'delivery'];
export const PAGE_SIZE = 12;
