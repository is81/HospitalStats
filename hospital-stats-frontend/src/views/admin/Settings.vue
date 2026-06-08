<script setup lang="ts">
import { ref, onMounted } from 'vue';
import { ElMessage } from 'element-plus';
import { settingsApi } from '../../api/settings';

const loading = ref(false);
const saving = ref(false);
const form = ref<Record<string, string | number>>({
  QueryTimeoutSeconds: 120,
  MaxRowCount: 50000,
  DashboardDateColumns: 'VISIT_DATE,BILLING_DATE_TIME,DISCHARGE_DATE_TIME,PRESC_DATE',
  DashboardDefaultDays: 1,
  TrendDefaultDays: 30,
  HistoryLimit: 50000,
});

async function load() {
  loading.value = true;
  try {
    const res = await settingsApi.getAll();
    form.value.QueryTimeoutSeconds = Number(res.data.QueryTimeoutSeconds) || 120;
    form.value.MaxRowCount = Number(res.data.MaxRowCount) || 50000;
    form.value.DashboardDateColumns = res.data.DashboardDateColumns ||
      'VISIT_DATE,BILLING_DATE_TIME,DISCHARGE_DATE_TIME,PRESC_DATE';
    form.value.DashboardDefaultDays = Number(res.data.DashboardDefaultDays || '1');
    form.value.TrendDefaultDays = Number(res.data.TrendDefaultDays || '30');
    form.value.HistoryLimit = Number(res.data.HistoryLimit || '50000');
  } finally {
    loading.value = false;
  }
}

async function save() {
  saving.value = true;
  try {
    await settingsApi.update({
      QueryTimeoutSeconds: String(form.value.QueryTimeoutSeconds),
      MaxRowCount: String(form.value.MaxRowCount),
      DashboardDateColumns: String(form.value.DashboardDateColumns),
      DashboardDefaultDays: String(form.value.DashboardDefaultDays),
      TrendDefaultDays: String(form.value.TrendDefaultDays),
      HistoryLimit: String(form.value.HistoryLimit),
    });
    ElMessage.success('已保存，立即生效');
  } catch {
    ElMessage.error('保存失败');
  } finally {
    saving.value = false;
  }
}

onMounted(load);
</script>

<template>
  <div>
    <div style="margin-bottom:20px">
      <span style="font-size:18px;font-weight:600">配置管理</span>
    </div>

    <div style="background:#fff;padding:24px;border-radius:8px;max-width:600px" v-loading="loading">
      <el-form label-width="180px">
        <el-form-item label="查询超时(秒)">
          <el-input-number v-model="form.QueryTimeoutSeconds" :min="10" :max="600" />
          <span style="color:#909399;font-size:12px;margin-left:8px">默认 120</span>
        </el-form-item>
        <el-form-item label="最大行数限制">
          <el-input-number v-model="form.MaxRowCount" :min="1000" :max="500000" :step="10000" />
          <span style="color:#909399;font-size:12px;margin-left:8px">超出弹窗提醒，默认 50000</span>
        </el-form-item>
        <el-divider />
        <el-form-item label="仪表盘日期列">
          <el-input v-model="form.DashboardDateColumns" placeholder="逗号分隔" />
          <span style="color:#909399;font-size:12px;margin-left:8px">用于匹配日期筛选器</span>
        </el-form-item>
        <el-form-item label="仪表盘默认起始日">
          <el-input-number v-model="form.DashboardDefaultDays" :min="0" :max="365" />
          <span style="color:#909399;font-size:12px;margin-left:8px">核心指标标签页，向前推 N 天</span>
        </el-form-item>
        <el-form-item label="趋势对比默认起始日">
          <el-input-number v-model="form.TrendDefaultDays" :min="1" :max="365" />
          <span style="color:#909399;font-size:12px;margin-left:8px">趋势对比标签页，向前推 N 天，默认 30</span>
        </el-form-item>
        <el-form-item label="查询历史保留条数">
          <el-input-number v-model="form.HistoryLimit" :min="1000" :max="200000" :step="10000" />
          <span style="color:#909399;font-size:12px;margin-left:8px">超出自动删除旧记录，默认 50000</span>
        </el-form-item>
      </el-form>
    </div>

    <div style="max-width:600px;display:flex;justify-content:flex-end;margin-top:12px">
      <el-button type="primary" :loading="saving" @click="save">保存</el-button>
    </div>
  </div>
</template>
