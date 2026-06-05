<script setup lang="ts">
import { ref, onMounted } from 'vue';
import { ElMessage, ElMessageBox } from 'element-plus';
import draggable from 'vuedraggable';
import { dashboardApi, type DashboardCardData } from '../../api/dashboard';
import { queryApi, type QueryConfigItem } from '../../api/query';

const cards = ref<DashboardCardData[]>([]);
const configs = ref<QueryConfigItem[]>([]);
const loading = ref(false);
const dialogVisible = ref(false);
const editingCard = ref<DashboardCardData | null>(null);
const form = ref({
  title: '', queryConfigId: 0, displayType: 'number',
  icon: '', color: '#00603D', unit: '', sortOrder: 0, width: 6, isEnabled: true,
});

async function loadData() {
  loading.value = true;
  try {
    const [cRes, qRes] = await Promise.all([dashboardApi.getCards(), queryApi.getConfigs()]);
    cards.value = cRes.data;
    configs.value = qRes.data;
  } finally {
    loading.value = false;
  }
}

function openDialog(card?: DashboardCardData) {
  if (card) {
    editingCard.value = card;
    form.value = {
      title: card.title, queryConfigId: card.queryConfigId, displayType: card.displayType,
      icon: card.icon || '', color: card.color || '#00603D', unit: card.unit || '',
      sortOrder: card.sortOrder, width: card.width, isEnabled: card.isEnabled,
    };
  } else {
    editingCard.value = null;
    form.value = { title: '', queryConfigId: 0, displayType: 'number', icon: '', color: '#00603D', unit: '', sortOrder: cards.value.length, width: 6, isEnabled: true };
  }
  dialogVisible.value = true;
}

async function saveCard() {
  if (!form.value.title || !form.value.queryConfigId) {
    ElMessage.warning('请填写标题和选择查询配置'); return;
  }
  try {
    if (editingCard.value) {
      await dashboardApi.updateCard(editingCard.value.id, form.value);
    } else {
      await dashboardApi.createCard(form.value);
    }
    ElMessage.success('已保存');
    dialogVisible.value = false;
    await loadData();
  } catch {
    ElMessage.error('保存失败');
  }
}

async function deleteCard(id: number) {
  try {
    await ElMessageBox.confirm('确定删除？', '确认', { type: 'warning' });
  } catch { return; }
  try {
    await dashboardApi.deleteCard(id);
    ElMessage.success('已删除');
    await loadData();
  } catch {
    ElMessage.error('删除失败');
  }
}

async function onDragEnd() {
  const ids = cards.value.map(c => c.id);
  try {
    await dashboardApi.updateOrder(ids);
    ElMessage.success('排序已更新');
  } catch {
    ElMessage.error('排序保存失败');
  }
}

function cardColor(card: DashboardCardData) { return card.color ?? '#00603D'; }
function getDisplayLabel(type: string) {
  const map: Record<string, string> = { number: '数值', bar: '柱状图', line: '折线图', pie: '饼图', table: '表格' };
  return map[type] || type;
}

const iconOptions = [
  { value: 'money', label: '💰 金额' },
  { value: 'people', label: '👥 人员' },
  { value: 'hospital', label: '🏥 医院' },
  { value: 'medicine', label: '💊 药品' },
  { value: 'chart', label: '📊 图表' },
  { value: 'calendar', label: '📅 日历' },
  { value: 'doc', label: '📋 文档' },
];
const unitOptions = ['人次', '人', '元', '张', '%', '例', '天', '次', '件'];

onMounted(loadData);
</script>

<template>
  <div>
    <div style="margin-bottom: 16px">
      <el-button type="primary" @click="openDialog()">新增卡片</el-button>
    </div>

    <draggable v-model="cards" item-key="id" handle=".drag-handle" @end="onDragEnd" class="config-grid">
      <template #item="{ element: card }">
        <div class="config-card">
          <div class="config-card-header">
            <span class="drag-handle" title="拖拽排序">⠿</span>
            <strong style="flex:1">{{ card.title }}</strong>
            <span>
              <el-button size="small" text @click="openDialog(card)">编辑</el-button>
              <el-button size="small" text type="danger" @click="deleteCard(card.id)">删除</el-button>
            </span>
          </div>
          <div style="font-size:12px; color:#909399; display:flex; align-items:center; gap:8px; flex-wrap:wrap">
            <span>{{ card.queryConfigName }} | {{ getDisplayLabel(card.displayType) }} | 宽度:{{ card.width }}/24</span>
            <span :style="{ display:'inline-block',width:'12px',height:'12px',borderRadius:'3px',background:cardColor(card),flexShrink:0 }" :title="cardColor(card)" />
            <el-tag :type="card.isEnabled ? 'success' : 'info'" size="small">
              {{ card.isEnabled ? '启用中' : '停用' }}
            </el-tag>
          </div>
        </div>
      </template>
    </draggable>

    <el-dialog v-model="dialogVisible" :title="editingCard ? '编辑卡片' : '新增卡片'" width="500px">
      <el-form :model="form" label-width="90px">
        <el-form-item label="标题" required>
          <el-input v-model="form.title" placeholder="如：今日门诊量" />
        </el-form-item>
        <el-form-item label="查询配置" required>
          <el-select v-model="form.queryConfigId" placeholder="选择查询" style="width:100%">
            <el-option v-for="c in configs" :key="c.id" :label="c.name" :value="c.id" />
          </el-select>
        </el-form-item>
        <el-form-item label="展示方式">
          <el-radio-group v-model="form.displayType">
            <el-radio value="number">数值</el-radio>
            <el-radio value="bar">柱状图</el-radio>
            <el-radio value="line">折线图</el-radio>
            <el-radio value="pie">饼图</el-radio>
          </el-radio-group>
        </el-form-item>
        <el-form-item label="图标">
          <el-select v-model="form.icon" placeholder="选择图标" clearable style="width:100%">
            <el-option v-for="ic in iconOptions" :key="ic.value" :label="ic.label" :value="ic.value" />
          </el-select>
        </el-form-item>
        <el-form-item label="颜色">
          <el-color-picker v-model="form.color" />
        </el-form-item>
        <el-form-item label="单位">
          <el-select v-model="form.unit" placeholder="选择单位" clearable filterable
            style="width:100%">
            <el-option v-for="u in unitOptions" :key="u" :label="u" :value="u" />
          </el-select>
        </el-form-item>
        <el-form-item label="宽度">
          <el-select v-model="form.width">
            <el-option :value="6" label="1/4 (6)" />
            <el-option :value="8" label="1/3 (8)" />
            <el-option :value="12" label="1/2 (12)" />
            <el-option :value="18" label="3/4 (18)" />
            <el-option :value="24" label="整行 (24)" />
          </el-select>
        </el-form-item>
        <el-form-item label="启用">
          <el-switch v-model="form.isEnabled" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" @click="saveCard">保存</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.config-grid {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 12px;
}
.config-card {
  background: white; padding: 12px; border-radius: 4px;
  border: 1px solid #ebeef5;
}
.config-card-header {
  display: flex; justify-content: space-between; align-items: center;
  margin-bottom: 4px;
}
.drag-handle {
  cursor: grab; color: #c0c4cc; font-size: 16px; margin-right: 6px; user-select: none;
}
.drag-handle:active { cursor: grabbing; }
</style>
