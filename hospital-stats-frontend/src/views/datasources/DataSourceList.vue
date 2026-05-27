<script setup lang="ts">
import { ref, onMounted } from 'vue';
import { ElMessage, ElMessageBox } from 'element-plus';
import { dataSourcesApi, type DataSourceItem, type DataSourceForm } from '../../api/datasources';

const loading = ref(false);
const list = ref<DataSourceItem[]>([]);
const dialogVisible = ref(false);
const editingItem = ref<DataSourceItem | null>(null);
const form = ref<DataSourceForm>({
  name: '',
  dbType: 'Oracle',
  connectionString: '',
  schema: '',
  charSetOverride: '',
});
const testResult = ref<{ success: boolean; message: string; tableCount: number | null; dbVersion: string | null; charSet: string | null; } | null>(null);
const testing = ref(false);

async function loadList() {
  loading.value = true;
  try {
    const res = await dataSourcesApi.getAll();
    list.value = res.data;
  } finally {
    loading.value = false;
  }
}

function openDialog(item?: DataSourceItem) {
  testResult.value = null;
  if (item) {
    editingItem.value = item;
    form.value = {
      name: item.name,
      dbType: item.dbType,
      connectionString: '',
      schema: item.schema || '',
      charSetOverride: item.charSetOverride || '',
    };
  } else {
    editingItem.value = null;
    form.value = { name: '', dbType: 'Oracle', connectionString: '', schema: '', charSetOverride: '' };
  }
  dialogVisible.value = true;
}

async function handleSave() {
  if (!form.value.name || !form.value.connectionString) {
    ElMessage.warning('请填写名称和连接字符串');
    return;
  }
  try {
    if (editingItem.value) {
      await dataSourcesApi.update(editingItem.value.id, {
        ...form.value,
        isEnabled: editingItem.value.isEnabled,
      });
      ElMessage.success('更新成功');
    } else {
      await dataSourcesApi.create(form.value);
      ElMessage.success('创建成功');
    }
    dialogVisible.value = false;
    await loadList();
  } catch {
    ElMessage.error('操作失败');
  }
}

async function handleDelete(id: number, name: string) {
  try {
    await ElMessageBox.confirm(`确定删除数据源 "${name}" 吗？`, '确认', {
      confirmButtonText: '删除',
      cancelButtonText: '取消',
      type: 'warning',
    });
    await dataSourcesApi.delete(id);
    ElMessage.success('已删除');
    await loadList();
  } catch {
    // cancelled
  }
}

async function handleTestConnection() {
  if (!form.value.connectionString) {
    ElMessage.warning('请先填写连接字符串');
    return;
  }
  testing.value = true;
  try {
    const res = await dataSourcesApi.testConnectionString(form.value.connectionString);
    testResult.value = res.data;
    if (res.data.success) {
      ElMessage.success('连接成功');
    } else {
      ElMessage.error(res.data.message);
    }
  } catch {
    ElMessage.error('测试失败');
  } finally {
    testing.value = false;
  }
}

async function handleTestSaved(id: number) {
  testing.value = true;
  try {
    const res = await dataSourcesApi.testConnection(id);
    testResult.value = res.data;
    if (res.data.success) {
      ElMessage.success('连接成功');
    } else {
      ElMessage.error(res.data.message);
    }
  } catch {
    ElMessage.error('测试失败');
  } finally {
    testing.value = false;
  }
}

onMounted(loadList);
</script>

<template>
  <div>
    <div style="margin-bottom: 16px">
      <el-button type="primary" @click="openDialog()">新增数据源</el-button>
    </div>

    <el-table :data="list" v-loading="loading" border stripe style="width: 100%">
      <el-table-column prop="id" label="ID" width="60" />
      <el-table-column prop="name" label="名称" min-width="150" />
      <el-table-column prop="dbType" label="数据库类型" width="100" />
      <el-table-column prop="schema" label="Schema" width="120" />
      <el-table-column prop="charSetInfo" label="字符集" min-width="200" show-overflow-tooltip />
      <el-table-column label="状态" width="80">
        <template #default="{ row }">
          <el-tag :type="row.isEnabled ? 'success' : 'info'" size="small">
            {{ row.isEnabled ? '启用' : '禁用' }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column prop="updatedAt" label="更新时间" width="170" />
      <el-table-column label="操作" width="260" fixed="right">
        <template #default="{ row }">
          <el-button size="small" @click="handleTestSaved(row.id)"
            :loading="testing">测试</el-button>
          <el-button size="small" @click="openDialog(row)">编辑</el-button>
          <el-button size="small" type="danger"
            @click="handleDelete(row.id, row.name)">删除</el-button>
        </template>
      </el-table-column>
    </el-table>

    <!-- 新增/编辑弹窗 -->
    <el-dialog v-model="dialogVisible"
      :title="editingItem ? '编辑数据源' : '新增数据源'"
      width="560px" destroy-on-close>
      <el-form :model="form" label-width="110px">
        <el-form-item label="名称" required>
          <el-input v-model="form.name" placeholder="如：HIS生产库" />
        </el-form-item>
        <el-form-item label="数据库类型" required>
          <el-select v-model="form.dbType" style="width: 100%">
            <el-option label="Oracle" value="Oracle" />
            <el-option label="SQL Server" value="SqlServer" />
            <el-option label="MySQL" value="MySQL" />
          </el-select>
        </el-form-item>
        <el-form-item label="连接字符串" required>
          <el-input v-model="form.connectionString" type="textarea" :rows="3"
            placeholder="User Id=hospital;Password=xxx;Data Source=localhost:1521/orcl" />
        </el-form-item>
        <el-form-item label="Schema">
          <el-input v-model="form.schema" placeholder="如：HOSPITAL" />
        </el-form-item>
        <el-form-item label="字符集覆盖">
          <el-select v-model="form.charSetOverride" placeholder="自动检测（推荐）" clearable
            style="width: 100%">
            <el-option label="自动检测" value="" />
            <el-option label="GBK" value="GBK" />
            <el-option label="UTF-8" value="UTF-8" />
          </el-select>
        </el-form-item>
        <el-form-item>
          <el-button @click="handleTestConnection" :loading="testing"
            type="success">测试连接</el-button>
        </el-form-item>
      </el-form>

      <!-- 测试结果 -->
      <div v-if="testResult" style="margin-top: 16px">
        <el-alert :title="testResult.message" :type="testResult.success ? 'success' : 'error'"
          :closable="false" show-icon />
        <el-descriptions v-if="testResult.success" :column="2" border
          style="margin-top: 12px" size="small">
          <el-descriptions-item label="数据库版本">{{ testResult.dbVersion }}</el-descriptions-item>
          <el-descriptions-item label="表数量">{{ testResult.tableCount }}</el-descriptions-item>
          <el-descriptions-item label="字符集" :span="2">{{ testResult.charSet }}</el-descriptions-item>
        </el-descriptions>
      </div>

      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" @click="handleSave">保存</el-button>
      </template>
    </el-dialog>
  </div>
</template>
