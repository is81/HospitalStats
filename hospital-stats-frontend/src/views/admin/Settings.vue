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
  DashboardUnitOptions: '人次,人,元,万元,张,%,例,天,次,件',
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
    form.value.DashboardUnitOptions = res.data.DashboardUnitOptions ||
      '人次,人,元,万元,张,%,例,天,次,件';
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
      DashboardUnitOptions: String(form.value.DashboardUnitOptions),
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
    <div v-loading="loading" style="display:flex;flex-direction:column;gap:16px;max-width:600px">

      <!-- 查询设置 -->
      <div style="background:#fff;padding:20px 24px;border-radius:8px">
        <div style="font-size:14px;font-weight:600;color:#303133;margin-bottom:12px">查询设置</div>
        <el-form label-width="160px">
          <el-form-item label="查询超时(秒)">
            <el-input-number v-model="form.QueryTimeoutSeconds" :min="10" :max="600" size="small" />
            <span style="color:#909399;font-size:12px;margin-left:8px">默认 120</span>
          </el-form-item>
          <el-form-item label="最大行数限制">
            <el-input-number v-model="form.MaxRowCount" :min="1000" :max="500000" :step="10000" size="small" />
            <span style="color:#909399;font-size:12px;margin-left:8px">超出弹窗提醒，默认 50000</span>
          </el-form-item>
        </el-form>
      </div>

      <!-- 运营数据设置 -->
      <div style="background:#fff;padding:20px 24px;border-radius:8px">
        <div style="font-size:14px;font-weight:600;color:#303133;margin-bottom:12px">运营数据设置</div>
        <el-form label-width="160px">
          <el-form-item label="日期列匹配">
            <el-input v-model="form.DashboardDateColumns" placeholder="逗号分隔" size="small" style="width:340px" />
            <span style="color:#909399;font-size:12px;margin-left:8px">用于匹配筛选器</span>
          </el-form-item>
          <el-form-item label="卡片单位选项">
            <el-input v-model="form.DashboardUnitOptions" placeholder="逗号分隔" size="small" style="width:340px" />
            <span style="color:#909399;font-size:12px;margin-left:8px">逗号分隔可选单位</span>
          </el-form-item>
        </el-form>
      </div>

      <!-- 历史设置 -->
      <div style="background:#fff;padding:20px 24px;border-radius:8px">
        <div style="font-size:14px;font-weight:600;color:#303133;margin-bottom:12px">查询历史</div>
        <el-form label-width="160px">
          <el-form-item label="保留条数">
            <el-input-number v-model="form.HistoryLimit" :min="1000" :max="200000" :step="10000" size="small" />
            <span style="color:#909399;font-size:12px;margin-left:8px">超出自动删除旧记录，默认 50000</span>
          </el-form-item>
        </el-form>
      </div>

    </div>

    <div style="max-width:600px;display:flex;justify-content:flex-end;margin-top:12px">
      <el-button type="primary" :loading="saving" @click="save">保存</el-button>
    </div>
  </div>
</template>
