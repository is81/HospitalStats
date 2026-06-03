<script setup lang="ts">
import { ref, reactive, onMounted } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { ElMessage } from 'element-plus';
import { queryApi, type QueryConfigSave, type SqlParseResponse } from '../../api/query';
import { metaApi, type MetaTable, type MetaColumn } from '../../api/meta';
import { dataSourcesApi, type DataSourceItem } from '../../api/datasources';

const route = useRoute();
const router = useRouter();
const isEdit = route.name === 'QueryConfigEdit';
const configId = isEdit ? (route.params.id as string) : null;

// data sources
const dataSources = ref<DataSourceItem[]>([]);
const selectedDsIds = ref<number[]>([]);

// tables for main table selection
const tables = ref<MetaTable[]>([]);
// all tables keyed by dataSourceId (for multi-source JOIN)
const tablesByDs = ref<Record<number, MetaTable[]>>({});
// per-join-row data source selection (keyed by join row index)
const joinDsId = ref<Record<number, number>>({});
const tableColumns = ref<Record<number, MetaColumn[]>>({});
const unionTableIds = ref<number[]>([]);

// form
const activeStep = ref(0);
const saving = ref(false);

const form = reactive<QueryConfigSave>({
  name: '',
  mainTableId: 0,
  displayType: 'table',
  aggregateType: '',
  aggregateColumn: '',
  groupByColumn: '',
  sortColumn: '',
  sortDirection: 'ASC',
  pageSize: 50,
  isEnabled: true,
  rawSql: null,
  originalSql: null,
  fields: [],
  filters: [],
  joins: [],
});

// SQL import mode
const inputMode = ref<'manual' | 'sql'>(route.query.mode === 'sql' ? 'sql' : 'manual');
const sqlText = ref('');
const sqlParsing = ref(false);
const sqlResult = ref<SqlParseResponse | null>(null);

// operator options
const operators = [
  { value: 'EQ', label: '=' },
  { value: 'NE', label: '≠' },
  { value: 'GT', label: '＞' },
  { value: 'GTE', label: '≥' },
  { value: 'LT', label: '＜' },
  { value: 'LTE', label: '≤' },
  { value: 'LIKE', label: '包含' },
  { value: 'NOT LIKE', label: '排除' },
  { value: 'IN', label: '属于' },
  { value: 'NOT IN', label: '不属' },
  { value: 'BETWEEN', label: '范围' },
  { value: 'NOT BETWEEN', label: '非范围' },
];

const controlTypes = [
  { value: 'input', label: '文本框' },
  { value: 'date', label: '日期选择' },
  { value: 'select', label: '下拉框' },
];

const contextKeys = [
  { value: 'DeptName', label: '当前科室' },
  { value: 'UserId', label: '当前用户' },
];

async function doParseSql() {
  if (!sqlText.value.trim()) {
    ElMessage.warning('请输入 SQL 语句');
    return;
  }
  sqlParsing.value = true;
  sqlResult.value = null;
  try {
    const res = await queryApi.parseSql({ sql: sqlText.value });
    sqlResult.value = res.data;
  } catch (e: any) {
    ElMessage.error(e.response?.data?.message || 'SQL 解析失败');
  } finally {
    sqlParsing.value = false;
  }
}

