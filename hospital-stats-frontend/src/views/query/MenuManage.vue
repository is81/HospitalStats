<script setup lang="ts">
import { ref, onMounted } from 'vue';
import { ElMessage, ElMessageBox } from 'element-plus';
import { queryApi, type MenuItem, type MenuSave, type QueryConfigItem } from '../../api/query';

const menus = ref<MenuItem[]>([]);
const configs = ref<QueryConfigItem[]>([]);
const dialogVisible = ref(false);
const editingMenu = ref<MenuItem | null>(null);
const parentId = ref<number | null>(null);
const form = ref<MenuSave>({
  parentId: null,
  name: '',
  icon: '',
  sortOrder: 0,
  queryConfigId: null,
  isEnabled: true,
});

async function loadMenus() {
  const res = await queryApi.getMenus();
  menus.value = res.data;
}

async function loadConfigs() {
  const res = await queryApi.getConfigs();
  configs.value = res.data;
}

function openDialog(parent: number | null, menu?: MenuItem) {
  parentId.value = parent;
  if (menu) {
    editingMenu.value = menu;
    form.value = {
      parentId: menu.parentId,
      name: menu.name,
      icon: menu.icon || '',
      sortOrder: menu.sortOrder,
      queryConfigId: menu.queryConfigId,
      isEnabled: menu.isEnabled,
    };
  } else {
    editingMenu.value = null;
    form.value = {
      parentId: parent,
      name: '',
      icon: '',
      sortOrder: 0,
      queryConfigId: null,
      isEnabled: true,
    };
  }
  dialogVisible.value = true;
}

async function saveMenu() {
  if (!form.value.name) { ElMessage.warning('请输入菜单名称'); return; }
  try {
    if (editingMenu.value) {
      await queryApi.updateMenu(editingMenu.value.id, form.value);
      ElMessage.success('已更新');
    } else {
      await queryApi.createMenu(form.value);
      ElMessage.success('已创建');
    }
    dialogVisible.value = false;
    await loadMenus();
  } catch (e: any) {
    ElMessage.error(e?.response?.data?.message || '保存失败');
  }
}

async function deleteMenu(id: number) {
  try {
    await ElMessageBox.confirm('确定删除该菜单？子菜单将上移。', '确认', { type: 'warning' });
    await queryApi.deleteMenu(id);
    ElMessage.success('已删除');
    await loadMenus();
  } catch { /* cancelled */ }
}

// Icons
const icons = [
  'DataAnalysis', 'PieChart', 'TrendCharts', 'Document', 'Folder',
  'Setting', 'User', 'List', 'Grid', 'Histogram',
];

onMounted(() => {
  loadMenus();
  loadConfigs();
});
</script>

<template>
  <div>
    <div style="margin-bottom: 16px">
      <el-button type="primary" @click="openDialog(null)">新增根菜单</el-button>
    </div>

    <!-- Tree Table -->
    <el-table :data="menus" border stripe row-key="id" default-expand-all
      :row-class-name="({ row }: any) => row.parentId == null ? 'root-row' : ''"
      v-if="menus.length > 0">
      <el-table-column prop="name" label="菜单名称" min-width="200">
        <template #default="{ row }">
          <span style="display: inline-flex; align-items: center; gap: 6px; vertical-align: middle">
            <el-icon v-if="row.icon" size="16"><component :is="row.icon" /></el-icon>
            <el-icon v-else size="16"><Folder /></el-icon>
            {{ row.name }}
          </span>
        </template>
      </el-table-column>
      <el-table-column label="绑定配置" min-width="200">
        <template #default="{ row }">
          <span :style="{ color: row.queryConfigName ? '#303133' : '#c0c4cc' }">
            {{ row.queryConfigName || '未绑定（目录）' }}
          </span>
        </template>
      </el-table-column>
      <el-table-column prop="sortOrder" label="排序" width="80" align="center" />
      <el-table-column label="状态" width="80" align="center">
        <template #default="{ row }">
          <el-tag :type="row.isEnabled ? 'success' : 'info'" size="small">
            {{ row.isEnabled ? '启用' : '禁用' }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column label="操作" width="280">
        <template #default="{ row }">
          <el-button size="small" @click="openDialog(row.id)">添加子菜单</el-button>
          <el-button size="small" @click="openDialog(row.parentId, row)">编辑</el-button>
          <el-button size="small" type="danger" @click="deleteMenu(row.id)">删除</el-button>
        </template>
      </el-table-column>
    </el-table>
    <el-empty v-if="menus.length === 0" description="暂无菜单，点击上方按钮创建" />

    <!-- Dialog -->
    <el-dialog v-model="dialogVisible"
      :title="editingMenu ? '编辑菜单' : (parentId ? '添加子菜单' : '新增根菜单')"
      width="480px">
      <el-form :model="form" label-width="100px">
        <el-form-item label="菜单名称" required>
          <el-input v-model="form.name" placeholder="如：门诊统计" />
        </el-form-item>
        <el-form-item label="图标">
          <el-select v-model="form.icon" placeholder="选择图标" clearable style="width: 100%">
            <el-option v-for="icon in icons" :key="icon" :label="icon" :value="icon">
              <el-icon style="margin-right: 6px; vertical-align: middle">
                <component :is="icon" />
              </el-icon>
              <span>{{ icon }}</span>
            </el-option>
          </el-select>
        </el-form-item>
        <el-form-item label="绑定查询">
          <el-select v-model="form.queryConfigId" placeholder="目录无需绑定" clearable
            style="width: 100%">
            <el-option v-for="c in configs" :key="c.id" :label="c.name" :value="c.id" />
          </el-select>
        </el-form-item>
        <el-form-item label="排序">
          <el-input-number v-model="form.sortOrder" :min="0" />
        </el-form-item>
        <el-form-item label="启用">
          <el-switch v-model="form.isEnabled" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" @click="saveMenu">保存</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
:deep(.root-row) {
  background-color: #edf8f2;
}
</style>

