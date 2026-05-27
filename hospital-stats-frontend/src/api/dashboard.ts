import api from './index';

export interface DashboardCardData {
  id: number;
  title: string;
  queryConfigId: number;
  queryConfigName: string | null;
  displayType: string;
  icon: string | null;
  color: string | null;
  unit: string | null;
  sortOrder: number;
  width: number;
  isEnabled: boolean;
  data?: { value?: string; rows?: unknown[]; columns?: string[]; total?: number; error?: string };
}

export const dashboardApi = {
  getCards() {
    return api.get<DashboardCardData[]>('/dashboard/cards');
  },
  getDashboard() {
    return api.get<DashboardCardData[]>('/dashboard');
  },
  createCard(data: {
    title: string; queryConfigId: number; displayType: string;
    icon?: string; color?: string; unit?: string; sortOrder: number; width: number; isEnabled: boolean;
  }) {
    return api.post<DashboardCardData>('/dashboard/cards', data);
  },
  updateCard(id: number, data: {
    title: string; queryConfigId: number; displayType: string;
    icon?: string; color?: string; unit?: string; sortOrder: number; width: number; isEnabled: boolean;
  }) {
    return api.put(`/dashboard/cards/${id}`, data);
  },
  deleteCard(id: number) {
    return api.delete(`/dashboard/cards/${id}`);
  },
  updateOrder(cardIds: number[]) {
    return api.put('/dashboard/cards/order', { cardIds });
  },
};