async function applySqlResult() {
  if (!sqlResult.value) return;

  const r = sqlResult.value;

  // Save raw SQL for direct execution, original for re-editing
  form.rawSql = r.rawSql;
  form.originalSql = r.originalSql || sqlText.value;

  // populate form basic info
  if (r.mainTableId) {
    form.mainTableId = r.mainTableId;
    // load tables and columns for the main table
    // first find which data source this table belongs to
    if (tables.value.length === 0) {
      try {
        for (const ds of dataSources.value) {
          const tRes = await metaApi.getTables(ds.id);
          const enabled = tRes.data.filter(t => t.isEnabled);
          tablesByDs.value[ds.id] = enabled;
          for (const t of enabled) {
            if (!tables.value.find(ex => ex.id === t.id)) {
              tables.value.push(t);
            }
          }
        }
      } catch { /* ignore */ }
    }
    const mainTable = tables.value.find(t => t.id === r.mainTableId);
    if (mainTable) {
      selectedDsIds.value = [mainTable.dataSourceId];
    }
    await loadColumns(r.mainTableId);
  }
  form.sortColumn = r.sortColumn ?? '';
  form.sortDirection = r.sortDirection ?? 'ASC';
  form.groupByColumn = r.groupByColumn ?? '';

  // populate fields
  form.fields = r.columns
    .filter(c => c.matched && c.metaColumnId)
    .map((c, i) => ({
      metaColumnId: c.metaColumnId!,
      alias: c.alias ?? undefined,
      sortOrder: i,
      aggregateFunc: c.aggregateFunc ?? undefined,
    }));

  // populate filters
  form.filters = r.filters
    .filter(f => f.matched && f.metaColumnId)
    .map((f, i) => ({
      metaColumnId: f.metaColumnId!,
      operator: f.operator ?? 'EQ',
      defaultValue: f.defaultValue ?? undefined,
      isRequired: false,
      controlType: 'input',
      label: f.label ?? undefined,
      sortOrder: i,
    }));

  // populate joins (from detected WHERE join conditions)
  if (r.joins && r.joins.length > 0) {
    form.joins = r.joins
      .filter(j => j.matched && j.joinTableId && j.leftMetaColumnId && j.rightMetaColumnId)
      .map((j, i) => ({
        joinTableId: j.joinTableId!,
        joinType: j.joinType || 'INNER',
        leftMetaColumnId: j.leftMetaColumnId!,
        rightMetaColumnId: j.rightMetaColumnId!,
        sortOrder: i,
        leftDateTrunc: false,
      }));
    // load columns for joined tables + set joinDsId
    const dsIds = new Set<number>(selectedDsIds.value);
    for (let i = 0; i < form.joins.length; i++) {
      const jt = tables.value.find(t => t.id === form.joins[i].joinTableId);
      if (jt) {
        joinDsId.value[i] = jt.dataSourceId;
        dsIds.add(jt.dataSourceId);
      }
    }
    for (const j of r.joins) {
      if (j.joinTableId) {
        await loadColumns(j.joinTableId);
      }
    }
    selectedDsIds.value = [...dsIds];
  }

  // UNION: also load columns from tables in other branches for filter configuration
  if (r.unsupportedPattern === 'UNION') {
    for (const ds of dataSources.value) {
      if (!tablesByDs.value[ds.id]) {
        const tRes = await metaApi.getTables(ds.id);
        tablesByDs.value[ds.id] = tRes.data.filter(t => t.isEnabled);
      }
      for (const t of tablesByDs.value[ds.id]) {
        if (!tables.value.find(ex => ex.id === t.id)) tables.value.push(t);
      }
    }
    unionTableIds.value = [];
    const unionParts = sqlText.value.split(/\bUNION\s+(?:ALL\s+)?(?=SELECT\b)/i);
    for (let i = 1; i < unionParts.length; i++) {
      const fromMatch = unionParts[i].match(/\bFROM\s+(?:(\w+)\.)?(\w+)(?:\s+(\w+))?/i);
      if (fromMatch) {
        const tableName = fromMatch[2].toUpperCase();
        const tbl = tables.value.find(t => t.tableName.toUpperCase() === tableName);
        if (tbl && !unionTableIds.value.includes(tbl.id)) {
          unionTableIds.value.push(tbl.id);
          await loadColumns(tbl.id);
        }
      }
    }
  }

  // Now that all table columns are loaded, auto-fill field aliases from MetaColumn
  syncFieldAliases();

  // switch to manual mode and move to step 1
  inputMode.value = 'manual';
  activeStep.value = 1;
  ElMessage.success('已应用 SQL 解析结果，请继续调整配置');
}

async function loadDataSources() {
  const res = await dataSourcesApi.getAll();
  dataSources.value = res.data;
}

function getDsName(dsId: number): string {
  return dataSources.value.find(ds => ds.id === dsId)?.name ?? `DS#${dsId}`;
}

async function onDsChange(dsIds: number[]) {
  selectedDsIds.value = dsIds;
  // Load tables for newly selected sources, remove deselected ones
  const prevIds = new Set(Object.keys(tablesByDs.value).map(Number));
  const curIds = new Set(dsIds);
  for (const id of dsIds) {
    if (!prevIds.has(id)) {
      const res = await metaApi.getTables(id);
      tablesByDs.value[id] = res.data.filter(t => t.isEnabled);
    }
  }
  for (const id of prevIds) {
    if (!curIds.has(id)) {
      delete tablesByDs.value[id];
    }
  }
  // Aggregate tables from all selected data sources
  tables.value = dsIds.flatMap(id => tablesByDs.value[id] || []);
  // If the currently selected main table is no longer available, clear it
  if (form.mainTableId && !tables.value.find(t => t.id === form.mainTableId)) {
    form.mainTableId = 0;
  }
}

