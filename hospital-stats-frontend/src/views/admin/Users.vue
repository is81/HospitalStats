<script setup lang="ts">
import { ref, onMounted } from 'vue';
import { ElMessage, ElMessageBox } from 'element-plus';
import { adminApi, type UserInfo, type RoleInfo } from '../../api/admin';

const users = ref<UserInfo[]>([]);
const roles = ref<RoleInfo[]>([]);
const deptOptions = ref<string[]>([]);
const loading = ref(false);
const dialogVisible = ref(false);
const editingUser = ref<UserInfo | null>(null);
const form = ref({
  username: '',
  password: '',
  displayName: '',
  deptName: '',
  roleIds: [] as number[],
  isEnabled: true,
});

async function loadUsers() {
  loading.value = true;
  try {
    const [uRes, rRes] = await Promise.all([adminApi.getUsers(), adminApi.getRoles()]);
    users.value = uRes.data;
    roles.value = rRes.data;
    try {
      const dRes = await adminApi.getDeptOptions();
      deptOptions.value = dRes.data;
    } catch { /* dept options optional */ }
  } finally {
    loading.value = false;
  }
}

function openDialog(user?: UserInfo) {
  if (user) {
    editingUser.value = user;
    form.value = { username: user.username, password: '', displayName: user.displayName || '', deptName: user.deptName || '', roleIds: [...user.roleIds], isEnabled: user.isEnabled };
  } else {
    editingUser.value = null;
    form.value = { username: '', password: '', displayName: '', deptName: '', roleIds: [], isEnabled: true };
  }
  dialogVisible.value = true;
}

async function saveUser() {
  if (!editingUser.value && (!form.value.username || !form.value.password)) {
    ElMessage.warning('请填写用户名和密码'); return;
  }
  try {
    if (editingUser.value) {
      await adminApi.updateUser(editingUser.value.id, {
        displayName: form.value.displayName,
        deptName: form.value.deptName || undefined,
        password: form.value.password || undefined,
        isEnabled: form.value.isEnabled,
        roleIds: form.value.roleIds,
      });
      ElMessage.success('已更新');
    } else {
      await adminApi.createUser(form.value);
      ElMessage.success('已创建');
    }
    dialogVisible.value = false;
    await loadUsers();
  } catch (e: any) {
    ElMessage.error(e.response?.data?.message || '操作失败');
  }
}

async function deleteUser(id: number) {
  try {
    await ElMessageBox.confirm('确定删除该用户？', '确认', { type: 'warning' });
    await adminApi.deleteUser(id);
    ElMessage.success('已删除');
    await loadUsers();
  } catch { /* cancelled */ }
}

onMounted(loadUsers);
</script>

<template>
  <div>
    <div style="margin-bottom: 16px">
      <el-button type="primary" @click="openDialog()">新增用户</el-button>
    </div>

    <el-table :data="users" v-loading="loading" border stripe>
      <el-table-column prop="username" label="用户名" width="140" />
      <el-table-column prop="displayName" label="显示名" min-width="120" />
      <el-table-column prop="deptName" label="科室" width="100" />
      <el-table-column label="角色" min-width="200">
        <template #default="{ row }">
          <el-tag v-for="r in row.roles" :key="r" size="small" style="margin-right:4px">{{ r }}</el-tag>
          <span v-if="!row.roles.length" style="color:#c0c4cc">未分配</span>
        </template>
      </el-table-column>
      <el-table-column label="状态" width="80">
        <template #default="{ row }">
          <el-tag :type="row.isEnabled ? 'success' : 'info'" size="small">
            {{ row.isEnabled ? '启用' : '禁用' }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column label="操作" width="160">
        <template #default="{ row }">
          <el-button size="small" @click="openDialog(row)">编辑</el-button>
          <el-button size="small" type="danger" @click="deleteUser(row.id)"
            v-if="row.username !== 'admin'">删除</el-button>
        </template>
      </el-table-column>
    </el-table>

    <el-dialog v-model="dialogVisible"
      :title="editingUser ? '编辑用户' : '新增用户'" width="460px">
      <el-form :model="form" label-width="80px">
        <el-form-item label="用户名" required>
          <el-input v-model="form.username" :disabled="!!editingUser" />
        </el-form-item>
        <el-form-item label="密码" :required="!editingUser">
          <el-input v-model="form.password" type="password"
            :placeholder="editingUser ? '留空不修改' : '请输入密码'" show-password />
        </el-form-item>
        <el-form-item label="显示名">
          <el-input v-model="form.displayName" />
        </el-form-item>
        <el-form-item label="科室">
          <el-select v-model="form.deptName" placeholder="选择科室" clearable filterable style="width:100%">
            <el-option v-for="d in deptOptions" :key="d" :label="d" :value="d" />
          </el-select>
        </el-form-item>
        <el-form-item label="角色">
          <el-select v-model="form.roleIds" multiple placeholder="选择角色" style="width:100%">
            <el-option v-for="r in roles" :key="r.id" :label="r.name" :value="r.id" />
          </el-select>
        </el-form-item>
        <el-form-item label="启用">
          <el-switch v-model="form.isEnabled" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" @click="saveUser">保存</el-button>
      </template>
    </el-dialog>
  </div>
</template>
