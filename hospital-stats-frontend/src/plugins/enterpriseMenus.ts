/**
 * 企业版菜单 + 品牌注入 Hook。
 * 社区版 MainLayout 读取此状态渲染企业版菜单项和品牌标识。
 * 企业版插件通过赋值和 push 注入内容。
 */
import { reactive } from 'vue'

export interface EnterpriseMenuItem {
  label: string
  path: string
  icon?: string
  adminOnly?: boolean
}

export interface EnterpriseBranding {
  badge: string
  titleSuffix: string
  designBy: string   // 侧边栏 tooltip 作者署名
}

export const enterpriseMenuItems = reactive<EnterpriseMenuItem[]>([])
export const enterpriseBranding = reactive<EnterpriseBranding>({ badge: '', titleSuffix: '', designBy: '' })
