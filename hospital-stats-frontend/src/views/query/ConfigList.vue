<script setup lang="ts">
import { ref, onMounted } from 'vue';
import { useRouter } from 'vue-router';
import { ElMessage, ElMessageBox } from 'element-plus';
import { queryApi, type QueryConfigItem } from '../../api/query';
import { dataSourcesApi, type DataSourceItem } from '../../api/datasources';

const router = useRouter();
const dataSources = ref<DataSourceItem[]>([]);
const selectedDsId = ref<number | null>(null);
const configs = ref<QueryConfigItem[]>([]);
const loading = ref(false);
const showTips = ref(false);

async function loadDataSources() {
  const res = await dataSourcesApi.getAll();
  dataSources.value = res.data;
}

async function loadConfigs() {
  loading.value = true;
  try {
    const res = await queryApi.getConfigs(selectedDsId.value ?? undefined);
    configs.value = res.data;
  } finally {
    loading.value = false;
  }
}

async function handleDelete(id: number, name: string) {
  try {
    await ElMessageBox.confirm(`确定删除配置 "${name}" 吗？`, '确认', { type: 'warning' });
    await queryApi.deleteConfig(id);
    ElMessage.success('已删除');
    await loadConfigs();
  } catch { /* cancelled */ }
}

function goCreate() {
  router.push('/query/configs/new');
}

function goSqlImport() {
  router.push('/query/configs/new?mode=sql');
}

function goEdit(id: number) {
  router.push(`/query/configs/${id}`);
}

function getDisplayTypeLabel(type: string) {
  const map: Record<string, string> = { table: '表格', number: '数字', bar: '柱状图', line: '折线图', pie: '饼图' };
  return map[type] || type;
}

onMounted(() => {
  loadDataSources();
  loadConfigs();
});
</script>

<template>
  <div>
    <div style="margin-bottom: 16px; display: flex; gap: 12px; align-items: center">
      <el-select v-model="selectedDsId" placeholder="筛选数据源" clearable @change="loadConfigs"
        style="width: 220px">
        <el-option v-for="ds in dataSources" :key="ds.id" :label="ds.name" :value="ds.id" />
      </el-select>
      <el-button type="primary" @click="goCreate">新增查询配置</el-button>
      <el-button @click="goSqlImport">从 SQL 导入</el-button>
      <el-button @click="showTips = true">配置说明</el-button>
    </div>

    <el-table :data="configs" v-loading="loading" border stripe>
      <el-table-column label="配置名称" min-width="200">
        <template #default="{ row }">
          {{ row.name }}
          <el-tag v-if="row.dashboardCardCount > 0" size="small" type="warning"
            style="margin-left: 6px" title="被仪表盘引用">仪</el-tag>
        </template>
      </el-table-column>
      <el-table-column prop="mainTableName" label="主表" min-width="150" />
      <el-table-column label="展示方式" width="100">
        <template #default="{ row }">
          {{ getDisplayTypeLabel(row.displayType) }}
        </template>
      </el-table-column>
      <el-table-column label="状态" width="80">
        <template #default="{ row }">
          <el-tag :type="row.isEnabled ? 'success' : 'info'" size="small">
            {{ row.isEnabled ? '启用' : '禁用' }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column label="更新时间" width="120">
        <template #default="{ row }">
          {{ row.updatedAt?.slice(0, 10) }}
        </template>
      </el-table-column>
      <el-table-column label="操作" width="180">
        <template #default="{ row }">
          <el-button size="small" @click="goEdit(row.id)">编辑</el-button>
          <el-button size="small" type="danger" @click="handleDelete(row.id, row.name)">删除</el-button>
        </template>
      </el-table-column>
    </el-table>
    <el-empty v-if="!loading && configs.length === 0" description="暂无查询配置" />

    <el-dialog v-model="showTips" title="查询配置说明" width="580px">
      <div class="tips-content">
        <div class="tip-item">
          <div class="tip-body">
            <div class="tip-title">基本配置</div>
            <div class="tip-text">选择数据源和主表后，配置要显示的字段（列）、筛选条件和 JOIN 关联表。</div>
          </div>
        </div>
        <div class="tip-item">
          <div class="tip-body">
            <div class="tip-title">SQL 导入</div>
            <div class="tip-text">粘贴 Oracle SQL 自动解析列、筛选、JOIN，支持 UNION 查询。导入后保存 RawSql 以保留原始语义。</div>
          </div>
        </div>
        <div class="tip-item">
          <div class="tip-body">
            <div class="tip-title">UNION 查询</div>
            <div class="tip-text">筛选条件按分支独立注入——门诊分支的日期筛选不会影响住院分支。需确保筛选器的表在对应分支中存在。</div>
          </div>
        </div>
        <div class="tip-item">
          <div class="tip-body">
            <div class="tip-title">US7ASCII 数据库</div>
            <div class="tip-text">数据源需设置"字符集覆盖"为 GBK。系统会自动对字符串列做 hex 编码/解码，确保中文正常显示。</div>
          </div>
        </div>
        <div class="tip-item">
          <div class="tip-body">
            <div class="tip-title">筛选操作符</div>
            <div class="tip-text">支持 =、≠、＞、＜、≥、≤、LIKE、NOT LIKE、IN、NOT IN、BETWEEN。IN 多值用逗号分隔。</div>
          </div>
        </div>
        <div class="tip-item">
          <div class="tip-body">
            <div class="tip-title">上下文筛选器</div>
            <div class="tip-text">勾选后按当前登录用户自动注入科室或用户 ID，前端不可见。</div>
          </div>
        </div>
        <div class="tip-item">
          <div class="tip-body">
            <div class="tip-title">仪表盘引用</div>
            <div class="tip-text">配置名称旁有 <el-tag size="small" type="warning">仪</el-tag> 标识表示已被仪表盘卡片引用，删除前需先取消引用。</div>
          </div>
        </div>
      </div>
    </el-dialog>
  </div>
</template>

<style scoped>
.tips-content { display: flex; flex-direction: column; gap: 18px; padding: 4px 16px 8px; }
.tip-title { font-size: 14px; font-weight: 600; color: #1e293b; margin-bottom: 2px; }
.tip-title::before { content: '• '; color: #00603D; font-weight: 700; }
.tip-text { font-size: 13px; color: #64748b; line-height: 1.7; padding-left: 12px; }
</style>
