<script setup lang="ts">
import { ref } from 'vue';
import { useAuthStore } from '../stores/auth';
import { ElMessage } from 'element-plus';

const authStore = useAuthStore();
const username = ref('');
const password = ref('');
const loading = ref(false);

async function handleLogin() {
  if (!username.value || !password.value) {
    ElMessage.warning('请输入用户名和密码');
    return;
  }
  loading.value = true;
  try {
    await authStore.login(username.value, password.value);
    ElMessage.success('登录成功');
  } catch {
    ElMessage.error('用户名或密码错误');
  } finally {
    loading.value = false;
  }
}
</script>

<template>
  <div class="login-container">
    <div class="login-card">
      <h2>医院数据统计平台</h2>
      <p class="subtitle">Hospital Statistics Platform</p>
      <el-form @submit.prevent="handleLogin" label-width="0">
        <el-form-item>
          <el-input v-model="username" placeholder="用户名" size="large"
            autocomplete="username" />
        </el-form-item>
        <el-form-item>
          <el-input v-model="password" type="password" placeholder="密码"
            size="large" show-password autocomplete="current-password"
            @keyup.enter="handleLogin" />
        </el-form-item>
        <el-form-item>
          <el-button type="primary" size="large" :loading="loading"
            @click="handleLogin" style="width: 100%">
            登 录
          </el-button>
        </el-form-item>
      </el-form>
      <div class="login-footer">Design by 信息科 ZT</div>
    </div>
  </div>
</template>

<style scoped>
.login-container {
  display: flex;
  justify-content: center;
  align-items: center;
  height: 100vh;
  background: #0f172a;
  position: relative;
  overflow: hidden;
}
.login-container::before {
  content: '';
  position: absolute;
  top: -50%;
  left: -50%;
  width: 200%;
  height: 200%;
  background: radial-gradient(ellipse at 30% 20%, rgba(13, 148, 136, 0.12) 0%, transparent 60%),
              radial-gradient(ellipse at 70% 80%, rgba(45, 212, 191, 0.06) 0%, transparent 50%);
  pointer-events: none;
}
.login-card {
  width: 400px;
  padding: 44px 40px 40px;
  background: #ffffff;
  border-radius: 12px;
  box-shadow: 0 8px 40px rgba(0, 0, 0, 0.25);
  position: relative;
  z-index: 1;
}
.login-card h2 {
  text-align: center;
  margin: 0 0 6px 0;
  color: #0f172a;
  font-size: 22px;
  font-weight: 700;
  letter-spacing: 0.04em;
}
.subtitle {
  text-align: center;
  color: #94a3b8;
  font-size: 13px;
  margin: 0 0 36px 0;
  letter-spacing: 0.06em;
  text-transform: uppercase;
}
.login-footer {
  text-align: center;
  color: #cbd5e1;
  font-size: 11px;
  letter-spacing: 2px;
  margin-top: 28px;
}
</style>
