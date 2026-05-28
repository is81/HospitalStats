import api from './index';

export const authApi = {
  login(username: string, password: string) {
    return api.post('/auth/login', { username, password });
  },
  changePassword(oldPassword: string, newPassword: string) {
    return api.post('/auth/change-password', { oldPassword, newPassword });
  },
};
