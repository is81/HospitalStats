<script setup lang="ts">
import { ref, onMounted, nextTick } from 'vue';
import { dashboardApi, type DashboardCardData } from '../../api/dashboard';
import * as echarts from 'echarts';

const cards = ref<DashboardCardData[]>([]);
const loading = ref(false);

// 每个图表的 ECharts 实例，key 为卡片 ID
const chartInstances: Record<number, echarts.ECharts> = {};

// 图表容器 ref 模板引用
const chartRefs = ref<Record<number, HTMLDivElement | null>>({});

function setChartRef(id: number) {
  return (el: any) => {
    chartRefs.value[id] = el;
  };
}

async function loadDashboard() {
  loading.value = true;
  try {
    const res = await dashboardApi.getDashboard();
    cards.value = res.data;
    await nextTick();
    renderCharts();
  } finally {
    loading.value = false;
  }
}

function renderCharts() {
  for (const card of cards.value) {
    const type = card.displayType;
    if (type === 'bar' || type === 'line' || type === 'pie') {
      renderChart(card, type);
    }
  }
}

function renderChart(card: DashboardCardData, type: string) {
  const el = chartRefs.value[card.id];
  if (!el || !card.data?.rows || !card.data?.columns) return;

  // 销毁旧实例（如果存在）
  if (chartInstances[card.id]) {
    chartInstances[card.id]!.dispose();
  }

  const instance = echarts.init(el);
  chartInstances[card.id] = instance;

  const cols = card.data.columns as string[];
  const rows = card.data.rows as Record<string, any>[];

  if (type === 'pie') {
    instance.setOption({
      tooltip: { trigger: 'item' },
      legend: {
        orient: 'horizontal',
        bottom: 0,
        type: 'scroll',
        textStyle: { fontSize: 11 },
      },
      series: [{
        type: 'pie',
        radius: ['45%', '70%'],
        center: ['50%', '48%'],
        label: { fontSize: 10 },
        data: rows.map(row => ({
          name: String(row[cols[0]] ?? ''),
          value: Number(row[cols[1]] ?? 0),
        })),
        emphasis: { itemStyle: { shadowBlur: 10, shadowOffsetX: 0, shadowColor: 'rgba(0, 0, 0, 0.5)' } },
      }],
    });
  } else {
    // bar / line
    instance.setOption({
      tooltip: { trigger: 'axis' },
      xAxis: {
        type: 'category',
        data: rows.map(row => String(row[cols[0]] ?? '')),
        axisLabel: { rotate: 30 },
      },
      yAxis: { type: 'value' },
      series: cols.slice(1).map(col => ({
        name: col,
        type: type,
        data: rows.map(row => Number(row[col] ?? 0)),
      })),
    });
  }
}

function getIcon(icon: string | null) {
  const map: Record<string, string> = {
    money: '💰', people: '👥', hospital: '🏥', medicine: '💊',
    chart: '📊', calendar: '📅', doc: '📋',
  };
  return map[icon || ''] || '📈';
}

onMounted(loadDashboard);
</script>

<template>
  <div>
    <div style="margin-bottom: 16px; display: flex; gap: 12px; align-items: center">
      <span style="font-size: 18px; font-weight: 600">仪表盘</span>
      <el-button size="small" @click="loadDashboard" :loading="loading">刷新</el-button>
    </div>

    <div class="dash-grid" v-loading="loading">
      <div v-for="card in cards" :key="card.id" :style="{ gridColumn: `span ${card.width}` }">
        <div class="dash-card" :style="{ borderTopColor: card.color || '#409EFF' }">
          <div class="card-header">
            <span class="card-icon">{{ getIcon(card.icon) }}</span>
            <span class="card-title">{{ card.title }}</span>
          </div>
          <div class="card-body">
            <div v-if="card.data?.error" class="card-error">{{ card.data.error }}</div>
            <div v-else-if="card.displayType === 'number'" class="card-value">
              {{ card.data?.value || '-' }}
              <span v-if="card.unit" class="card-unit">{{ card.unit }}</span>
            </div>
            <div v-else-if="card.displayType === 'bar' || card.displayType === 'line' || card.displayType === 'pie'"
              class="chart-container" style="height: 300px">
              <div :ref="setChartRef(card.id)" style="width:100%;height:300px" />
            </div>
            <div v-else class="card-subtitle">
              {{ card.data?.total ?? '-' }} 条记录
            </div>
          </div>
        </div>
      </div>
    </div>

    <el-empty v-if="!loading && cards.length === 0"
      description="暂无仪表盘卡片，请在配置中添加" />
  </div>
</template>

<style scoped>
.dash-grid {
  display: grid;
  grid-template-columns: repeat(24, 1fr);
  gap: 16px;
  align-items: start;
}
.dash-card {
  background: white;
  border-radius: 6px;
  padding: 20px;
  border-top: 3px solid #409EFF;
  transition: box-shadow 0.2s;
}
.dash-card:hover { box-shadow: 0 2px 12px rgba(0,0,0,0.08); }
.card-header { display: flex; align-items: center; margin-bottom: 12px; }
.card-icon { font-size: 20px; margin-right: 8px; }
.card-title { font-size: 14px; color: #909399; }
.card-body { padding: 4px 0; }
.card-value { font-size: 36px; font-weight: 700; color: #303133; }
.card-unit { font-size: 14px; color: #909399; margin-left: 4px; }
.card-subtitle { font-size: 16px; color: #606266; }
.card-error { color: #f56c6c; font-size: 13px; }
</style>