async function onJoinDsChange(dsId: number, joinIndex: number) {
  joinDsId.value[joinIndex] = dsId;
  if (!tablesByDs.value[dsId]) {
    const res = await metaApi.getTables(dsId);
    tablesByDs.value[dsId] = res.data.filter(t => t.isEnabled);
  }
}

function getJoinTables(joinIndex: number): MetaTable[] {
  const dsId = joinDsId.value[joinIndex];
  return dsId ? (tablesByDs.value[dsId] || []) : [];
}

async function onMainTableChange(tableId: number) {
  await loadColumns(tableId);
  form.fields = [];
  form.filters = [];
  form.joins = [];
}

async function loadColumns(tableId: number) {
  if (!tableColumns.value[tableId]) {
    const res = await metaApi.getColumns(tableId);
    tableColumns.value[tableId] = res.data;
  }
}

async function loadExistingConfig() {
  const res = await queryApi.getConfig(Number(configId));
  const c = res.data;
  form.name = c.name;
  form.mainTableId = c.mainTableId;
  form.displayType = c.displayType;
  form.aggregateType = c.aggregateType ?? '';
  form.aggregateColumn = c.aggregateColumn ?? '';
  form.groupByColumn = c.groupByColumn ?? '';
  form.sortColumn = c.sortColumn ?? '';
  form.sortDirection = c.sortDirection ?? 'ASC';
  form.pageSize = c.pageSize ?? undefined;
  form.isEnabled = c.isEnabled;
  form.fields = c.fields.map(f => ({ metaColumnId: f.metaColumnId, alias: f.alias ?? undefined, sortOrder: f.sortOrder, aggregateFunc: f.aggregateFunc ?? undefined }));
  syncFieldAliases();
  form.filters = c.filters.map(f => ({ metaColumnId: f.metaColumnId, operator: f.operator, defaultValue: f.defaultValue ?? undefined, isRequired: f.isRequired, controlType: f.controlType, label: f.label ?? undefined, sortOrder: f.sortOrder, isContextFilter: f.isContextFilter, contextKey: f.contextKey }));
  form.joins = c.joins.map(j => ({ joinTableId: j.joinTableId, joinType: j.joinType, leftMetaColumnId: j.leftMetaColumnId, rightMetaColumnId: j.rightMetaColumnId, sortOrder: j.sortOrder, leftDateTrunc: j.leftDateTrunc }));

  // 恢复 rawSql 和 originalSql（SQL导入的配置）
  form.rawSql = c.rawSql ?? null;
  form.originalSql = c.originalSql ?? null;
  if (c.rawSql) {
    sqlText.value = c.originalSql || c.rawSql;
    inputMode.value = 'sql';
  }
}

// field management
function addField() {
  form.fields.push({ metaColumnId: 0, alias: '', sortOrder: form.fields.length, aggregateFunc: '' });
}

function removeField(idx: number) {
  form.fields.splice(idx, 1);
}

function moveUp(arr: { sortOrder: number }[], idx: number) {
  if (idx <= 0) return;
  [arr[idx - 1], arr[idx]] = [arr[idx], arr[idx - 1]];
  const tmp = arr[idx - 1].sortOrder;
  arr[idx - 1].sortOrder = arr[idx].sortOrder;
  arr[idx].sortOrder = tmp;
  // swap join dsIds too
  const tmpDs = joinDsId.value[idx - 1];
  joinDsId.value[idx - 1] = joinDsId.value[idx];
  joinDsId.value[idx] = tmpDs;
}

function moveDown(arr: { sortOrder: number }[], idx: number) {
  if (idx >= arr.length - 1) return;
  [arr[idx], arr[idx + 1]] = [arr[idx + 1], arr[idx]];
  const tmp = arr[idx].sortOrder;
  arr[idx].sortOrder = arr[idx + 1].sortOrder;
  arr[idx + 1].sortOrder = tmp;
  // swap join dsIds too
  const tmpDs = joinDsId.value[idx];
  joinDsId.value[idx] = joinDsId.value[idx + 1];
  joinDsId.value[idx + 1] = tmpDs;
}

// Auto-fill Field alias from MetaColumn alias when empty
function syncFieldAliases() {
  for (const field of form.fields) {
    if (field.alias || !field.metaColumnId) continue;
    const col = findMetaColumn(field.metaColumnId);
    if (col?.alias) field.alias = col.alias;
  }
}

function findMetaColumn(metaColumnId: number): MetaColumn | undefined {
  for (const cols of Object.values(tableColumns.value)) {
    const col = cols.find(c => c.id === metaColumnId);
    if (col) return col;
  }
  return undefined;
}

