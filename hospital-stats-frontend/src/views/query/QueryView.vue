<script setup lang="ts">
import { ref, computed, onMounted } from 'vue';
import { useRoute } from 'vue-router';
import { ElMessage } from 'element-plus';
import { queryApi, type QueryConfigDetail } from '../../api/query';
import { executeApi, type QueryResult } from '../../api/execute';
import * as echarts from 'echarts';

const route = useRoute();
const configId = Number(route.params.configId);

const config = ref<QueryConfigDetail | null>(null);
const filterValues = ref<Record<string, string>>({});
const filterOptions = ref<Record<string, string[]>>({});
const result = ref<QueryResult | null>(null);
const loading = ref(false);
const chartRef = ref<HTMLDivElement>();
let chartInstance: echarts.ECharts | null = null;

const page = ref(1);

const visibleFilters = computed(() =>
  config.value?.filters.filter(f => !f.isContextFilter) ?? []
);

async function loadConfig() {
  const res = await queryApi.getConfig(configId);
  config.value = res.data;
  // init filter defaults
  for (const f of res.data.filters) {
    if (f.isContextFilter) continue;
    if (f.defaultValue) {
      filterValues.value[f.id.toString()] = f.defaultValue;
    }
  }
  // load options for select-type filters
  for (const f of res.data.filters) {
    if (f.isContextFilter) continue;
    if (f.controlType === 'select') {
      try {
        const optRes = await executeApi.getFilterOptions(configId, f.id);
        filterOptions.value[f.id.toString()] = optRes.data;
      } catch { /* ignore */ }
    }
  }
}

async function doQuery(p?: number) {
  if (!config.value) return;
  page.value = p || 1;
  loading.value = true;
  try {
    const res = await executeApi.execute(
      configId,
      filterValues.value,
      page.value,
      config.value.pageSize ?? 50
    );
    result.value = res.data;
    if (config.value.displayType !== 'table') {
      setTimeout(renderChart, 100);
    }
  } catch (e: any) {
    ElMessage.error(e.response?.data?.message || '查询失败');
  } finally {
    loading.value = false;
  }
}

function renderChart() {
  if (!chartRef.value || !result.value || !config.value) return;
  if (!chartInstance) {
    chartInstance = echarts.init(chartRef.value);
  }

  const r = result.value;
  const displayType = config.value.displayType;

  if (displayType === 'pie') {
    chartInstance.setOption({
      tooltip: { trigger: 'item' },
      legend: { orient: 'vertical', left: 'left' },
      series: [{
        type: 'pie',
        radius: '60%',
        data: r.rows.map(row => ({
          name: String(row[r.columns[0]] ?? ''),
          value: Number(row[r.columns[1]] ?? 0),
        })),
        emphasis: { itemStyle: { shadowBlur: 10, shadowOffsetX: 0, shadowColor: 'rgba(0, 0, 0, 0.5)' } },
      }],
    });
  } else if (displayType === 'bar' || displayType === 'line') {
    chartInstance.setOption({
      tooltip: { trigger: 'axis' },
      xAxis: {
        type: 'category',
        data: r.rows.map(row => String(row[r.columns[0]] ?? '')),
        axisLabel: { rotate: 30 },
      },
      yAxis: { type: 'value' },
      series: r.columns.slice(1).map(col => ({
        name: col,
        type: displayType,
        data: r.rows.map(row => Number(row[col] ?? 0)),
      })),
    });
  }
}

async function doExport() {
  if (!config.value) return;
  try {
    await executeApi.exportExcel(configId, filterValues.value);
    ElMessage.success('导出成功');
  } catch {
    ElMessage.error('导出失败');
  }
}

const operatorLabels: Record<string, string> = {
  EQ: '=',
  NE: '≠',
  GT: '>',
  GTE: '≥',
  LT: '<',
  LTE: '≤',
  LIKE: '包含',
  'NOT LIKE': '不包含',
  IN: '属于',
  'NOT IN': '不属于',
  BETWEEN: '介于',
  'NOT BETWEEN': '不介于',
};

function getFilterLabel(f: { label?: string | null; columnAlias?: string | null; columnName?: string | null }) {
  return f.label || f.columnAlias || f.columnName || '';
}

function getOperatorLabel(op: string) {
  return operatorLabels[op] || op;
}

onMounted(loadConfig);
</script>

<template>
  <div>
    <div style="margin-bottom: 16px; display: flex; gap: 12px; align-items: center">
      <span style="font-size: 16px; font-weight: 500">
        {{ config?.name || '查询' }}
      </span>
      <span v-if="result" style="color: #909399; font-size: 13px">
        共 {{ result.total }} 条，耗时 {{ result.elapsedMs }}ms
      </span>
    </div>

    <!-- Filter Form -->
    <div v-if="config && visibleFilters.length > 0"
      style="background: white; padding: 16px; border-radius: 4px; margin-bottom: 16px">
      <el-form inline>
        <el-form-item v-for="f in visibleFilters" :key="f.id">
          <template #label>
            <span>{{ getFilterLabel(f) }}
              <el-tag size="small" type="info" style="margin-left: 4px">{{ getOperatorLabel(f.operator) }}</el-tag>
            </span>
          </template>
          <el-date-picker v-if="f.controlType === 'date'"
            v-model="filterValues[f.id.toString()]"
            type="date" value-format="YYYY-MM-DD" placeholder="选择日期" />
          <el-select v-else-if="f.controlType === 'select'"
            v-model="filterValues[f.id.toString()]"
            placeholder="请选择" clearable style="width: 180px">
            <el-option v-for="opt in filterOptions[f.id.toString()]"
              :key="opt" :label="opt" :value="opt" />
          </el-select>
          <el-input v-else
            v-model="filterValues[f.id.toString()]"
            :placeholder="f.label || '请输入'" clearable style="width: 180px" />
        </el-form-item>
        <el-form-item>
          <el-button type="primary" :loading="loading" @click="doQuery(1)">查询</el-button>
          <el-button @click="doExport" :disabled="!result || result.total === 0">导出Excel</el-button>
        </el-form-item>
      </el-form>
    </div>

    <div v-if="!config" v-loading="true" style="min-height: 200px" />

    <!-- Table Result -->
    <div v-if="result && config?.displayType === 'table'"
      style="background: white; padding: 16px; border-radius: 4px">
      <el-table :data="result.rows" v-loading="loading" border stripe
        max-height="calc(100vh - 320px)">
        <el-table-column v-for="col in result.columns" :key="col"
          :prop="col" :label="col" min-width="140" show-overflow-tooltip />
      </el-table>

      <div style="display: flex; justify-content: center; margin-top: 16px">
        <el-pagination
          v-model:current-page="page"
          :page-size="result.pageSize"
          :total="result.total"
          layout="prev, pager, next, total"
          @current-change="doQuery" />
      </div>
    </div>

    <!-- Chart Result -->
    <div v-if="result && config?.displayType !== 'table'"
      style="background: white; padding: 16px; border-radius: 4px">
      <div ref="chartRef" style="width: 100%; height: 450px" />
    </div>

    <!-- Empty -->
    <el-empty v-if="!loading && !result" description="设置筛选条件后点击查询" />
  </div>
</template>
