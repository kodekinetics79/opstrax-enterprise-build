import axios from 'axios';

export const BASE_URL = import.meta.env.VITE_API_URL ?? 'http://localhost:5117';

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
      const { data } = await axios.post(`${BASE_URL}/api/auth/refresh`, { refreshToken });
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
