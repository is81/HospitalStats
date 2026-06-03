<script setup lang="ts">
import { ref } from 'vue';
import { useRoute } from 'vue-router';
import { ElMessage } from 'element-plus';
import { useAuthStore } from '../stores/auth';
import { authApi } from '../api/auth';

const route = useRoute();
const authStore = useAuthStore();

function handleLogout() {
  authStore.logout();
}

// user profile dialog
const showProfileDialog = ref(false);
const showVersion = ref(false);
const pwdForm = ref({ oldPassword: '', newPassword: '', confirmPassword: '' });
const pwdSaving = ref(false);

function openProfile() {
  pwdForm.value = { oldPassword: '', newPassword: '', confirmPassword: '' };
  showProfileDialog.value = true;
}

async function handleChangePassword() {
  if (!pwdForm.value.oldPassword || !pwdForm.value.newPassword) {
    ElMessage.warning('请填写完整');
    return;
  }
  if (pwdForm.value.newPassword.length < 6) {
    ElMessage.warning('新密码至少6位');
    return;
  }
  if (pwdForm.value.newPassword !== pwdForm.value.confirmPassword) {
    ElMessage.warning('两次输入的新密码不一致');
    return;
  }
  pwdSaving.value = true;
  try {
    await authApi.changePassword(pwdForm.value.oldPassword, pwdForm.value.newPassword);
    ElMessage.success('密码修改成功，请重新登录');
    showProfileDialog.value = false;
    authStore.logout();
  } catch (e: any) {
    ElMessage.error(e.response?.data?.message || '修改失败');
  } finally {
    pwdSaving.value = false;
  }
}
</script>

