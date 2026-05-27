<script setup lang="ts">
import { useRoute } from 'vue-router';
import { useAuthStore } from '../stores/auth';

const route = useRoute();
const authStore = useAuthStore();

function handleLogout() {
  authStore.logout();
}
</script>

<template>
  <el-container style="height: 100vh">
    <el-aside width="220px" style="background: #304156">
      <div class="logo">
        <img src="/logo.png" class="logo-img" alt="logo" />
        <span>医院数据统计平台</span>
      </div>
      <el-menu
        :default-active="route.path"
        background-color="#304156"
        text-color="#bfcbd9"
        active-text-color="#409EFF"
        router
      >
        <!-- 仪表盘功能暂缓 -->
        <!--
        <el-menu-item index="/dashboard">
          <el-icon><Odometer /></el-icon>
          <span>仪表盘</span>
        </el-menu-item>
        -->
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
          <!-- 仪表盘配置暂缓 -->
          <!-- <el-menu-item index="/dashboard/config">仪表盘配置</el-menu-item> -->
          <el-menu-item index="/admin/users">用户管理</el-menu-item>
          <el-menu-item index="/admin/roles">角色管理</el-menu-item>
        </el-sub-menu>
      </el-menu>
    </el-aside>
    <el-container>
      <el-header style="background: white; border-bottom: 1px solid #e6e6e6;
        display: flex; align-items: center; justify-content: space-between">
        <span style="font-size: 16px; color: #303133">
          {{ route.meta.title || '' }}
        </span>
        <div>
          <span style="margin-right: 12px; color: #606266">
            {{ authStore.displayName }}
          </span>
          <el-button text @click="handleLogout">退出</el-button>
        </div>
      </el-header>
      <el-main style="background: #f0f2f5">
        <router-view />
      </el-main>
    </el-container>
  </el-container>
</template>

<style scoped>
.logo {
  padding: 16px 0;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 8px;
  color: white;
  font-size: 15px;
  font-weight: bold;
  border-bottom: 1px solid rgba(255,255,255,0.1);
}
.logo-img {
  width: 48px;
  height: 48px;
  border-radius: 8px;
  object-fit: contain;
  background: rgba(255,255,255,0.9);
  padding: 4px;
}
.el-menu {
  border-right: none;
}
</style>
