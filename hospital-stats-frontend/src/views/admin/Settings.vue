<script setup lang="ts">
import { ref, onMounted } from 'vue';
import { ElMessage, ElMessageBox } from 'element-plus';
import { settingsApi } from '../../api/settings';
import api from '../../api/index';

const loading = ref(false);
const saving = ref(false);
const form = ref<Record<string, string | number>>({
  QueryTimeoutSeconds: '120',
  MaxRowCount: '50000',
});
const licenseStatus = ref('');

async function load() {
  loading.value = true;
  try {
    const [sRes, lRes] = await Promise.all([
      settingsApi.getAll(),
      api.get('/license/status'),
    ]);
    form.value.QueryTimeoutSeconds = sRes.data.QueryTimeoutSeconds || '120';
    form.value.MaxRowCount = sRes.data.MaxRowCount || '50000';
    licenseStatus.value = lRes.data.message;
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
    });
    ElMessage.success('已保存，立即生效');
  } catch {
    ElMessage.error('保存失败');
  } finally {
    saving.value = false;
  }
}

async function resetLicense() {
  try {
    await ElMessageBox.confirm('确定要清除激活状态吗？清除后需要重新输入激活码。', '提示', { type: 'warning' });
    await api.post('/license/reset');
    ElMessage.success('已清除，请退出登录重新激活');
    const res = await api.get('/license/status');
    licenseStatus.value = res.data.message;
  } catch { /* cancelled */ }
}

onMounted(load);
</script>

<template>
  <div>
    <div style="display:flex;align-items:center;gap:16px;margin-bottom:20px">
      <span style="font-size:18px;font-weight:600">配置管理</span>
      <el-button type="primary" :loading="saving" @click="save">保存</el-button>
    </div>

    <div style="background:#fff;padding:24px;border-radius:8px;max-width:520px" v-loading="loading">
      <el-form label-width="160px">
        <el-form-item label="查询超时(秒)">
          <el-input-number v-model="form.QueryTimeoutSeconds" :min="10" :max="600" />
          <span style="color:#909399;font-size:12px;margin-left:8px">默认 120，超时抛错</span>
        </el-form-item>
        <el-form-item label="最大行数限制">
          <el-input-number v-model="form.MaxRowCount" :min="1000" :max="500000" :step="10000" />
          <span style="color:#909399;font-size:12px;margin-left:8px">超出弹窗提醒，默认 50000</span>
        </el-form-item>
      </el-form>
    </div>

    <div style="background:#fff;padding:24px;border-radius:8px;max-width:520px;margin-top:16px">
      <div style="display:flex;align-items:center;justify-content:space-between">
        <div>
          <div style="font-size:14px;font-weight:600;color:#1e293b;margin-bottom:4px">授权状态</div>
          <div style="font-size:13px;color:#64748b">{{ licenseStatus }}</div>
        </div>
        <el-button type="danger" plain size="small" @click="resetLicense">重新激活</el-button>
      </div>
    </div>
  </div>
</template>