function onFieldColumnChange(field: { alias: string; metaColumnId: number }, metaColumnId: number) {
  // only auto-fill if alias is empty to preserve user customizations
  if (field.alias) return;
  const col = findMetaColumn(metaColumnId);
  if (col?.alias) {
    field.alias = col.alias;
  }
}

function getColumnOptions(forTableIds?: number[]) {
  const ids = forTableIds ?? [form.mainTableId, ...form.joins.map(j => j.joinTableId), ...unionTableIds.value];
  const options: { label: string; value: number; table: string }[] = [];
  for (const tableId of ids) {
    const cols = tableColumns.value[tableId] || [];
    const table = tables.value.find(t => t.id === tableId);
    const tableLabel = table?.alias || table?.tableName || '';
    for (const col of cols) {
      options.push({ label: `${tableLabel}.${col.columnName}`, value: col.id, table: tableLabel });
    }
  }
  return options;
}

function getLeftTableId(joinIndex: number): number {
  const colId = form.joins[joinIndex]?.leftMetaColumnId;
  if (!colId) return form.mainTableId;
  for (const [tableId, cols] of Object.entries(tableColumns.value)) {
    if (cols.some(c => c.id === colId)) return Number(tableId);
  }
  // Also check in tables that have been loaded with columns
  for (const j of form.joins.slice(0, joinIndex)) {
    const jCols = tableColumns.value[j.joinTableId] || [];
    if (jCols.some(c => c.id === colId)) return j.joinTableId;
  }
  return form.mainTableId;
}

// filter management
function addFilter() {
  form.filters.push({ metaColumnId: 0, operator: 'EQ', controlType: 'input', isRequired: false, sortOrder: form.filters.length, isContextFilter: false, contextKey: null });
}

function removeFilter(idx: number) {
  form.filters.splice(idx, 1);
}

// join management
function addJoin() {
  const idx = form.joins.length;
  form.joins.push({ joinTableId: 0, joinType: 'LEFT', leftMetaColumnId: 0, rightMetaColumnId: 0, sortOrder: idx, leftDateTrunc: false });
  joinDsId.value[idx] = selectedDsIds.value[0] ?? dataSources.value[0]?.id;
}

function removeJoin(idx: number) {
  form.joins.splice(idx, 1);
  const newMap: Record<number, number> = {};
  for (const [k, v] of Object.entries(joinDsId.value)) {
    const ki = Number(k);
    if (ki < idx) newMap[ki] = v;
    else if (ki > idx) newMap[ki - 1] = v;
  }
  joinDsId.value = newMap;
}

async function handleSave() {
  if (!form.name || !form.mainTableId) {
    ElMessage.warning('请填写名称和选择主表');
    return;
  }
  saving.value = true;
  try {
    if (isEdit) {
      await queryApi.updateConfig(Number(configId), form);
      ElMessage.success('已更新');
    } else {
      const res = await queryApi.createConfig(form);
      ElMessage.success('已创建');
      router.replace(`/query/configs/${res.data.id}`);
    }
  } catch (err: any) {
    const msg = err?.response?.data?.message || err?.response?.data?.title || err?.message || '保存失败';
    ElMessage.error(typeof msg === 'string' ? msg : '保存失败');
  } finally {
    saving.value = false;
  }
}

