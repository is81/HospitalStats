<script setup lang="ts">
import { ref, onMounted } from 'vue';
import { useRouter } from 'vue-router';
import { ElMessage, ElMessageBox } from 'element-plus';
import { metaApi, type BizDomain, type MetaTable, type ScanResult } from '../../api/meta';
import { dataSourcesApi, type DataSourceItem } from '../../api/datasources';

const router = useRouter();

// data sources
const dataSources = ref<DataSourceItem[]>([]);
const selectedDsId = ref<number | null>(null);
const searchText = ref('');

// domains
const domains = ref<BizDomain[]>([]);
const domainFormVisible = ref(false);
const domainForm = ref({ name: '', description: '', sortOrder: 0 });
const editingDomainId = ref<number | null>(null);
const selectedDomainId = ref<number | null>(null);

// tables
const tables = ref<MetaTable[]>([]);
const tableLoading = ref(false);
const allTablesCount = ref(0);

// scan
const scanning = ref(false);
const scanResult = ref<ScanResult | null>(null);
const scanSchema = ref('');

async function loadDataSources() {
  const res = await dataSourcesApi.getAll();
  dataSources.value = res.data;
}

async function loadDomains() {
  const res = await metaApi.getDomains(selectedDsId.value ?? undefined);
  domains.value = res.data;
}

async function loadTables() {
  if (!selectedDsId.value) return;
  tableLoading.value = true;
  try {
    const res = await metaApi.getTables(
      selectedDsId.value,
      selectedDomainId.value ?? undefined,
      searchText.value || undefined
    );
    tables.value = res.data;
    // 无筛选时记录真实总数
    if (!selectedDomainId.value && !searchText.value) {
      allTablesCount.value = res.data.length;
    }
  } finally {
    tableLoading.value = false;
  }
}

function onDsChange() {
  scanResult.value = null;
  tables.value = [];
  allTablesCount.value = 0;
  loadDomains();
  loadTables();
}

function onDomainSelect(domainId: number | null) {
  selectedDomainId.value = domainId;
  loadTables();
}

function openDomainDialog(domain?: BizDomain) {
  if (domain) {
    editingDomainId.value = domain.id;
    domainForm.value = { name: domain.name, description: domain.description || '', sortOrder: domain.sortOrder };
  } else {
    editingDomainId.value = null;
    domainForm.value = { name: '', description: '', sortOrder: domains.value.length };
  }
  domainFormVisible.value = true;
}

async function saveDomain() {
  if (!domainForm.value.name) { ElMessage.warning('请输入名称'); return; }
  if (editingDomainId.value) {
    await metaApi.updateDomain(editingDomainId.value, domainForm.value);
    ElMessage.success('已更新');
  } else {
    await metaApi.createDomain(domainForm.value);
    ElMessage.success('已添加');
  }
  domainFormVisible.value = false;
  await loadDomains();
}

async function deleteDomain(id: number) {
  try {
    await ElMessageBox.confirm('确定删除该业务域？', '确认', { type: 'warning' });
    await metaApi.deleteDomain(id);
    ElMessage.success('已删除');
    await loadDomains();
  } catch { /* cancelled */ }
}

async function doScan() {
  if (!selectedDsId.value) return;
  scanning.value = true;
  try {
    const res = await metaApi.scan(selectedDsId.value, scanSchema.value || undefined);
    scanResult.value = res.data;
    ElMessage.success(`扫描完成：新增 ${res.data.created} 张表，更新 ${res.data.updated} 张表`);
    await loadTables();
    await loadDomains();
  } catch (e: any) {
    ElMessage.error(e.response?.data?.message || '扫描失败');
  } finally {
    scanning.value = false;
  }
}

async function toggleTable(table: MetaTable) {
  await metaApi.updateTable(table.id, {
    alias: table.alias ?? undefined,
    description: table.description ?? undefined,
    bizDomainId: table.bizDomainId,
    isEnabled: !table.isEnabled,
  });
  table.isEnabled = !table.isEnabled;
}

async function onChangeDomain(row: MetaTable, val: number | null | undefined) {
  const newBizDomainId = val ?? null;
  const oldBizDomainId = row.bizDomainId;
  const oldBizDomainName = row.bizDomainName;
  // 乐观更新
  row.bizDomainId = newBizDomainId;
  row.bizDomainName = domains.value.find(d => d.id === newBizDomainId)?.name ?? null;
  try {
    await metaApi.updateTable(row.id, { bizDomainId: newBizDomainId, isEnabled: row.isEnabled });
    await loadDomains();
  } catch (e: any) {
    // 失败回滚
    row.bizDomainId = oldBizDomainId;
    row.bizDomainName = oldBizDomainName;
    ElMessage.error(e.response?.data?.message || '更新失败');
  }
}

function goToColumns(tableId: number) {
  router.push(`/meta/tables/${tableId}`);
}

onMounted(() => {
  loadDataSources();
  loadDomains();
});
</script>

