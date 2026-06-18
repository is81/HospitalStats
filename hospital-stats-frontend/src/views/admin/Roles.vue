<script setup lang="ts">
import { ref, onMounted } from 'vue';
import { ElMessage, ElMessageBox } from 'element-plus';
import { adminApi, type RoleInfo } from '../../api/admin';
import { queryApi, type MenuItem } from '../../api/query';

const roles = ref<RoleInfo[]>([]);
const menus = ref<MenuItem[]>([]);
const loading = ref(false);
const dialogVisible = ref(false);
const editingRole = ref<RoleInfo | null>(null);
const form = ref({ name: '', description: '', menuIds: [] as number[], dashboardAccess: false });

async function loadData() {
  loading.value = true;
  try {
    const [rRes, mRes] = await Promise.all([adminApi.getRoles(), queryApi.getMenus()]);
    roles.value = rRes.data;
    menus.value = mRes.data;
  } finally {
    loading.value = false;
  }
}

function collectAllMenuIds(items: typeof menus.value): number[] {
  const ids: number[] = [];
  for (const m of items) {
    ids.push(m.id);
    if (m.children.length > 0) ids.push(...collectAllMenuIds(m.children));
  }
  return ids;
}

function openDialog(role?: RoleInfo) {
  if (role) {
    editingRole.value = role;
    const menuIds = role.menuIds.length > 0 ? [...role.menuIds] : collectAllMenuIds(menus.value);
    form.value = { name: role.name, description: role.description || '', menuIds, dashboardAccess: role.dashboardAccess || false };
  } else {
    editingRole.value = null;
    form.value = { name: '', description: '', menuIds: [], dashboardAccess: false };
  }
  dialogVisible.value = true;
}

async function saveRole() {
  if (!form.value.name) { ElMessage.warning('请输入角色名'); return; }
  const isAdmin = editingRole.value?.name === 'admin' || form.value.name === 'admin';
  const payload = { ...form.value, menuIds: isAdmin ? [] : form.value.menuIds, dashboardAccess: isAdmin || form.value.dashboardAccess };
  try {
    if (editingRole.value) {
      await adminApi.updateRole(editingRole.value.id, payload);
      ElMessage.success('已更新');
    } else {
      await adminApi.createRole(payload);
      ElMessage.success('已创建');
    }
    dialogVisible.value = false;
    await loadData();
  } catch (e: any) {
    ElMessage.error(e.response?.data?.message || '操作失败');
  }
}

async function deleteRole(id: number) {
  try {
    await ElMessageBox.confirm('确定删除该角色？', '确认', { type: 'warning' });
    await adminApi.deleteRole(id);
    ElMessage.success('已删除');
    await loadData();
  } catch { /* cancelled */ }
}

function getMenuName(menuId: number): string {
  function find(items: typeof menus.value): string {
    for (const m of items) {
      if (m.id === menuId) return m.name;
      if (m.children.length > 0) {
        const found = find(m.children);
        if (found) return found;
      }
    }
    return '';
  }
  return find(menus.value);
}

onMounted(loadData);
</script>

<template>
  <div>
    <div style="margin-bottom: 16px">
      <el-button type="primary" @click="openDialog()">新增角色</el-button>
    </div>

    <el-table :data="roles" v-loading="loading" border stripe>
      <el-table-column prop="name" label="角色名" width="140" />
      <el-table-column prop="description" label="描述" min-width="150" />
      <el-table-column label="运营数据" width="100">
        <template #default="{ row }">
          <el-tag :type="row.name === 'admin' || row.dashboardAccess ? 'success' : 'info'" size="small">
            {{ row.name === 'admin' || row.dashboardAccess ? '可访问' : '不可访问' }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column label="菜单权限" min-width="300">
        <template #default="{ row }">
          <template v-if="row.name === 'admin'">
            <el-tag type="success" size="small">全部菜单</el-tag>
          </template>
          <template v-else>
            <el-tag v-for="mid in row.menuIds" :key="mid" size="small" style="margin:2px">
              {{ getMenuName(mid) }}
            </el-tag>
            <span v-if="!row.menuIds.length" style="color:#c0c4cc">未分配</span>
          </template>
        </template>
      </el-table-column>
      <el-table-column label="操作" width="160">
        <template #default="{ row }">
          <el-button size="small" @click="openDialog(row)">编辑</el-button>
          <el-button size="small" type="danger" @click="deleteRole(row.id)"
            v-if="row.name !== 'admin'">删除</el-button>
        </template>
      </el-table-column>
    </el-table>

    <el-dialog v-model="dialogVisible"
      :title="editingRole ? '编辑角色' : '新增角色'" width="540px">
      <el-form :model="form" label-width="80px">
        <el-form-item label="名称" required>
          <el-input v-model="form.name" :disabled="editingRole?.name === 'admin'" />
        </el-form-item>
        <el-form-item label="描述">
          <el-input v-model="form.description" />
        </el-form-item>
        <el-form-item label="运营数据">
          <el-switch v-model="form.dashboardAccess" :disabled="editingRole?.name === 'admin' || form.name === 'admin'" />
          <span style="color:#909399;font-size:12px;margin-left:8px">允许此角色用户查看运营数据</span>
        </el-form-item>
        <el-form-item label="菜单权限">
          <el-tree
            :data="menus"
            show-checkbox
            node-key="id"
            :props="{ label: 'name', children: 'children' }"
            :default-checked-keys="form.menuIds"
            @check="(_node: any, checked: any) => { form.menuIds = checked.checkedKeys as number[]; }"
            default-expand-all
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" @click="saveRole">保存</el-button>
      </template>
    </el-dialog>
  </div>
</template>
