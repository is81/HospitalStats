/**
 * 企业版菜单 + 品牌注入 Hook。
 * 社区版 MainLayout 读取此状态渲染企业版菜单项和品牌标识。
 * 企业版插件通过赋值和 push 注入内容。
 */
import { reactive, computed } from 'vue'

export interface EnterpriseMenuItem {
  label: string
  path?: string
  icon?: string
  adminOnly?: boolean
  children?: EnterpriseMenuItem[]   // 子菜单 → el-sub-menu
  group?: 'top' | 'system'          // top=顶层菜单, system=系统管理子菜单（默认）
}

export interface EnterpriseBranding {
  badge: string
  titleSuffix: string
  designBy: string
}

export const enterpriseMenuItems = reactive<EnterpriseMenuItem[]>([])
export const enterpriseBranding = reactive<EnterpriseBranding>({ badge: '', titleSuffix: '', designBy: '' })

/** 顶层企业版菜单项（数据分析、大屏展示等） */
export const topEnterpriseItems = computed(() =>
  enterpriseMenuItems.filter(i => i.group === 'top')
)
/** 系统管理内的企业版菜单项（多院区、SSO、授权等） */
export const systemEnterpriseItems = computed(() =>
  enterpriseMenuItems.filter(i => i.group !== 'top')
)
