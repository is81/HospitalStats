import api from './index';

export interface DataSourceItem {
  id: number;
  name: string;
  dbType: string;
  schema: string | null;
  charSetOverride: string | null;
  charSetInfo: string | null;
  isEnabled: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface DataSourceForm {
  name: string;
  dbType: string;
  connectionString: string;
  schema?: string;
  charSetOverride?: string;
}

export interface TestConnectionResult {
  success: boolean;
  message: string;
  tableCount: number | null;
  dbVersion: string | null;
  charSet: string | null;
}

export const dataSourcesApi = {
  getAll() {
    return api.get<DataSourceItem[]>('/datasources');
  },
  getById(id: number) {
    return api.get<DataSourceItem>(`/datasources/${id}`);
  },
  create(data: DataSourceForm) {
    return api.post<DataSourceItem>('/datasources', data);
  },
  update(id: number, data: DataSourceForm & { isEnabled: boolean }) {
    return api.put<DataSourceItem>(`/datasources/${id}`, data);
  },
  delete(id: number) {
    return api.delete(`/datasources/${id}`);
  },
  testConnection(id: number) {
    return api.post<TestConnectionResult>(`/datasources/${id}/test`);
  },
  testConnectionString(connectionString: string) {
    return api.post<TestConnectionResult>('/datasources/test', { connectionString });
  },
};
