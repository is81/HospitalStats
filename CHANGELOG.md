# Changelog

## v2.0 (2026-06)

### 新增
- 开源（MIT License）+ 中英双语 README
- 系统配置管理页面（查询超时、行数限制、仪表盘参数在线修改）
- 操作审计日志（自动记录到 `logs/audit-{date}.log`）
- 仪表盘卡片拖拽排序
- 仪表盘日期筛选栏与日期列可配置
- 查询配置启用/禁用开关
- 菜单禁用灰显（保留入口但阻止查询）
- 查询行数超限弹窗提醒
- 部署脚本 `build.ps1` + `install.ps1`（Windows Service 自动部署）
- SQLite 配置库定时备份服务

### 改进
- 主色调改为 Teal `#0d9488`
- 仪表盘 UI 重构（日期标签、说明文字、控件整合）
- 操作符下拉显示符号（= ≠ ＞ ＜ ≥ ≤）
- 版本信息对话框美化
- 前端菜单图标渲染 Element Plus 图标
- 部署包清理（排除 logo、backups、logs、Development 配置）
- 连接串使用绝对路径防 System32 误解析

### 安全
- 管理员密码随机生成
- 生产环境强制要求 JWT/Encryption 密钥
- 授权模块完全移除

### 测试
- 测试从 155 扩展到 184（新增 DataSourceService + SystemSettingsService）

---

## v1.0 (2026-05)

### 核心
- 医院数据查询与统计平台初始版本
- Vue 3 + Vite 前端 / .NET 8 Web API 后端
- Oracle 10g/11g 数据源 + SQLite 配置库双数据库架构
- JWT Bearer 认证 + BCrypt 密码哈希 + RBAC 权限控制

### 功能
- UNION 复杂查询全链路（导入 → 分支筛选注入 → 中文别名安全化 → hex 编码）
- Oracle US7ASCII 字符集完整适配（列数据/标识符/行内字面量三层防护）
- 14 种筛选操作符（含 NOT LIKE / NOT IN / NOT BETWEEN）
- RawSql 导入解析（SELECT/FROM/WHERE/JOIN/GROUP BY/ORDER BY 自动提取）
- 仪表盘卡片 + ECharts 图表（数值/柱状图/折线图/饼图）
- 数据源管理（AES-CBC 加密连接串 + 在线测试连接）
- 元数据扫描（Oracle Schema 自动发现）
- Oracle 10g ROWNUM 三层分页
- 查询结果 Excel 导出
- 上下文筛选器（DeptName / UserId 自动注入）
- 浏览器兼容（Chrome 64+ / Firefox 67+ / Safari 12+ / Edge 79+）
