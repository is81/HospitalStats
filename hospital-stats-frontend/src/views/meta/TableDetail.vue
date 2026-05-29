<script setup lang="ts">
import { ref, onMounted } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { ElMessage } from 'element-plus';
import { metaApi, type MetaColumn } from '../../api/meta';

const route = useRoute();
const router = useRouter();
const tableId = Number(route.params.tableId);

const columns = ref<MetaColumn[]>([]);
const loading = ref(false);

async function loadColumns() {
  loading.value = true;
  try {
    const res = await metaApi.getColumns(tableId);
    columns.value = res.data;
  } finally {
    loading.value = false;
  }
}

async function saveColumn(col: MetaColumn) {
  try {
    await metaApi.updateColumn(col.id, {
      alias: col.alias ?? undefined,
      isQueryField: col.isQueryField,
      isFilterField: col.isFilterField,
      isDisplayField: col.isDisplayField,
    });
    ElMessage.success('已保存');
  } catch (e: any) {
    ElMessage.error(e?.response?.data?.message || '保存失败');
  }
}

function goBack() {
  router.back();
}

onMounted(loadColumns);
</script>

<template>
  <div>
    <div style="margin-bottom: 16px; display: flex; gap: 12px; align-items: center">
      <el-button @click="goBack" text>
        <el-icon><ArrowLeft /></el-icon> 返回
      </el-button>
      <span style="font-size: 16px; font-weight: 500">表字段设置</span>
    </div>

    <el-table :data="columns" v-loading="loading" border stripe max-height="calc(100vh - 200px)">
      <el-table-column prop="sortOrder" label="#" width="50" align="center" />
      <el-table-column prop="columnName" label="字段名" width="160" fixed />
      <el-table-column label="中文别名" min-width="160">
        <template #default="{ row }">
          <el-input v-model="row.alias" size="small" placeholder="输入中文别名" clearable />
        </template>
      </el-table-column>
      <el-table-column prop="dataType" label="数据类型" width="100" />
      <el-table-column label="长度" width="80" align="center">
        <template #default="{ row }">
          {{ row.dataLength || row.dataPrecision ? (row.dataPrecision ? `${row.dataPrecision},${row.dataScale ?? 0}` : row.dataLength) : '-' }}
        </template>
      </el-table-column>
      <el-table-column prop="nullable" label="可空" width="65" align="center">
        <template #default="{ row }">{{ row.nullable ? 'Y' : 'N' }}</template>
      </el-table-column>
      <el-table-column prop="comment" label="Oracle注释" min-width="150" show-overflow-tooltip />
      <el-table-column label="查询字段" width="90" align="center">
        <template #default="{ row }">
          <el-switch v-model="row.isQueryField" size="small" />
        </template>
      </el-table-column>
      <el-table-column label="筛选字段" width="90" align="center">
        <template #default="{ row }">
          <el-switch v-model="row.isFilterField" size="small" />
        </template>
      </el-table-column>
      <el-table-column label="显示字段" width="90" align="center">
        <template #default="{ row }">
          <el-switch v-model="row.isDisplayField" size="small" />
        </template>
      </el-table-column>
      <el-table-column label="保存" width="70" align="center" fixed="right">
        <template #default="{ row }">
          <el-button size="small" type="primary" @click="saveColumn(row)">保存</el-button>
        </template>
      </el-table-column>
    </el-table>
  </div>
</template>
