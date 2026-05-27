import api from './index';

export interface BizDomain {
  id: number;
  name: string;
  description: string | null;
  sortOrder: number;
  tableCount: number;
}

export interface MetaTable {
  id: number;
  dataSourceId: number;
  bizDomainId: number | null;
  bizDomainName: string | null;
  tableName: string;
  schemaName: string | null;
  alias: string | null;
  description: string | null;
  isEnabled: boolean;
  isView: boolean;
  columnCount: number;
  updatedAt: string;
}

export interface MetaColumn {
  id: number;
  metaTableId: number;
  columnName: string;
  dataType: string | null;
  dataLength: number | null;
  dataPrecision: number | null;
  dataScale: number | null;
  nullable: boolean;
  alias: string | null;
  comment: string | null;
  isQueryField: boolean;
  isFilterField: boolean;
  isDisplayField: boolean;
  sortOrder: number | null;
}

export interface ScanResult {
  schema: string;
  tablesFound: number;
  viewsFound: number;
  created: number;
  updated: number;
}

export const metaApi = {
  // domains
  getDomains(dataSourceId?: number) {
    return api.get<BizDomain[]>('/meta/domains', dataSourceId != null ? { params: { dataSourceId } } : undefined);
  },
  createDomain(data: { name: string; description?: string; sortOrder: number }) {
    return api.post<BizDomain>('/meta/domains', data);
  },
  updateDomain(id: number, data: { name: string; description?: string; sortOrder: number }) {
    return api.put(`/meta/domains/${id}`, data);
  },
  deleteDomain(id: number) {
    return api.delete(`/meta/domains/${id}`);
  },
  // tables
  getTables(dsId: number, domainId?: number, search?: string) {
    return api.get<MetaTable[]>('/meta/datasources/' + dsId + '/tables', {
      params: { domainId, search },
    });
  },
  updateTable(id: number, data: { alias?: string; description?: string; bizDomainId?: number | null; isEnabled: boolean }) {
    return api.put(`/meta/tables/${id}`, data);
  },
  // columns
  getColumns(tableId: number) {
    return api.get<MetaColumn[]>(`/meta/tables/${tableId}/columns`);
  },
  updateColumn(id: number, data: { alias?: string; isQueryField: boolean; isFilterField: boolean; isDisplayField: boolean }) {
    return api.put(`/meta/columns/${id}`, data);
  },
  // scan
  scan(dsId: number, schema?: string) {
    return api.post<ScanResult>('/meta/datasources/' + dsId + '/scan', null, {
      params: { schema },
    });
  },
};