onMounted(async () => {
  await loadDataSources();
  if (isEdit) {
    // We need tables loaded first, load them lazily by reading config
    const res = await queryApi.getConfig(Number(configId));
    // For simplicity, load all tables from all data sources
    for (const ds of dataSources.value) {
      const tRes = await metaApi.getTables(ds.id);
      const enabled = tRes.data.filter(t => t.isEnabled);
      tablesByDs.value[ds.id] = enabled;
      for (const t of enabled) {
        tables.value.push(t);
      }
    }
    await loadColumns(res.data.mainTableId);
    for (const j of res.data.joins) {
      await loadColumns(j.joinTableId);
    }
    // UNION: load columns from non-primary branch tables for filter dropdowns
    if (res.data.rawSql && /\bUNION\s+(?:ALL\s+)?SELECT\b/i.test(res.data.rawSql)) {
      unionTableIds.value = [];
      const unionParts = res.data.rawSql.split(/\bUNION\s+(?:ALL\s+)?(?=SELECT\b)/i);
      for (let i = 1; i < unionParts.length; i++) {
        const fromMatch = unionParts[i].match(/\bFROM\s+(?:(\w+)\.)?(\w+)(?:\s+(\w+))?/i);
        if (fromMatch) {
          const tableName = fromMatch[2].toUpperCase();
          const tbl = tables.value.find(t => t.tableName.toUpperCase() === tableName);
          if (tbl && !unionTableIds.value.includes(tbl.id)) {
            unionTableIds.value.push(tbl.id);
            await loadColumns(tbl.id);
          }
        }
      }
    }
    await loadExistingConfig();
    // Collect all unique data source IDs used by main table + joins
    const dsIds = new Set<number>();
    const mainTable = tables.value.find(t => t.id === res.data.mainTableId);
    if (mainTable) {
      dsIds.add(mainTable.dataSourceId);
    }
    // Initialize joinDsId for existing joins
    for (let i = 0; i < form.joins.length; i++) {
      const jt = tables.value.find(t => t.id === form.joins[i].joinTableId);
      if (jt) {
        joinDsId.value[i] = jt.dataSourceId;
        dsIds.add(jt.dataSourceId);
      }
    }
    selectedDsIds.value = [...dsIds];
    // Keep tables in sync: only show tables from actually selected data sources
    tables.value = tables.value.filter(t => dsIds.has(t.dataSourceId));
  }
});
</script>