<template>
  <div>
    <div style="margin-bottom: 16px; display: flex; gap: 12px; align-items: center; flex-wrap: wrap">
      <el-select v-model="selectedDsId" placeholder="选择数据源" @change="onDsChange"
        style="width: 240px">
        <el-option v-for="ds in dataSources" :key="ds.id" :label="ds.name" :value="ds.id" />
      </el-select>
      <el-input v-model="searchText" placeholder="搜索表名/别名" style="width: 200px" clearable
        @change="loadTables" @clear="loadTables" @keyup.enter="loadTables" />
      <el-input v-model="scanSchema" placeholder="Schema（可选）" style="width: 160px" />
      <el-button type="success" :loading="scanning" @click="doScan"
        :disabled="!selectedDsId">
        扫描表结构
      </el-button>
    </div>

    <el-alert v-if="scanResult" type="success" :closable="false" style="margin-bottom: 16px">
      Schema: {{ scanResult.schema }} |
      表 {{ scanResult.tablesFound }} 张 | 视图 {{ scanResult.viewsFound }} 个 |
      新增 {{ scanResult.created }} | 更新 {{ scanResult.updated }}
    </el-alert>

    <el-row :gutter="16">
      <!-- 左侧：业务域树 -->
      <el-col :span="5">
        <div style="background: white; padding: 12px; border-radius: 4px; min-height: 400px">
          <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 8px">
            <strong>业务域</strong>
            <el-button size="small" text type="primary" @click="openDomainDialog()">+ 新增</el-button>
          </div>
          <div
            :class="{ 'domain-item': true, 'active': selectedDomainId === null }"
            @click="onDomainSelect(null)"
            style="padding: 6px 8px; cursor: pointer; border-radius: 4px">
            全部 ({{ allTablesCount }})
          </div>
          <div v-for="d in domains" :key="d.id"
            :class="{ 'domain-item': true, 'active': selectedDomainId === d.id }"
            @click="onDomainSelect(d.id)"
            style="padding: 6px 8px; cursor: pointer; border-radius: 4px; display: flex; justify-content: space-between">
            <span>{{ d.name }} ({{ d.tableCount }})</span>
            <span>
              <el-button size="small" text @click.stop="openDomainDialog(d)">
                <el-icon><Edit /></el-icon>
              </el-button>
              <el-button size="small" text @click.stop="deleteDomain(d.id)">
                <el-icon><Delete /></el-icon>
              </el-button>
            </span>
          </div>
        </div>
      </el-col>

      <!-- 右侧：表列表 -->
      <el-col :span="19">
        <el-table :data="tables" v-loading="tableLoading" border stripe
          style="width: 100%" v-if="selectedDsId"
          empty-text="请选择数据源并点击扫描">
          <el-table-column prop="tableName" label="表名" min-width="180" />
          <el-table-column prop="alias" label="中文别名" min-width="150">
            <template #default="{ row }">
              <span :style="{ color: row.alias ? '#303133' : '#c0c4cc' }">
                {{ row.alias || '未设置' }}
              </span>
            </template>
          </el-table-column>
          <el-table-column label="业务域" width="160">
            <template #default="{ row }">
              <el-select
                :model-value="row.bizDomainId"
                size="small"
                placeholder="无"
                clearable
                style="width: 100%"
                @change="(val: any) => onChangeDomain(row, val)"
              >
                <el-option :value="null" label="无" />
                <el-option v-for="d in domains" :key="d.id" :label="d.name" :value="d.id" />
              </el-select>
            </template>
          </el-table-column>
          <el-table-column label="类型" width="70">
            <template #default="{ row }">
              <el-tag size="small" :type="row.isView ? 'warning' : ''">
                {{ row.isView ? '视图' : '表' }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column prop="columnCount" label="字段数" width="80" align="center" />
          <el-table-column label="启用" width="70" align="center">
            <template #default="{ row }">
              <el-switch :model-value="row.isEnabled" size="small"
                @change="toggleTable(row)" />
            </template>
          </el-table-column>
          <el-table-column label="操作" width="120">
            <template #default="{ row }">
              <el-button size="small" type="primary" @click="goToColumns(row.id)">
                字段
              </el-button>
            </template>
          </el-table-column>
        </el-table>
        <el-empty v-if="!selectedDsId" description="请先选择一个数据源" />
      </el-col>
    </el-row>

    <!-- 业务域弹窗 -->
    <el-dialog v-model="domainFormVisible"
      :title="editingDomainId ? '编辑业务域' : '新增业务域'" width="420px">
      <el-form :model="domainForm" label-width="80px">
        <el-form-item label="名称">
          <el-input v-model="domainForm.name" placeholder="如：门诊" />
        </el-form-item>
        <el-form-item label="描述">
          <el-input v-model="domainForm.description" placeholder="可选描述" />
        </el-form-item>
        <el-form-item label="排序">
          <el-input-number v-model="domainForm.sortOrder" :min="0" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="domainFormVisible = false">取消</el-button>
        <el-button type="primary" @click="saveDomain">保存</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.domain-item:hover { background: #f5f7fa; }
.domain-item.active { background: #edf8f2; color: #00603D; font-weight: bold; }
</style>
