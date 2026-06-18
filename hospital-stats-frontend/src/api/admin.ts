import api from './index';

export interface UserInfo {
  id: number;
  username: string;
  displayName: string | null;
  deptName: string | null;
  isEnabled: boolean;
  roles: string[];
  roleIds: number[];
  createdAt: string;
}

export interface RoleInfo {
  id: number;
  name: string;
  description: string | null;
  menuIds: number[];
  dashboardAccess: boolean;
  createdAt: string;
}

export const adminApi = {
  // users
  getUsers() {
    return api.get<UserInfo[]>('/admin/users');
  },
  createUser(data: { username: string; password: string; displayName?: string; deptName?: string; roleIds?: number[] }) {
    return api.post<UserInfo>('/admin/users', data);
  },
  updateUser(id: number, data: { displayName?: string; deptName?: string; password?: string; isEnabled: boolean; roleIds?: number[] }) {
    return api.put(`/admin/users/${id}`, data);
  },
  deleteUser(id: number) {
    return api.delete(`/admin/users/${id}`);
  },
  // roles
  getRoles() {
    return api.get<RoleInfo[]>('/admin/roles');
  },
  createRole(data: { name: string; description?: string; menuIds: number[]; dashboardAccess: boolean }) {
    return api.post<RoleInfo>('/admin/roles', data);
  },
  updateRole(id: number, data: { name: string; description?: string; menuIds: number[]; dashboardAccess: boolean }) {
    return api.put(`/admin/roles/${id}`, data);
  },
  deleteRole(id: number) {
    return api.delete(`/admin/roles/${id}`);
  },
  getDeptOptions() {
    return api.get<string[]>('/admin/dept-options');
  },
};
