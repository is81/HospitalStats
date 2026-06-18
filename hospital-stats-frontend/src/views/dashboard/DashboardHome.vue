<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted, nextTick } from 'vue';
import { ElMessage, ElMessageBox, ElEmpty } from 'element-plus';
import { dashboardApi, type DashboardCardData, type DashboardFilter } from '../../api/dashboard';
import { settingsApi } from '../../api/settings';
import { useAuthStore } from '../../stores/auth';
import * as echarts from 'echarts';

const authStore = useAuthStore();
const canAccess = computed(() => authStore.isAdmin || authStore.dashboardAccess);

function localDate(d: Date): string {
  const y = d.getFullYear();
  const m = String(d.getMonth() + 1).padStart(2, '0');
  const day = String(d.getDate()).padStart(2, '0');
  return `${y}-${m}-${day}`;
}

const defaultDays = ref(1);

function defaultDateFrom() {
  const d = new Date();
  d.setDate(d.getDate() - defaultDays.value);
  return localDate(d);
}
function defaultDateTo() {
  return localDate(new Date());
}

const allCards = ref<DashboardCardData[]>([]);
const loading = ref(false);
const filters = ref<DashboardFilter>({
  dateFrom: defaultDateFrom(),
  dateTo: defaultDateTo(),
});

let loadGen = 0;
const chartInstances: Record<number, echarts.ECharts> = {};
const chartRefs = ref<Record<number, HTMLDivElement | null>>({});

function setChartRef(id: number) {
  return (el: any) => {
    if (el) chartRefs.value[id] = el;
    else delete chartRefs.value[id];
  };
}

async function loadDashboard() {
  const gen = ++loadGen;
  loading.value = true;
  try {
    const params: DashboardFilter = {};
    if (filters.value.dateFrom) params.dateFrom = filters.value.dateFrom;
    if (filters.value.dateTo) params.dateTo = filters.value.dateTo;
    const res = await dashboardApi.getDashboard(Object.keys(params).length ? params : undefined);
    if (gen !== loadGen) return;
    allCards.value = res.data;
    await nextTick();
    renderCharts();
  } catch {
    ElMessage.error('仪表盘加载失败，请检查网络连接');
  } finally {
    if (gen === loadGen) loading.value = false;
  }
}

function daysBetween(from: string, to: string) {
  return (new Date(to).getTime() - new Date(from).getTime()) / 86400000;
}

async function maybeLoadDashboard() {
  const from = filters.value.dateFrom;
  const to = filters.value.dateTo;
  if (from && to && daysBetween(from, to) >= 180) {
    (document.activeElement as HTMLElement)?.blur();
    try {
      await ElMessageBox.confirm('日期范围超过6个月，查询可能较慢，确定执行？', '提示', { confirmButtonText: '确定', cancelButtonText: '取消', type: 'warning' });
    } catch { return; }
  }
  loadDashboard();
}

function onFilterChange() {
  if (!filters.value.dateFrom || !filters.value.dateTo) {
    filters.value.dateFrom = defaultDateFrom();
    filters.value.dateTo = defaultDateTo();
  }
  maybeLoadDashboard();
}

function quickDate(months: number) {
  const now = new Date();
  const dateTo = new Date(now.getFullYear(), now.getMonth(), 0); // 上月最后一天
  const dateFrom = new Date(now.getFullYear(), now.getMonth() - months, 1); // N 个月前第一天
  filters.value = { dateFrom: localDate(dateFrom), dateTo: localDate(dateTo) };
  maybeLoadDashboard();
}

function activePreset() {
  const from = filters.value.dateFrom;
  const to = filters.value.dateTo;
  if (!from || !to) return 0;
  const now = new Date();
  const presetTo = localDate(new Date(now.getFullYear(), now.getMonth(), 0));
  if (to !== presetTo) return 0;
  for (const m of [1, 2, 3, 12]) {
    const firstDay = localDate(new Date(now.getFullYear(), now.getMonth() - m, 1));
    if (firstDay === from) return m;
  }
  return 0;
}