<template>
  <div>
    <div style="margin-bottom: 16px; display: flex; gap: 12px; align-items: center">
      <el-button @click="router.back()" text><el-icon><ArrowLeft /></el-icon> 返回</el-button>
      <span style="font-size: 16px; font-weight: 500">{{ isEdit ? '编辑查询配置' : '新增查询配置' }}</span>
    </div>

    <!-- Step Header -->
    <el-steps :active="activeStep" finish-status="success" align-center
      style="background: white; padding: 20px; border-radius: 4px; margin-bottom: 16px">
      <el-step title="基本信息" />
      <el-step title="关联表" />
      <el-step title="查询字段" />
      <el-step title="筛选条件" />
      <el-step title="展示设置" />
    </el-steps>

    <div style="background: white; padding: 20px; border-radius: 4px; min-height: 400px">

      <!-- Step 0: Basic Info -->
      <div v-show="activeStep === 0">
        <!-- Mode toggle -->
        <div style="margin-bottom: 16px">
          <el-radio-group v-model="inputMode" size="default">
            <el-radio-button value="manual">手动配置</el-radio-button>
            <el-radio-button value="sql">SQL 导入</el-radio-button>
          </el-radio-group>
        </div>

        <!-- Manual mode -->
        <el-form v-if="inputMode === 'manual'" label-width="100px">
          <el-form-item label="配置名称" required>
            <el-input v-model="form.name" placeholder="如：门诊收入汇总" />
          </el-form-item>
          <el-form-item label="数据源">
            <el-select v-model="selectedDsIds" placeholder="选择数据源（可多选）" @change="onDsChange"
              style="width: 100%" multiple>
              <el-option v-for="ds in dataSources" :key="ds.id" :label="ds.name" :value="ds.id" />
            </el-select>
          </el-form-item>
          <el-form-item label="主表" required>
            <el-select v-model="form.mainTableId" placeholder="选择主表" @change="onMainTableChange"
              style="width: 100%" filterable>
              <el-option v-for="t in tables" :key="t.id"
                :label="`[${getDsName(t.dataSourceId)}] ${t.alias || t.tableName} (${t.tableName})`"
                :value="t.id" />
            </el-select>
          </el-form-item>
        </el-form>

        <!-- SQL import mode -->
        <div v-if="inputMode === 'sql'">
          <el-form label-width="100px">
            <el-form-item label="配置名称" required>
              <el-input v-model="form.name" placeholder="如：门诊收入汇总" />
            </el-form-item>
          </el-form>
          <div style="margin-bottom: 12px">
            <span style="font-size: 13px; color: #909399">粘贴已有的 Oracle SQL 查询语句，系统自动解析并生成查询配置。</span>
          </div>
          <el-input v-model="sqlText" type="textarea" :rows="6"
            placeholder="SELECT &quot;T&quot;.&quot;COL1&quot; AS &quot;列1&quot;, &quot;T&quot;.&quot;COL2&quot;&#10;FROM &quot;HOSPITAL&quot;.&quot;TABLE&quot; &quot;T&quot;&#10;WHERE &quot;T&quot;.&quot;COL3&quot; >= TO_DATE('2024-01-01', 'YYYY-MM-DD')&#10;ORDER BY &quot;T&quot;.&quot;COL1&quot; ASC" />
          <div style="margin-top: 12px">
            <el-button type="primary" :loading="sqlParsing" @click="doParseSql">解析</el-button>
          </div>

          <!-- Parse result preview -->
          <div v-if="sqlResult" style="margin-top: 16px; border: 1px solid #e4e7ed; border-radius: 4px; padding: 16px">
            <div style="font-weight: 500; margin-bottom: 12px">解析结果预览</div>

            <div style="margin-bottom: 8px">
              <span style="color: #909399; font-size: 13px">主表：</span>
              <el-tag v-if="sqlResult.mainTableId" type="success" size="small">{{ sqlResult.mainTableName }}</el-tag>
              <el-tag v-else type="danger" size="small">{{ sqlResult.mainTableName || '未匹配' }}</el-tag>
            </div>

            <div style="margin-bottom: 8px">
              <span style="color: #909399; font-size: 13px">字段 ({{ sqlResult.columns.length }})：</span>
              <div style="display: flex; flex-wrap: wrap; gap: 4px; margin-top: 4px">
                <el-tag v-for="(c, i) in sqlResult.columns" :key="i"
                  :type="c.matched ? 'success' : 'warning'" size="small">
                  {{ c.alias || c.expression || '(未知)' }}
                  <span v-if="c.aggregateFunc" style="color: #00603D">[{{ c.aggregateFunc }}]</span>
                </el-tag>
              </div>
            </div>

            <div v-if="sqlResult.filters.length > 0" style="margin-bottom: 8px">
              <span style="color: #909399; font-size: 13px">筛选 ({{ sqlResult.filters.length }})：</span>
              <div style="display: flex; flex-wrap: wrap; gap: 4px; margin-top: 4px">
                <el-tag v-for="(f, i) in sqlResult.filters" :key="i"
                  :type="f.matched ? '' : 'warning'" size="small">
                  {{ f.label }} {{ f.operator }}
                  <span v-if="f.defaultValue" style="color: #00603D">[{{ f.defaultValue }}]</span>
                </el-tag>
              </div>
            </div>

            <div v-if="sqlResult.joins && sqlResult.joins.length > 0" style="margin-bottom: 8px">
              <span style="color: #909399; font-size: 13px">关联 ({{ sqlResult.joins.length }})：</span>
              <div style="display: flex; flex-wrap: wrap; gap: 4px; margin-top: 4px">
                <el-tag v-for="(j, i) in sqlResult.joins" :key="i"
                  :type="j.matched ? 'success' : 'warning'" size="small">
                  {{ j.joinTableName }} [{{ j.joinType }}]
                  <span v-if="j.leftColumnName && j.rightColumnName" style="color: #909399">
                    ({{ j.leftColumnName }} = {{ j.rightColumnName }})
                  </span>
                </el-tag>
              </div>
            </div>

            <div v-if="sqlResult.sortColumn" style="margin-bottom: 8px">
              <span style="color: #909399; font-size: 13px">排序：{{ sqlResult.sortColumn }} {{ sqlResult.sortDirection }}</span>
            </div>

            <div v-if="sqlResult.unmatchedColumns.length > 0" style="margin-top: 12px">
              <el-alert type="warning" :closable="false" show-icon>
                <template #title>
                  以下列未能匹配到元数据：{{ sqlResult.unmatchedColumns.join(', ') }}
                </template>
                请在后续步骤中手动选择对应的字段和筛选条件。
              </el-alert>
            </div>

            <div style="margin-top: 16px">
              <el-button type="primary" @click="applySqlResult">应用并继续</el-button>
            </div>
          </div>
        </div>
      </div>

      <!-- Step 2: Fields -->
      <div v-show="activeStep === 2">
        <div style="margin-bottom: 12px">
          <el-button size="small" @click="addField">+ 添加字段</el-button>
        </div>
        <el-table :data="form.fields" border stripe v-if="form.mainTableId">
          <el-table-column label="序号" width="60">
            <template #default="{ $index }">{{ $index + 1 }}</template>
          </el-table-column>
          <el-table-column label="字段" min-width="200">
            <template #default="{ row }">
              <el-select v-model="row.metaColumnId" placeholder="选择字段" filterable style="width: 100%" @change="(val: number) => onFieldColumnChange(row, val)">
                <el-option v-for="opt in getColumnOptions()" :key="opt.value"
                  :label="opt.label" :value="opt.value" />
              </el-select>
            </template>
          </el-table-column>
          <el-table-column label="显示别名" width="160">
            <template #default="{ row }">
              <el-input v-model="row.alias" placeholder="可选别名" size="small" />
            </template>
          </el-table-column>
          <el-table-column label="聚合函数" width="120">
            <template #default="{ row }">
              <el-select v-model="row.aggregateFunc" placeholder="无" clearable size="small">
                <el-option label="COUNT" value="COUNT" />
                <el-option label="SUM" value="SUM" />
                <el-option label="AVG" value="AVG" />
                <el-option label="MAX" value="MAX" />
                <el-option label="MIN" value="MIN" />
              </el-select>
            </template>
          </el-table-column>
          <el-table-column label="操作" width="120">
            <template #default="{ $index }">
              <el-button size="small" text :disabled="$index === 0" @click="moveUp(form.fields, $index)">↑</el-button>
              <el-button size="small" text :disabled="$index === form.fields.length - 1" @click="moveDown(form.fields, $index)">↓</el-button>
              <el-button size="small" type="danger" text @click="removeField($index)">删除</el-button>
            </template>
          </el-table-column>
        </el-table>
        <el-empty v-if="!form.mainTableId" description="请先在基本信息中选择主表" />
      </div>

      <!-- Step 3: Filters -->
      <div v-show="activeStep === 3">
        <div style="margin-bottom: 12px">
          <el-button size="small" @click="addFilter">+ 添加筛选</el-button>
        </div>
        <el-table :data="form.filters" border stripe v-if="form.mainTableId">
          <el-table-column label="字段" min-width="160">
            <template #default="{ row }">
              <el-select v-model="row.metaColumnId" placeholder="选择字段" filterable style="width: 100%">
                <el-option v-for="opt in getColumnOptions()" :key="opt.value"
                  :label="opt.label" :value="opt.value" />
              </el-select>
            </template>
          </el-table-column>
          <el-table-column label="标签" width="120">
            <template #default="{ row }">
              <el-input v-model="row.label" placeholder="筛选标签" size="small" />
            </template>
          </el-table-column>
          <el-table-column label="操作符" width="110">
            <template #default="{ row }">
              <el-tooltip :content="row.operator" placement="top">
                <el-select v-model="row.operator" size="small" popper-class="op-code-dropdown">
                  <el-option v-for="op in operators" :key="op.value" :label="op.label" :value="op.value">
                    <span>{{ op.label }} <span class="op-code">{{ op.value }}</span></span>
                  </el-option>
                </el-select>
              </el-tooltip>
            </template>
          </el-table-column>
          <el-table-column label="控件类型" width="110">
            <template #default="{ row }">
              <el-select v-model="row.controlType" size="small">
                <el-option v-for="ct in controlTypes" :key="ct.value" :label="ct.label" :value="ct.value" />
              </el-select>
            </template>
          </el-table-column>
          <el-table-column label="默认值" width="140">
            <template #default="{ row }">
              <el-input v-model="row.defaultValue" placeholder="可选" size="small" />
            </template>
          </el-table-column>
          <el-table-column label="必填" width="70" align="center">
            <template #default="{ row }">
              <el-switch v-model="row.isRequired" size="small" />
            </template>
          </el-table-column>
          <el-table-column label="上下文筛选" width="90" align="center">
            <template #default="{ row }">
              <el-switch v-model="row.isContextFilter" size="small" />
            </template>
          </el-table-column>
          <el-table-column label="上下文键" width="130">
            <template #default="{ row }">
              <el-select v-if="row.isContextFilter" v-model="row.contextKey" size="small" placeholder="选择" clearable>
                <el-option v-for="ck in contextKeys" :key="ck.value" :label="ck.label" :value="ck.value" />
              </el-select>
              <span v-else style="color: #c0c4cc; font-size: 12px">—</span>
            </template>
          </el-table-column>
          <el-table-column label="操作" width="120">
            <template #default="{ $index }">
              <el-button size="small" text :disabled="$index === 0" @click="moveUp(form.filters, $index)">↑</el-button>
              <el-button size="small" text :disabled="$index === form.filters.length - 1" @click="moveDown(form.filters, $index)">↓</el-button>
              <el-button size="small" type="danger" text @click="removeFilter($index)">删除</el-button>
            </template>
          </el-table-column>
        </el-table>
      </div>

      <!-- Step 1: Joins -->
      <div v-show="activeStep === 1">
        <div style="margin-bottom: 12px">
          <el-button size="small" @click="addJoin">+ 添加关联</el-button>
        </div>
        <el-table :data="form.joins" border stripe v-if="form.mainTableId">
          <el-table-column label="数据源" width="110">
            <template #default="{ $index }">
              <el-select v-model="joinDsId[$index]" size="small"
                @change="(id: number) => onJoinDsChange(id, $index)" style="width: 100%">
                <el-option v-for="ds in dataSources" :key="ds.id" :label="ds.name" :value="ds.id" />
              </el-select>
            </template>
          </el-table-column>
          <el-table-column label="关联表" min-width="180">
            <template #default="{ row, $index }">
              <el-select v-model="row.joinTableId" placeholder="选择关联表" @change="(id: number) => loadColumns(id)"
                filterable style="width: 100%">
                <el-option v-for="t in getJoinTables($index)" :key="t.id"
                  :label="`${t.alias || t.tableName}`" :value="t.id" />
              </el-select>
            </template>
          </el-table-column>
          <el-table-column label="关联类型" width="100">
            <template #default="{ row }">
              <el-select v-model="row.joinType" size="small">
                <el-option label="LEFT" value="LEFT" />
                <el-option label="INNER" value="INNER" />
              </el-select>
            </template>
          </el-table-column>
          <el-table-column label="左字段" min-width="160">
            <template #default="{ row, $index }">
              <el-select v-model="row.leftMetaColumnId" placeholder="选择字段" filterable style="width: 100%">
                <el-option v-for="opt in getColumnOptions([getLeftTableId($index)])" :key="opt.value"
                  :label="opt.label" :value="opt.value" />
              </el-select>
            </template>
          </el-table-column>
          <el-table-column label="右字段（关联表）" min-width="160">
            <template #default="{ row }">
              <el-select v-model="row.rightMetaColumnId" placeholder="关联表字段" filterable style="width: 100%"
                v-if="row.joinTableId">
                <el-option v-for="opt in getColumnOptions([row.joinTableId])" :key="opt.value"
                  :label="opt.label" :value="opt.value" />
              </el-select>
            </template>
          </el-table-column>
          <el-table-column label="TRUNC日期" width="100">
            <template #default="{ row }">
              <el-checkbox v-model="row.leftDateTrunc"
                title="对左字段应用 TRUNC()，去掉时分秒" />
            </template>
          </el-table-column>
          <el-table-column label="操作" width="120">
            <template #default="{ $index }">
              <el-button size="small" text :disabled="$index === 0" @click="moveUp(form.joins, $index)">↑</el-button>
              <el-button size="small" text :disabled="$index === form.joins.length - 1" @click="moveDown(form.joins, $index)">↓</el-button>
              <el-button size="small" type="danger" text @click="removeJoin($index)">删除</el-button>
            </template>
          </el-table-column>
        </el-table>
      </div>

      <!-- Step 4: Display Settings -->
      <div v-show="activeStep === 4">
        <el-form label-width="100px">
          <el-form-item label="展示方式">
            <el-radio-group v-model="form.displayType">
              <el-radio-button value="table">表格</el-radio-button>
              <el-radio-button value="number">数字</el-radio-button>
              <el-radio-button value="bar">柱状图</el-radio-button>
              <el-radio-button value="line">折线图</el-radio-button>
              <el-radio-button value="pie">饼图</el-radio-button>
            </el-radio-group>
          </el-form-item>
          <el-form-item label="排序列">
            <el-select v-model="form.sortColumn" placeholder="选择排序列" clearable filterable
              style="width: 300px">
              <el-option v-for="opt in getColumnOptions()" :key="opt.value"
                :label="opt.label" :value="opt.label" />
            </el-select>
            <el-radio-group v-model="form.sortDirection" style="margin-left: 12px">
              <el-radio value="ASC">升序</el-radio>
              <el-radio value="DESC">降序</el-radio>
            </el-radio-group>
          </el-form-item>
          <el-form-item label="每页条数">
            <el-input-number v-model="form.pageSize" :min="10" :max="5000" :step="10" />
          </el-form-item>
          <el-form-item label="启用">
            <el-switch v-model="form.isEnabled" />
          </el-form-item>
        </el-form>
      </div>

      <!-- Navigation buttons -->
      <div style="display: flex; justify-content: center; gap: 12px; margin-top: 24px">
        <el-button :disabled="activeStep === 0" @click="activeStep--">上一步</el-button>
        <el-button v-if="activeStep < 4" type="primary" @click="activeStep++">下一步</el-button>
        <el-button v-else type="primary" :loading="saving" @click="handleSave">
          {{ isEdit ? '保存修改' : '创建配置' }}
        </el-button>
      </div>
    </div>
  </div>
</template>
