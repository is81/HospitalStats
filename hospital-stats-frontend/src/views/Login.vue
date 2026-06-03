<script setup lang="ts">
import { ref } from 'vue';
import { useAuthStore } from '../stores/auth';
import { ElMessage } from 'element-plus';

const authStore = useAuthStore();
const username = ref('');
const password = ref('');
const loading = ref(false);
const showVersion = ref(false);

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
      <div class="login-header">
        <img src="/logo.png" alt="logo" class="logo-img" />
        <div class="login-title">
          <h2>医院数据统计平台</h2>
          <p class="subtitle">Hospital Statistics Platform</p>
        </div>
      </div>
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
      <div class="login-footer">
        Design by 信息科 ZT |
        <a href="#" @click.prevent="showVersion = true" class="version-link">版本</a>
      </div>
    </div>
  </div>
  <el-dialog v-model="showVersion" title="版本信息" width="500px" :close-on-click-modal="true">
    <div class="version-content">
      <div class="version-block">
        <div class="version-badge">v2.0</div>
        <div class="version-date">2026-06</div>
        <ul>
          <li>UNION 复杂查询全链路（导入→分支筛选注入→中文别名安全化→hex编码）</li>
          <li>Oracle US7ASCII 字符集完整适配（列数据/标识符/行内字面量三层防护）</li>
          <li>筛选条件按 UNION 分支独立注入</li>
          <li>仪表盘日期筛选栏 + 操作符智能匹配</li>
          <li>基于角色的菜单权限控制</li>
          <li>IN 多值参数独立绑定</li>
          <li>浏览器兼容（IE 2018+ polyfills）</li>
        </ul>
      </div>
      <div class="version-block">
        <div class="version-badge v1">v1.0</div>
        <div class="version-date">2026-05</div>
        <ul>
          <li>基础查询平台（Vue 3 + .NET 8 + Oracle 10g/11g）</li>
          <li>JWT 认证 + BCrypt 密码哈希</li>
          <li>数据源管理（AES-CBC 加密连接串）</li>
          <li>元数据扫描（Oracle Schema 自动发现）</li>
          <li>仪表盘 8 卡片 + ECharts 图表</li>
          <li>查询结果 Excel 导出</li>
          <li>SQLite 配置库自动备份</li>
          <li>12 种筛选操作符 + RawSql 导入解析</li>
          <li>上下文筛选器（DeptName / UserId）</li>
        </ul>
      </div>
    </div>
  </el-dialog>
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
.login-header { display: flex; align-items: center; justify-content: center; gap: 18px; margin-bottom: 20px; }
.login-header .logo-img { width: 64px; height: 64px; border-radius: 12px; flex-shrink: 0; }
.login-title h2 {
  margin: 0 0 2px 0;
  color: #0f172a;
  font-size: 22px;
  font-weight: 700;
  letter-spacing: 0.04em;
  text-align: center;
}
.subtitle {
  color: #94a3b8;
  font-size: 11px;
  margin: 0;
  letter-spacing: 0.06em;
  text-transform: uppercase;
  text-align: center;
}
.login-footer {
  text-align: center;
  color: #cbd5e1;
  font-size: 11px;
  letter-spacing: 2px;
  margin-top: 28px;
}
.version-link { color: #94a3b8; text-decoration: none; }
.version-link:hover { color: #2dd4bf; }
.version-content { display: flex; flex-direction: column; gap: 24px; padding: 8px 16px 4px; }
.version-block { position: relative; padding-left: 20px; border-left: 2px solid #e2e8f0; }
.version-badge {
  display: inline-block; padding: 2px 12px; border-radius: 12px;
  background: linear-gradient(135deg, #00603D, #1a7d54); color: #fff;
  font-size: 13px; font-weight: 700; letter-spacing: 0.04em;
}
.version-badge.v1 { background: linear-gradient(135deg, #94a3b8, #64748b); }
.version-date { display: inline-block; margin-left: 8px; font-size: 12px; color: #94a3b8; }
.version-block ul { padding-left: 16px; margin: 10px 0 0; }
.version-block li { line-height: 2; font-size: 13px; color: #475569; }
.version-block li::marker { color: #00603D; }
</style>
