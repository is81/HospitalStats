<script setup lang="ts">
import { ref, onMounted } from 'vue';
import { ElMessage } from 'element-plus';
import { settingsApi } from '../../api/settings';

const loading = ref(false);
const saving = ref(false);
const form = ref<Record<string, string | number>>({
  QueryTimeoutSeconds: '120',
  MaxRowCount: '50000',
});

async function load() {
  loading.value = true;
  try {
    const res = await settingsApi.getAll();
    form.value.QueryTimeoutSeconds = res.data.QueryTimeoutSeconds || '120';
    form.value.MaxRowCount = res.data.MaxRowCount || '50000';
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
  </div>
</template>
