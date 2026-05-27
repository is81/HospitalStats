<script setup lang="ts">
import { ref, onMounted } from 'vue';
import { useRouter } from 'vue-router';
import { ElMessage, ElMessageBox } from 'element-plus';
import { queryApi, type QueryConfigItem } from '../../api/query';
import { dataSourcesApi, type DataSourceItem } from '../../api/datasources';

const router = useRouter();
const dataSources = ref<DataSourceItem[]>([]);
const selectedDsId = ref<number | null>(null);
const configs = ref<QueryConfigItem[]>([]);
const loading = ref(false);

async function loadDataSources() {
  const res = await dataSourcesApi.getAll();
  dataSources.value = res.data;
}

async function loadConfigs() {
  loading.value = true;
  try {
    const res = await queryApi.getConfigs(selectedDsId.value ?? undefined);
    configs.value = res.data;
  } finally {
    loading.value = false;
  }
}

async function handleDelete(id: number, name: string) {
  try {
    await ElMessageBox.confirm(`确定删除配置 "${name}" 吗？`, '确认', { type: 'warning' });
    await queryApi.deleteConfig(id);
    ElMessage.success('已删除');
    await loadConfigs();
  } catch { /* cancelled */ }
}

function goCreate() {
  router.push('/query/configs/new');
}

function goSqlImport() {
  router.push('/query/configs/new?mode=sql');
}

function goEdit(id: number) {
  router.push(`/query/configs/${id}`);
}

function getDisplayTypeLabel(type: string) {
  const map: Record<string, string> = { table: '表格', bar: '柱状图', line: '折线图', pie: '饼图' };
  return map[type] || type;
}

onMounted(() => {
  loadDataSources();
  loadConfigs();
});
</script>

<template>
  <div>
    <div style="margin-bottom: 16px; display: flex; gap: 12px; align-items: center">
      <el-select v-model="selectedDsId" placeholder="筛选数据源" clearable @change="loadConfigs"
        style="width: 220px">
        <el-option v-for="ds in dataSources" :key="ds.id" :label="ds.name" :value="ds.id" />
      </el-select>
      <el-button type="primary" @click="goCreate">新增查询配置</el-button>
      <el-button @click="goSqlImport">从 SQL 导入</el-button>
    </div>

    <el-table :data="configs" v-loading="loading" border stripe>
      <el-table-column prop="name" label="配置名称" min-width="180" />
      <el-table-column prop="mainTableName" label="主表" min-width="150" />
      <el-table-column label="展示方式" width="100">
        <template #default="{ row }">
          {{ getDisplayTypeLabel(row.displayType) }}
        </template>
      </el-table-column>
      <el-table-column label="状态" width="80">
        <template #default="{ row }">
          <el-tag :type="row.isEnabled ? 'success' : 'info'" size="small">
            {{ row.isEnabled ? '启用' : '禁用' }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column prop="updatedAt" label="更新时间" width="170" />
      <el-table-column label="操作" width="180">
        <template #default="{ row }">
          <el-button size="small" @click="goEdit(row.id)">编辑</el-button>
          <el-button size="small" type="danger" @click="handleDelete(row.id, row.name)">删除</el-button>
        </template>
      </el-table-column>
    </el-table>
    <el-empty v-if="!loading && configs.length === 0" description="暂无查询配置" />
  </div>
</template>
