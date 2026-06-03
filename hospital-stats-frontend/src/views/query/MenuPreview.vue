<script setup lang="ts">
import { ref, onMounted } from 'vue';
import { useRouter } from 'vue-router';
import { ElMessage } from 'element-plus';
import { useAuthStore } from '../../stores/auth';
import { queryApi, type MenuItem } from '../../api/query';

const authStore = useAuthStore();

const router = useRouter();
const menus = ref<MenuItem[]>([]);

async function loadMenus() {
  try {
    const res = await queryApi.getMenus();
    menus.value = res.data;
  } catch {
    menus.value = [];
  }
}

function handleMenuClick(menu: MenuItem) {
  if (!menu.isEnabled) {
    ElMessage.warning('此菜单已禁用');
    return;
  }
  if (menu.queryConfigId) {
    router.push(`/query/view/${menu.queryConfigId}`);
  }
}

function hasQueryConfig(menu: MenuItem): boolean {
  return !!menu.queryConfigId;
}

onMounted(loadMenus);
</script>

<template>
  <div>
    <div style="margin-bottom: 16px">
      <span style="font-size: 16px; font-weight: 500">查询菜单</span>
      <span style="color: #909399; margin-left: 8px; font-size: 13px">
        点击菜单项进入查询页面
      </span>
    </div>

    <div style="background: white; padding: 24px; border-radius: 4px; min-height: 400px">
      <el-empty v-if="menus.length === 0" :description="authStore.isAdmin ? '暂无菜单，请先到菜单管理中创建' : '暂无菜单，请联系管理员分配'" />

      <div v-else>
        <div v-for="menu in menus" :key="menu.id" style="margin-bottom: 20px">
          <!-- Root menu -->
          <div :style="{ fontSize: '15px', fontWeight: 600, color: menu.isEnabled ? '#303133' : '#c0c4cc', padding: '8px 0',
            borderBottom: '1px solid #ebeef5', marginBottom: '12px', display: 'flex', alignItems: 'center', gap: '6px' }">
            <el-icon v-if="menu.icon" size="16"><component :is="menu.icon" /></el-icon>
            {{ menu.name }}
            <el-tag v-if="!menu.isEnabled" size="small" type="info">已禁用</el-tag>
          </div>

          <!-- Children as clickable cards -->
          <el-row :gutter="12">
            <el-col v-for="child in menu.children" :key="child.id"
              :span="6" style="margin-bottom: 12px">
              <div
                :class="{ 'menu-card': true, 'clickable': hasQueryConfig(child) && child.isEnabled, 'disabled-card': !child.isEnabled }"
                @click="handleMenuClick(child)">
                <div style="font-size: 14px; font-weight: 500; display: flex; align-items: center; gap: 4px">
                  <el-icon v-if="child.icon" size="14"><component :is="child.icon" /></el-icon>
                  {{ child.name }}
                </div>
                <div style="font-size: 12px; color: #909399; margin-top: 4px">
                  {{ child.queryConfigName || '目录' }}
                  <el-tag v-if="!child.isEnabled" size="small" type="info" style="margin-left: 4px">已禁用</el-tag>
                </div>
              </div>
            </el-col>
          </el-row>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.menu-card {
  background: #f5f7fa;
  padding: 16px;
  border-radius: 6px;
  border: 1px solid #ebeef5;
  transition: all 0.2s;
}
.menu-card.clickable {
  cursor: pointer;
  background: #ecf5ff;
  border-color: #d9ecff;
}
.menu-card.clickable:hover {
  background: #d9ecff;
  border-color: #409EFF;
  transform: translateY(-1px);
  box-shadow: 0 2px 8px rgba(64, 158, 255, 0.15);
}
.disabled-card { opacity: 0.5; cursor: not-allowed; }
</style>
