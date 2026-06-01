import api from './index';

export interface MenuItem {
  id: number;
  parentId: number | null;
  name: string;
  icon: string | null;
  sortOrder: number;
  queryConfigId: number | null;
  queryConfigName: string | null;
  isEnabled: boolean;
  children: MenuItem[];
}

export interface MenuSave {
  parentId: number | null;
  name: string;
  icon?: string;
  sortOrder: number;
  queryConfigId?: number | null;
  isEnabled: boolean;
}

export interface QueryConfigItem {
  id: number;
  name: string;
  mainTableId: number;
  mainTableName: string | null;
  displayType: string;
  updatedAt: string;
  isEnabled: boolean;
}

export interface QueryFieldInfo {
  id: number;
  metaColumnId: number;
  columnName: string | null;
  columnAlias: string | null;
  tableName: string | null;
  alias: string | null;
  sortOrder: number;
  aggregateFunc: string | null;
}

export interface QueryFilterInfo {
  id: number;
  metaColumnId: number;
  columnName: string | null;
  columnAlias: string | null;
  operator: string;
  defaultValue: string | null;
  isRequired: boolean;
  controlType: string;
  label: string | null;
  sortOrder: number;
  isContextFilter: boolean;
  contextKey: string | null;
}

export interface QueryJoinInfo {
  id: number;
  joinTableId: number;
  joinTableName: string | null;
  joinType: string;
  leftMetaColumnId: number;
  leftColumnName: string | null;
  rightMetaColumnId: number;
  rightColumnName: string | null;
  sortOrder: number;
  leftDateTrunc: boolean;
}

export interface QueryConfigDetail {
  id: number;
  name: string;
  mainTableId: number;
  mainTableName: string | null;
  displayType: string;
  aggregateType: string | null;
  aggregateColumn: string | null;
  groupByColumn: string | null;
  sortColumn: string | null;
  sortDirection: string | null;
  pageSize: number | null;
  isEnabled: boolean;
  updatedAt: string;
  rawSql: string | null;
  originalSql: string | null;
  fields: QueryFieldInfo[];
  filters: QueryFilterInfo[];
  joins: QueryJoinInfo[];
}

export interface QueryConfigSave {
  name: string;
  mainTableId: number;
  displayType: string;
  aggregateType?: string;
  aggregateColumn?: string;
  groupByColumn?: string;
  sortColumn?: string;
  sortDirection?: string;
  pageSize?: number;
  isEnabled: boolean;
  rawSql?: string | null;
  originalSql?: string | null;
  fields: { metaColumnId: number; alias?: string; sortOrder: number; aggregateFunc?: string }[];
  filters: { metaColumnId: number; operator: string; defaultValue?: string; isRequired: boolean; controlType: string; label?: string; sortOrder: number; isContextFilter?: boolean; contextKey?: string | null }[];
  joins: { joinTableId: number; joinType: string; leftMetaColumnId: number; rightMetaColumnId: number; sortOrder: number; leftDateTrunc: boolean }[];
}

// ===== SQL Import =====

export interface SqlParseRequest {
  sql: string;
}

export interface SqlColumnMatch {
  metaColumnId: number | null;
  alias: string | null;
  aggregateFunc: string | null;
  expression: string | null;
  matched: boolean;
}

export interface SqlFilterMatch {
  metaColumnId: number | null;
  operator: string | null;
  defaultValue: string | null;
  label: string | null;
  matched: boolean;
}

export interface SqlJoinMatch {
  joinTableId: number | null;
  joinTableName: string | null;
  joinType: string;
  leftMetaColumnId: number | null;
  leftColumnName: string | null;
  rightMetaColumnId: number | null;
  rightColumnName: string | null;
  matched: boolean;
}

export interface SqlParseResponse {
  mainTableId: number | null;
  mainTableName: string | null;
  columns: SqlColumnMatch[];
  filters: SqlFilterMatch[];
  joins: SqlJoinMatch[];
  sortColumn: string | null;
  sortDirection: string | null;
  groupByColumn: string | null;
  unmatchedColumns: string[];
  rawSql: string | null;
  originalSql: string | null;
  unsupportedPattern: string | null;
}

export const queryApi = {
  // menus
  getMenus() {
    return api.get<MenuItem[]>('/query/menus');
  },
  createMenu(data: MenuSave) {
    return api.post<MenuItem>('/query/menus', data);
  },
  updateMenu(id: number, data: MenuSave) {
    return api.put(`/query/menus/${id}`, data);
  },
  deleteMenu(id: number) {
    return api.delete(`/query/menus/${id}`);
  },
  // configs
  getConfigs(dsId?: number) {
    return api.get<QueryConfigItem[]>('/query/configs', { params: { dsId } });
  },
  getConfig(id: number) {
    return api.get<QueryConfigDetail>(`/query/configs/${id}`);
  },
  createConfig(data: QueryConfigSave) {
    return api.post<QueryConfigDetail>('/query/configs', data);
  },
  updateConfig(id: number, data: QueryConfigSave) {
    return api.put<QueryConfigDetail>(`/query/configs/${id}`, data);
  },
  deleteConfig(id: number) {
    return api.delete(`/query/configs/${id}`);
  },
  // sql import
  parseSql(data: SqlParseRequest) {
    return api.post<SqlParseResponse>('/query/configs/parse-sql', data);
  },
};
