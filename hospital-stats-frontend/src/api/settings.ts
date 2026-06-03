import api from './index';

export const settingsApi = {
  getAll() {
    return api.get<Record<string, string>>('/admin/settings');
  },
  update(settings: Record<string, string>) {
    return api.put('/admin/settings', settings);
  },
};
