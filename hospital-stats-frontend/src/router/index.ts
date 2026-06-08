import { createRouter, createWebHistory, type RouteRecordRaw } from 'vue-router';

const routes: RouteRecordRaw[] = [
  {
    path: '/login',
    name: 'Login',
    component: () => import('../views/Login.vue'),
    meta: { noAuth: true },
  },
  {
    path: '/',
    component: () => import('../layout/MainLayout.vue'),
    redirect: '/query/preview',
    children: [
      {
        path: 'datasources',
        name: 'DataSources',
        component: () => import('../views/datasources/DataSourceList.vue'),
        meta: { title: '数据源管理', admin: true },
      },
      {
        path: 'meta',
        name: 'MetaTables',
        component: () => import('../views/meta/TableList.vue'),
        meta: { title: '元数据管理', admin: true },
      },
      {
        path: 'meta/tables/:tableId',
        name: 'MetaTableDetail',
        component: () => import('../views/meta/TableDetail.vue'),
        meta: { title: '表字段设置', admin: true },
      },
      {
        path: 'query/configs',
        name: 'QueryConfigs',
        component: () => import('../views/query/ConfigList.vue'),
        meta: { title: '查询配置', admin: true },
      },
      {
        path: 'query/configs/new',
        name: 'QueryConfigCreate',
        component: () => import('../views/query/ConfigEdit.vue'),
        meta: { title: '新增查询配置', admin: true },
      },
      {
        path: 'query/configs/:id',
        name: 'QueryConfigEdit',
        component: () => import('../views/query/ConfigEdit.vue'),
        meta: { title: '编辑查询配置', admin: true },
      },
      {
        path: 'query/menus',
        name: 'MenuManage',
        component: () => import('../views/query/MenuManage.vue'),
        meta: { title: '菜单管理', admin: true },
      },
      {
        path: 'query/view/:configId',
        name: 'QueryView',
        component: () => import('../views/query/QueryView.vue'),
        meta: { title: '数据查询' },
      },
      {
        path: 'query/preview',
        name: 'MenuPreview',
        component: () => import('../views/query/MenuPreview.vue'),
        meta: { title: '数据查询' },
      },
      {
        path: 'dashboard',
        name: 'DashboardHome',
        component: () => import('../views/dashboard/DashboardHome.vue'),
        meta: { title: '仪表盘' },
      },
      {
        path: 'dashboard/config',
        name: 'DashboardConfig',
        component: () => import('../views/dashboard/DashboardConfig.vue'),
        meta: { title: '仪表盘配置', admin: true },
      },
      {
        path: 'admin/users',
        name: 'AdminUsers',
        component: () => import('../views/admin/Users.vue'),
        meta: { title: '用户管理', admin: true },
      },
      {
        path: 'admin/roles',
        name: 'AdminRoles',
        component: () => import('../views/admin/Roles.vue'),
        meta: { title: '角色管理', admin: true },
      },
      {
        path: 'admin/settings',
        name: 'AdminSettings',
        component: () => import('../views/admin/Settings.vue'),
        meta: { title: '配置管理', admin: true },
      },
      {
        path: 'admin/history',
        name: 'AdminHistory',
        component: () => import('../views/admin/QueryHistory.vue'),
        meta: { title: '查询历史', admin: true },
      },
    ],
  },
];

const router = createRouter({
  history: createWebHistory(),
  routes,
});

function safeParseJsonArray<T>(key: string, fallback: T): T {
  try {
    return JSON.parse(localStorage.getItem(key) || '[]');
  } catch {
    return fallback;
  }
}

router.beforeEach((to, _from) => {
  if (to.meta.noAuth) return true;
  const token = localStorage.getItem('token');
  if (!token) return { name: 'Login' };

  if (to.meta.admin) {
    const roles: string[] = safeParseJsonArray('roles', []);
    if (!roles.includes('admin')) return { name: 'MenuPreview' };
  }

  return true;
});

export default router;
