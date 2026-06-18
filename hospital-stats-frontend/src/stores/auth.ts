import { defineStore } from 'pinia';
import { ref, computed } from 'vue';
import { authApi } from '../api/auth';
import router from '../router';

function safeParseJsonArray<T>(key: string, fallback: T): T {
  try {
    return JSON.parse(localStorage.getItem(key) || '[]');
  } catch {
    return fallback;
  }
}

export const useAuthStore = defineStore('auth', () => {
  const token = ref(localStorage.getItem('token') || '');
  const displayName = ref(localStorage.getItem('displayName') || '');
  const roles = ref<string[]>(safeParseJsonArray('roles', []));
  const menuIds = ref<number[]>(safeParseJsonArray('menuIds', []));
  const deptName = ref(localStorage.getItem('deptName') || '');
  const dashboardAccess = ref(localStorage.getItem('dashboardAccess') === 'true');

  const isLoggedIn = computed(() => !!token.value);
  const isAdmin = computed(() => roles.value.includes('admin'));

  async function login(username: string, password: string) {
    const res = await authApi.login(username, password);
    token.value = res.data.token;
    displayName.value = res.data.displayName;
    roles.value = res.data.roles || [];
    menuIds.value = res.data.menuIds || [];
    deptName.value = res.data.deptName || '';
    dashboardAccess.value = res.data.dashboardAccess === true;
    localStorage.setItem('token', res.data.token);
    localStorage.setItem('displayName', res.data.displayName);
    localStorage.setItem('roles', JSON.stringify(roles.value));
    localStorage.setItem('menuIds', JSON.stringify(menuIds.value));
    localStorage.setItem('deptName', deptName.value);
    localStorage.setItem('dashboardAccess', dashboardAccess.value ? 'true' : '');
    router.push('/');
  }

  function logout() {
    token.value = '';
    displayName.value = '';
    roles.value = [];
    menuIds.value = [];
    deptName.value = '';
    dashboardAccess.value = false;
    localStorage.removeItem('token');
    localStorage.removeItem('displayName');
    localStorage.removeItem('roles');
    localStorage.removeItem('menuIds');
    localStorage.removeItem('deptName');
    localStorage.removeItem('dashboardAccess');
    router.push('/login');
  }

  return { token, displayName, roles, menuIds, deptName, dashboardAccess, isLoggedIn, isAdmin, login, logout };
});
