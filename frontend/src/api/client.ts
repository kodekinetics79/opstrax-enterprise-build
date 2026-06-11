import axios from 'axios';

// In the browser, use relative URLs so Next.js proxy handles CORS.
// On the server (SSR), we need the absolute URL since there's no proxy.
function resolveBaseUrl(): string {
  if (typeof window !== 'undefined') return '';
  const raw = process.env.NEXT_PUBLIC_API_BASE_URL;
  if (!raw) return 'http://localhost:5117';
  if (raw.startsWith('http://') || raw.startsWith('https://')) return raw;
  return `https://${raw}`;
}
export const BASE_URL = resolveBaseUrl();

const client = axios.create({ baseURL: BASE_URL });

client.interceptors.request.use((config) => {
  const token = localStorage.getItem('zayra_access_token');
  if (token) config.headers.Authorization = `Bearer ${token}`;
  return config;
});

let isRefreshing = false;
let pending: Array<(token: string) => void> = [];

client.interceptors.response.use(
  (res) => res,
  async (err) => {
    const original = err.config;

    // 402 — subscription expired or inactive; redirect to tenant admin with alert
    if (err.response?.status === 402) {
      // Only redirect for non-tenant-admin URLs to avoid redirect loops
      const url: string = original?.url ?? '';
      if (!url.includes('/api/tenant-admin/usage') && !url.includes('/api/tenant-admin/subscription')) {
        window.location.href = '/tenant-admin?alert=subscription';
      }
      return Promise.reject(err);
    }

    if (err.response?.status !== 401 || original._retry) {
      return Promise.reject(err);
    }
    original._retry = true;

    if (isRefreshing) {
      return new Promise((resolve, reject) => {
        pending.push((token) => {
          original.headers.Authorization = `Bearer ${token}`;
          resolve(client(original));
        });
      });
    }

    isRefreshing = true;
    try {
      const refreshToken = localStorage.getItem('zayra_refresh_token');
      if (!refreshToken) throw new Error('No refresh token');
      const { data } = await axios.post(`${resolveBaseUrl()}/api/auth/refresh`, { refreshToken });
      localStorage.setItem('zayra_access_token', data.accessToken);
      localStorage.setItem('zayra_refresh_token', data.refreshToken);
      pending.forEach((cb) => cb(data.accessToken));
      pending = [];
      original.headers.Authorization = `Bearer ${data.accessToken}`;
      return client(original);
    } catch {
      localStorage.clear();
      window.location.href = '/login';
      return Promise.reject(err);
    } finally {
      isRefreshing = false;
    }
  }
);

export default client;
