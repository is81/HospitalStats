import api from './index';

export interface QueryResult {
  rows: Record<string, unknown>[];
  columns: string[];
  total: number;
  page: number;
  pageSize: number;
  elapsedMs: number;
}

export const executeApi = {
  execute(configId: number, filters: Record<string, string>, page?: number, pageSize?: number) {
    return api.post<QueryResult>(`/query-execute/${configId}`, {
      filters,
      page,
      pageSize,
    });
  },
  getFilterOptions(configId: number, filterId: number) {
    return api.get<string[]>(`/query-execute/${configId}/filter-options/${filterId}`);
  },
  getExportUrl(configId: number) {
    return `/api/query-execute/${configId}/export`;
  },
  async exportExcel(configId: number, filters: Record<string, string>) {
    const res = await api.post(`/query-execute/${configId}/export`, { filters }, {
      responseType: 'blob',
    });
    const url = window.URL.createObjectURL(new Blob([res.data]));
    const link = document.createElement('a');
    link.href = url;
    link.download = `query_result_${new Date().toISOString().slice(0, 10)}.xlsx`;
    link.click();
    window.URL.revokeObjectURL(url);
  },
};