function renderCharts() {
  for (const card of allCards.value) {
    const type = card.displayType;
    if (type === 'bar' || type === 'line' || type === 'pie') {
      renderChart(card, type);
    }
  }
}

function renderChart(card: DashboardCardData, type: string) {
  const el = chartRefs.value[card.id];
  if (!el || !card.data?.rows || !card.data?.columns) return;

  if (chartInstances[card.id]) chartInstances[card.id]!.dispose();

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
    instance.setOption({
      tooltip: { trigger: 'axis' },
      grid: { left: 10, right: 20, top: 10, bottom: 30, containLabel: true },
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

function handleResize() {
  for (const inst of Object.values(chartInstances)) {
    try { inst.resize(); } catch { /* disposed */ }
  }
}

let resizeTimer: ReturnType<typeof setTimeout> | null = null;
function onWindowResize() {
  if (resizeTimer) clearTimeout(resizeTimer);
  resizeTimer = setTimeout(handleResize, 150);
}

function getIcon(icon: string | null) {
  const map: Record<string, string> = {
    money: '💰', people: '👥', hospital: '🏥', medicine: '💊',
    chart: '📊', calendar: '📅', doc: '📋',
  };
  return map[icon || ''] || '📈';
}

function formatValue(raw: string | undefined, unit: string | null, decimalPlaces?: number | null): string {
  if (raw == null || raw === '' || raw === '-') return '-';
  const n = Number(raw);
  if (isNaN(n)) return raw;
  let v = n;
  if (unit === '万元') v = n / 10000;
  const dp = decimalPlaces ?? 2;
  return v.toLocaleString('zh-CN', { minimumFractionDigits: dp, maximumFractionDigits: dp });
}

onMounted(async () => {
  try {
    const res = await settingsApi.getAll();
    defaultDays.value = Number(res.data.DashboardDefaultDays || '1');
    filters.value.dateFrom = defaultDateFrom();
  } catch { /* use defaults */ }
  loadDashboard();
  window.addEventListener('resize', onWindowResize);
});

onUnmounted(() => {
  window.removeEventListener('resize', onWindowResize);
  if (resizeTimer) clearTimeout(resizeTimer);
  for (const inst of Object.values(chartInstances)) {
    try { inst.dispose(); } catch { /* already disposed */ }
  }
});
</script>

<template>
  <div v-if="canAccess">
    <div style="margin-bottom: 12px; display: flex; gap: 8px; align-items: center; flex-wrap: wrap">
      <span style="font-size: 13px; color: #606266">开始日期</span>
      <el-date-picker
        v-model="filters.dateFrom"
        type="date"
        placeholder="选择日期"
        value-format="YYYY-MM-DD"
        size="small"
        style="width: 130px"
        @change="onFilterChange"
      />
      <span style="font-size: 13px; color: #606266; margin-left: 4px">结束日期</span>
      <el-date-picker
        v-model="filters.dateTo"
        type="date"
        placeholder="选择日期"
        value-format="YYYY-MM-DD"
        size="small"
        style="width: 130px"
        @change="onFilterChange"
      />
      <el-button-group size="small">
        <el-button :type="activePreset() === 1 ? 'primary' : 'default'" @click="quickDate(1)">近1月</el-button>
        <el-button :type="activePreset() === 2 ? 'primary' : 'default'" @click="quickDate(2)">近2月</el-button>
        <el-button :type="activePreset() === 3 ? 'primary' : 'default'" @click="quickDate(3)">近3月</el-button>
        <el-button :type="activePreset() === 12 ? 'primary' : 'default'" @click="quickDate(12)">近1年</el-button>
      </el-button-group>
      <el-button size="small" @click="loadDashboard" :loading="loading" type="primary">刷新</el-button>
      <span style="font-size: 12px; color: #909399">默认显示昨天 ~ 今天，查更多请修改起止日期</span>
    </div>

    <div class="dash-grid" v-loading="loading">
      <div v-for="card in allCards" :key="card.id" :style="{ gridColumn: `span ${card.width}` }">
        <div class="dash-card" :style="{ borderTopColor: card.color || '#00603D' }">
          <div class="card-header">
            <span class="card-icon">{{ getIcon(card.icon) }}</span>
            <span class="card-title">{{ card.title }}</span>
            <el-tag v-if="card.compareMode" size="small" type="warning" style="margin-left: 6px">
              {{ card.compareMode === 'mom' ? '环比' : '同比' }}
            </el-tag>
            <span v-if="card.data?.compareLabel && card.data?.changePct != null" class="card-compare-inline"
              :class="card.data.changePct >= 0 ? 'up' : 'down'">
              <span class="arrow">{{ card.data.changePct >= 0 ? '↑' : '↓' }}</span>
              {{ Math.abs(card.data.changePct) }}%
            </span>
          </div>
          <div class="card-body">
            <div v-if="card.data?.error" class="card-error">{{ card.data.error }}</div>
            <div v-else-if="card.displayType === 'number'" class="card-value">
              <div style="display: flex; align-items: baseline; gap: 6px; flex-wrap: wrap">
                <span>{{ formatValue(card.data?.value, card.unit, card.decimalPlaces) }}</span>
                <span v-if="card.unit" class="card-unit">{{ card.unit }}</span>
              </div>
            </div>
            <div v-else-if="card.displayType === 'bar' || card.displayType === 'line' || card.displayType === 'pie'"
              class="chart-container">
              <div :ref="setChartRef(card.id)" class="chart-inner" />
            </div>
            <div v-else-if="card.data?.rows?.length" class="card-table">
              <table>
                <thead><tr><th v-for="c in card.data.columns" :key="c">{{ c }}</th></tr></thead>
                <tbody><tr v-for="(r,i) in (card.data.rows as Record<string,any>[]).slice(0,8)" :key="i"><td v-for="c in card.data.columns" :key="c">{{ (r as Record<string,any>)[c] }}</td></tr></tbody>
              </table>
            </div>
            <div v-else class="card-subtitle">{{ card.data?.total ?? '-' }} 条记录</div>
          </div>
        </div>
      </div>
    </div>

    <el-empty v-if="!loading && allCards.length === 0"
      description="暂无卡片，请在运营数据配置中添加" />
  </div>
  <div v-else style="padding:60px 0;text-align:center">
    <el-empty description="无权限访问运营数据，请联系管理员" />
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
  border-top: 3px solid #00603D;
  transition: box-shadow 0.2s;
}
.dash-card:hover { box-shadow: 0 2px 12px rgba(0,0,0,0.08); }
.card-header { display: flex; align-items: center; margin-bottom: 12px; }
.card-icon { font-size: 20px; margin-right: 8px; }
.card-title { font-size: 14px; color: #909399; }
.card-body { padding: 4px 0; }
.card-value { font-size: 36px; font-weight: 700; color: #303133; }
.card-unit { font-size: 14px; color: #909399; margin-left: 4px; }
.card-compare-inline { display: inline-flex; align-items: center; gap: 2px; font-size: 12px; font-weight: 600; margin-left: 6px; white-space: nowrap; }
.card-compare-inline.up { color: #10b981; }
.card-compare-inline.down { color: #ef4444; }
.card-compare-inline .arrow { font-size: 13px; }
.card-subtitle { font-size: 16px; color: #606266; }
.card-error { color: #f56c6c; font-size: 13px; }
.chart-container { position: relative; width: 100%; min-height: 260px; }
.chart-inner { position: absolute; inset: 0; }
.card-table { overflow: auto; }
.card-table table { width: 100%; border-collapse: collapse; font-size: 12px; }
.card-table th { background: #f5f7fa; padding: 6px 8px; text-align: left; border-bottom: 1px solid #ebeef5; white-space: nowrap; }
.card-table td { padding: 5px 8px; border-bottom: 1px solid #ebeef5; white-space: nowrap; max-width: 160px; overflow: hidden; text-overflow: ellipsis; }
</style>