<template>
  <el-container style="height: 100vh">
    <el-aside width="220px" class="app-sidebar">
      <div class="logo">
        <img src="/logo.png" class="logo-img" alt="logo" />
        <div class="logo-text">
          <span class="logo-title">医院数据统计平台</span>
          <span class="logo-subtitle">Hospital Statistics Platform</span>
        </div>
      </div>
      <el-menu
        :default-active="route.path"
        background-color="#0f172a"
        text-color="#94a3b8"
        active-text-color="#2dd4bf"
        router
      >
        <el-menu-item index="/dashboard" v-if="authStore.isAdmin">
          <el-icon><Odometer /></el-icon>
          <span>仪表盘</span>
        </el-menu-item>
        <el-menu-item index="/datasources" v-if="authStore.isAdmin">
          <el-icon><Monitor /></el-icon>
          <span>数据源管理</span>
        </el-menu-item>
        <el-menu-item index="/meta" v-if="authStore.isAdmin">
          <el-icon><Grid /></el-icon>
          <span>元数据管理</span>
        </el-menu-item>
        <el-sub-menu index="/query" v-if="authStore.isAdmin">
          <template #title>
            <el-icon><DataAnalysis /></el-icon>
            <span>查询管理</span>
          </template>
          <el-menu-item index="/query/configs">查询配置</el-menu-item>
          <el-menu-item index="/query/menus">菜单管理</el-menu-item>
          <el-menu-item index="/query/preview">菜单预览</el-menu-item>
        </el-sub-menu>
        <el-menu-item index="/query/preview" v-if="!authStore.isAdmin">
          <el-icon><DataAnalysis /></el-icon>
          <span>数据查询</span>
        </el-menu-item>
        <el-sub-menu index="/system" v-if="authStore.isAdmin">
          <template #title>
            <el-icon><Setting /></el-icon>
            <span>系统管理</span>
          </template>
          <el-menu-item index="/dashboard/config">仪表盘配置</el-menu-item>
          <el-menu-item index="/admin/users">用户管理</el-menu-item>
          <el-menu-item index="/admin/roles">角色管理</el-menu-item>
          <el-menu-item index="/admin/settings">配置管理</el-menu-item>
        </el-sub-menu>
      </el-menu>
      <div class="sidebar-footer">
        Design by 信息科 ZT |
        <a href="#" @click.prevent="showVersion = true" class="version-link">版本</a>
      </div>
    </el-aside>
    <el-container>
      <el-header class="app-header">
        <span class="header-title">{{ route.meta.title || '' }}</span>
        <div class="header-right">
          <span class="header-user" @click="openProfile">{{ authStore.displayName || '用户' }}</span>
          <el-button text @click="handleLogout">退出</el-button>
        </div>
      </el-header>
      <el-main class="app-main">
        <router-view />
      </el-main>
    </el-container>

    <!-- User Profile Dialog -->
    <el-dialog v-model="showProfileDialog" title="个人信息" width="420px" :close-on-click-modal="false">
      <el-form label-width="80px">
        <el-form-item label="用户名">
          <el-input :model-value="authStore.displayName" disabled />
        </el-form-item>
        <el-divider />
        <el-form-item label="原密码">
          <el-input v-model="pwdForm.oldPassword" type="password" show-password
            placeholder="输入原密码" />
        </el-form-item>
        <el-form-item label="新密码">
          <el-input v-model="pwdForm.newPassword" type="password" show-password
            placeholder="至少6位" />
        </el-form-item>
        <el-form-item label="确认密码">
          <el-input v-model="pwdForm.confirmPassword" type="password" show-password
            placeholder="再次输入新密码" @keyup.enter="handleChangePassword" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="showProfileDialog = false">取消</el-button>
        <el-button type="primary" :loading="pwdSaving" @click="handleChangePassword">修改密码</el-button>
      </template>
    </el-dialog>
  </el-container>
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
.app-sidebar {
  background: #0f172a !important;
  position: relative;
  overflow: hidden;
}
.app-sidebar::after {
  content: '';
  position: absolute;
  top: 0; left: 0; right: 0; bottom: 0;
  background: radial-gradient(ellipse at 50% 0%, rgba(13, 148, 136, 0.08) 0%, transparent 70%);
  pointer-events: none;
}
.logo {
  padding: 18px 16px 14px;
  display: flex;
  align-items: center;
  gap: 12px;
  border-bottom: 1px solid rgba(148, 163, 184, 0.12);
  position: relative;
  z-index: 1;
}
.logo-img {
  width: 40px;
  height: 40px;
  border-radius: 10px;
  object-fit: contain;
  background: rgba(255, 255, 255, 0.95);
  padding: 4px;
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.15);
  flex-shrink: 0;
}
.logo-text {
  display: flex;
  flex-direction: column;
  gap: 2px;
  overflow: hidden;
}
.logo-title {
  color: #f1f5f9;
  font-size: 14px;
  font-weight: 600;
  letter-spacing: 0.04em;
  line-height: 1.3;
}
.logo-subtitle {
  color: #64748b;
  font-size: 10px;
  letter-spacing: 0.06em;
  text-transform: uppercase;
  line-height: 1.2;
}
:deep(.el-menu) {
  border-right: none;
}
:deep(.el-menu-item) {
  transition: all 0.2s ease;
}
:deep(.el-menu-item:hover) {
  background-color: rgba(45, 212, 191, 0.08) !important;
}
:deep(.el-sub-menu__title:hover) {
  background-color: rgba(45, 212, 191, 0.08) !important;
}
.sidebar-footer {
  position: absolute;
  bottom: 14px;
  width: 100%;
  text-align: center;
  color: rgba(148, 163, 184, 0.4);
  font-size: 10px;
  letter-spacing: 2px;
  z-index: 1;
}
.version-link { color: rgba(148,163,184,0.5); text-decoration: none; }
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

.app-header {
  background: #ffffff;
  border-bottom: 1px solid #e2e8f0;
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0 24px;
  box-shadow: 0 1px 3px rgba(15, 23, 42, 0.04);
}
.header-title {
  font-size: 15px;
  font-weight: 600;
  color: #1e293b;
  letter-spacing: 0.02em;
}
.header-right {
  display: flex;
  align-items: center;
}
.header-user {
  margin-right: 16px;
  color: #475569;
  font-size: 13px;
  cursor: pointer;
  padding: 4px 8px;
  border-radius: 4px;
  transition: all 0.2s ease;
}
.header-user:hover {
  color: #00603D;
  background: rgba(0, 96, 61, 0.06);
}

.app-main {
  background: #f1f5f9;
  padding: 20px;
}
</style>
