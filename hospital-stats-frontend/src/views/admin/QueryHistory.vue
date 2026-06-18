<script setup lang="ts">
import { ref, onMounted } from 'vue';
import { executeApi, type HistoryEntry } from '../../api/execute';

const history = ref<HistoryEntry[]>([]);
const loading = ref(false);

async function loadHistory() {
  loading.value = true;
  try {
    const res = await executeApi.getHistory(50);
    history.value = res.data;
  } catch {
    history.value = [];
  } finally {
    loading.value = false;
  }
}

onMounted(loadHistory);
</script>

<template>
  <div>
    <div style="margin-bottom:16px">
      <el-button size="small" @click="loadHistory" :loading="loading">刷新</el-button>
    </div>

    <div style="background:#fff;padding:16px;border-radius:4px" v-loading="loading">
      <el-table :data="history" stripe>
        <el-table-column prop="queryConfigName" label="查询配置" min-width="140" />
        <el-table-column prop="userName" label="用户" width="100" />
        <el-table-column prop="executedAt" label="执行时间" width="160">
          <template #default="{ row }">
            {{ row.executedAt?.slice(0, 16)?.replace('T', ' ') }}
          </template>
        </el-table-column>
        <el-table-column prop="rowCount" label="结果行数" width="100" />
        <el-table-column prop="elapsedMs" label="耗时" width="80">
          <template #default="{ row }">
            {{ row.elapsedMs }}ms
          </template>
        </el-table-column>
      </el-table>
      <el-empty v-if="!loading && history.length === 0" description="暂无查询记录" />
    </div>
  </div>
</template>
