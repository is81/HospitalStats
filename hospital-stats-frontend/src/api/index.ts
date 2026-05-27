import axios from 'axios';
import router from '../router';

const api = axios.create({
  baseURL: '/api',
  timeout: 30000,
});

api.interceptors.request.use((config) => {
  const token = localStorage.getItem('token');
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

api.interceptors.response.use(
  (res) => res,
  (err) => {
    if (err.response?.status === 401 && !router.currentRoute.value.meta.noAuth) {
      localStorage.removeItem('token');
      localStorage.removeItem('displayName');
      router.push('/login');
    }
    return Promise.reject(err);
  }
);

export default api;
