# 贡献指南 · Contributing Guide

感谢你考虑为本项目做出贡献！

> **社区版处于维护模式**：仅接受 Bug 修复和文档改进。新功能开发请移至企业版（联系项目所有者）。

## CLA 签署（必须）

在提交 Pull Request 之前，你必须同意[贡献者许可协议 (CLA)](CLA.md)。只需在 PR 描述中包含以下声明：

```
I have read and agree to the Contributor License Agreement (CLA.md).
```

未包含此声明的 PR 将不会被合并。

## 开发环境搭建

### 前置要求

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Node.js 18+](https://nodejs.org/)
- Oracle 数据库（可选，用于测试数据源连接；没有 Oracle 环境的可在测试项目中编写单元测试）

### 克隆与运行

```bash
git clone <repo-url>
cd HospitalStats

# 后端
cd HospitalStats.Backend/HospitalStats.Api
dotnet run
# 监听 http://localhost:5000

# 前端（新终端）
cd hospital-stats-frontend
npm install
npm run dev
# 监听 http://localhost:5173，/api 自动代理到 localhost:5000
```

### 运行测试

```bash
# 从项目根目录
dotnet test HospitalStats.Backend/HospitalStats.Api.Tests --no-restore

# 或从测试项目目录
cd HospitalStats.Backend/HospitalStats.Api.Tests
dotnet test
```

共 180 个单元测试，覆盖 QueryExecutionService、SqlParsingService、DataSourceService、SystemSettingsService。

### 快速编译检查

```bash
# 后端
cd HospitalStats.Backend/HospitalStats.Api
dotnet build --no-restore

# 前端（类型检查 + 构建）
cd hospital-stats-frontend
npm run build
```

## 分支策略

| 分支 | 用途 |
|------|------|
| `master` | 稳定发布分支，只接受 PR 合并 |
| `feat/<功能名>` | 新功能开发 |
| `fix/<问题描述>` | Bug 修复 |
| `refactor/<描述>` | 代码重构 |

## Pull Request 流程

1. **开 Issue 先行**（Bug 修复）或讨论（新功能）——避免做了工作却被拒绝
2. **Fork 仓库**，从 `master` 创建特性分支
3. **编写代码**，确保通过现有测试，新功能需加测试
4. **运行 `npm run build`**（前端类型检查）和 **`dotnet test`**（后端测试），确保全部通过
5. **提交 PR**，描述中包含：
   - 做了什么、为什么
   - 关联的 Issue 编号
   - CLA 签署声明
   - 截图（如有 UI 变更）
6. 等待 Review，CI 必须全部通过

## 代码风格

### 后端（C# / .NET 8）

- 遵循项目现有风格：PascalCase 方法名，camelCase 局部变量，`_camelCase` 私有字段
- Controller → Service → EF Core 分层，业务逻辑放 Service 层
- 异步方法以 `Async` 结尾
- DTO 类放在 `DTOs/` 目录下，用 `<ClassName>Dto` 后缀
- Oracle SQL 参数化查询，禁止字符串拼接

### 前端（Vue 3 / TypeScript）

- 使用 Composition API（`<script setup lang="ts">`）
- Pinia store 按功能模块拆分（`auth.ts`、`dashboard.ts` 等）
- 组件文件 PascalCase（`ConfigEdit.vue`），工具文件 camelCase（`api/index.ts`）
- 中文注释优先（项目面向国内医院用户）
- 遵循项目设计规范：Teal `#0d9488` 主色调，详见 `src/styles/theme.css`

### 通用规则

- 提交信息使用中文（本项目面向国内开发者），格式：`类型: 描述`
  - 例如：`feat: 添加 DRG 医保分析模块`、`fix: 修复 US7ASCII 下中文别名冲突`
- 一个提交做一件事
- 不要在 PR 中包含 `config.db`、`appsettings*.json`、`backups/`、`logs/` 目录下的文件

## 文档

- 调研文档、技术总结等保存到 `docs/` 目录
- `docs/` 目录不上传 Git（已在 `.gitignore` 中排除）
- `CLAUDE.md` 是 Claude Code 的项目上下文文件，修改前需谨慎

## 行为准则

- 尊重所有贡献者和用户
- 建设性的代码 Review，对事不对人
- 本项目面向医疗行业，代码质量直接影响患者服务质量，请保持高标准

## 问题反馈

- **Bug 报告**：开 Issue，附上错误日志、复现步骤、环境信息（Oracle 版本、字符集）
- **功能建议**：开 Issue，描述使用场景和期望行为
- **安全漏洞**：请**不要**公开开 Issue，直接联系项目所有者

## 致谢

每位贡献者都将被记录在项目的致谢列表中。你的每一行代码都在帮助中国医院提升数据利用能力。
